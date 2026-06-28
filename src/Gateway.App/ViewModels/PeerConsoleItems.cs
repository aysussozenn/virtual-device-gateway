using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using Gateway.Icd;
using Gateway.Peers;

namespace Gateway.App.ViewModels;

/// <summary>
/// Common face of a message in the catalog: enough to render a clickable chip and to toggle
/// the message in/out of the workspace. Both the editable outgoing card and the incoming
/// monitor implement it, so one catalog lists them all.
/// </summary>
public interface IMessageCardVm : INotifyPropertyChanged
{
    string Name { get; }
    string CommandText { get; }
    string Group { get; }       // catalog section header ("Periodic ↑" / "Aperiodic ⚡" / "Incoming ↓")
    bool IsOutgoing { get; }
    bool IsOpen { get; set; }   // shown in the workspace when true
}

/// <summary>Formatting + range helpers shared by the generated field controls.</summary>
internal static class FieldFormat
{
    public static string Display(IcdField f, double v)
    {
        if (f.Enum is { } e && e.TryGetValue((long)v, out var label))
            return $"{(long)v} ({label})";
        var s = f.Type.Format(v);
        return string.IsNullOrEmpty(f.Unit) ? s : $"{s} {f.Unit}";
    }

    /// <summary>Slider bounds: explicit ICD min/max if present, else the type's natural range.
    /// Returns null when there is no sensible bounded range (unbounded float) → text entry only.</summary>
    public static (double Min, double Max)? Range(IcdField f)
    {
        if (f.Min is { } lo && f.Max is { } hi) return (lo, hi);
        return f.Type.NaturalRange;
    }
}

/// <summary>One option in an enumerated field's dropdown.</summary>
public sealed class EnumOptionVm
{
    public required long Key { get; init; }
    public required string Label { get; init; }
    public string Display => $"{Key} ({Label})";
}

/// <summary>One byte of the live wire preview; <see cref="Changed"/> drives highlight on edit.</summary>
public sealed class HexCellVm : ObservableObject
{
    private bool _changed;
    public required string Text { get; init; }
    public bool Changed { get => _changed; set => Set(ref _changed, value); }
}

/// <summary>An editable outgoing field (peer → DUT). Numeric fields show a bounded slider; enum
/// fields a dropdown. Any change notifies the owning message to rebuild its snapshot + hex.</summary>
public sealed class FieldEditVm : ObservableObject
{
    private readonly IcdField _field;
    private readonly Action _onChanged;
    private double _value;
    private EnumOptionVm? _selectedEnum;

    public FieldEditVm(IcdField field, Action onChanged)
    {
        _field = field;
        _onChanged = onChanged;
        _value = field.Min ?? 0;

        if (field.Enum is { Count: > 0 })
        {
            foreach (var kv in field.Enum)
                EnumOptions.Add(new EnumOptionVm { Key = kv.Key, Label = kv.Value });
            _selectedEnum = EnumOptions.FirstOrDefault();
            _value = _selectedEnum?.Key ?? 0;
        }

        if (EnumOptions.Count == 0 && FieldFormat.Range(field) is { } r)
        {
            SliderMin = r.Min;
            SliderMax = r.Max;
            HasRange = true;
        }
    }

    public string Name => _field.Name;
    public string TypeText => _field.Type.Name;
    public string Unit => _field.Unit;
    public bool IsEnum => EnumOptions.Count > 0;
    public bool HasRange { get; }
    public bool IsNumeric => !IsEnum;

    /// <summary>Editor key the template selector resolves to "editor.&lt;widget&gt;". Explicit ICD hint
    /// wins; otherwise derived from the field shape (enum → dropdown, bounded → slider, else text).</summary>
    public string Widget => !string.IsNullOrWhiteSpace(_field.Widget) ? _field.Widget!
        : IsEnum ? "enum" : HasRange ? "slider" : "text";
    public double SliderMin { get; }
    public double SliderMax { get; }

    public ObservableCollection<EnumOptionVm> EnumOptions { get; } = new();

    public double Value
    {
        get => _value;
        set { if (Set(ref _value, value)) _onChanged(); }
    }

    public EnumOptionVm? SelectedEnum
    {
        get => _selectedEnum;
        set
        {
            if (!Set(ref _selectedEnum, value) || value is null) return;
            Value = value.Key;
        }
    }
}

/// <summary>A read-only incoming field (DUT → peer), shown as a live label.</summary>
public sealed class FieldLabelVm : ObservableObject
{
    private string _value = "—";
    public required string Name { get; init; }
    public string Value { get => _value; set => Set(ref _value, value); }
}

/// <summary>
/// One generated outgoing-message card. Sampling messages get a streaming toggle (the periodic
/// push); others get a Send button (aperiodic one-shot). The live hex strip shows exactly which
/// wire bytes a field edit changes.
/// </summary>
public sealed class OutgoingMessageVm : ObservableObject, IMessageCardVm, IDisposable
{
    private readonly PeerChannel _channel;
    private readonly IcdMessage _message;
    private readonly PeerStream? _stream;
    private byte[] _prevBytes = Array.Empty<byte>();
    private bool _isStreaming;
    private bool _isOpen;
    private ushort _seq;

    public OutgoingMessageVm(PeerChannel channel, IcdMessage message)
    {
        _channel = channel;
        _message = message;
        IsSampling = message.Port == PortKind.Sampling;

        foreach (var f in message.Fields)
            Fields.Add(new FieldEditVm(f, OnFieldChanged));

        if (IsSampling)
            _stream = new PeerStream(channel, message, message.RefreshMs ?? 100);

        SendCommand = new RelayCommand(SendOnce);
        Rebuild();
    }

    public string Name => _message.Name;
    public string CommandText => $"0x{_message.Command:X4}";
    public bool IsSampling { get; }
    public bool IsAperiodic => !IsSampling;

    // IMessageCardVm
    public string Group => IsSampling ? "Periodic ↑" : "Aperiodic ⚡";
    public bool IsOutgoing => true;
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }
    public string PortText => IsSampling
        ? $"sampling · {_message.RefreshMs ?? 100} ms"
        : _message.Port == PortKind.Queuing ? "queuing · aperiodic" : "aperiodic";

    public ObservableCollection<FieldEditVm> Fields { get; } = new();
    public ObservableCollection<HexCellVm> WireHex { get; } = new();
    public RelayCommand SendCommand { get; }

    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            if (value) { Rebuild(); _stream?.Start(); }
            else _stream?.Stop();
        }
    }

    private void OnFieldChanged() => Rebuild();

    private IReadOnlyDictionary<string, double> CurrentValues()
        => Fields.ToDictionary(f => f.Name, f => f.Value);

    /// <summary>Publish the latest values to the stream and refresh the hex preview.</summary>
    private void Rebuild()
    {
        var values = CurrentValues();
        _stream?.UpdateValues(new FieldSnapshot(values));
        UpdateHex(_channel.Encode(_message, values, _seq));
    }

    private void SendOnce()
    {
        _channel.Send(_message, CurrentValues(), _seq++);
        Rebuild();
    }

    private void UpdateHex(byte[] bytes)
    {
        WireHex.Clear();
        for (var i = 0; i < bytes.Length; i++)
        {
            var changed = i >= _prevBytes.Length || _prevBytes[i] != bytes[i];
            WireHex.Add(new HexCellVm { Text = bytes[i].ToString("X2"), Changed = changed });
        }
        _prevBytes = bytes;
    }

    public void Dispose() => _stream?.Dispose();
}

/// <summary>One generated incoming-message monitor (DUT → peer): live field labels + freshness.</summary>
public sealed class IncomingMessageVm : ObservableObject, IMessageCardVm
{
    private readonly IcdMessage _message;
    private readonly Dictionary<string, FieldLabelVm> _byName = new();
    private long _lastSeenMs;
    private bool _hasData;
    private bool _isStale;
    private bool _isOpen;
    private string _lastSeq = "—";

    public IncomingMessageVm(IcdMessage message)
    {
        _message = message;
        foreach (var f in message.Fields)
        {
            var vm = new FieldLabelVm { Name = f.Name };
            _byName[f.Name] = vm;
            Fields.Add(vm);
        }
    }

    public string Name => _message.Name;
    public string CommandText => $"0x{_message.Command:X4}";
    public ObservableCollection<FieldLabelVm> Fields { get; } = new();

    // IMessageCardVm
    public string Group => "Incoming ↓";
    public bool IsOutgoing => false;
    public bool IsOpen { get => _isOpen; set => Set(ref _isOpen, value); }

    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }
    public bool IsStale { get => _isStale; private set => Set(ref _isStale, value); }
    public string LastSeq { get => _lastSeq; private set => Set(ref _lastSeq, value); }

    /// <summary>Apply a freshly decoded frame (called on the UI thread).</summary>
    public void Update(DecodedMessage m, long tMs)
    {
        foreach (var f in _message.Fields)
            if (_byName.TryGetValue(f.Name, out var vm) && m.Fields.TryGetValue(f.Name, out var v))
                vm.Value = FieldFormat.Display(f, v);

        _lastSeenMs = tMs;
        LastSeq = m.Sequence.ToString(CultureInfo.InvariantCulture);
        HasData = true;
        IsStale = false;
    }

    /// <summary>Mark stale if no frame arrived within ~3× the declared refresh window.</summary>
    public void RefreshFreshness(long nowMs)
    {
        if (!HasData) return;
        var window = (_message.RefreshMs ?? 500) * 3L;
        IsStale = nowMs - _lastSeenMs > window;
    }
}

/// <summary>
/// One peer's generated tab: outgoing (editable) and incoming (monitor) cards, split by message
/// direction. Built entirely from the peer's ICD — no per-peer UI code.
/// </summary>
public sealed class PeerTabViewModel : ObservableObject, IDisposable
{
    private readonly PeerChannel _channel;
    private readonly Dictionary<ushort, IncomingMessageVm> _incomingByCmd = new();
    private readonly List<IMessageCardVm> _cards = new();
    private readonly List<OutgoingMessageVm> _outgoing = new();

    public PeerTabViewModel(PeerChannel channel, string mac, IReadOnlySet<string>? allowed)
    {
        _channel = channel;
        Id = channel.PeerId;
        Ip = channel.PeerIp.ToString();
        Mac = mac;

        // Order matters: it drives the catalog's group order (Periodic, Aperiodic, then Incoming).
        var ordered = channel.Spec.Messages
            .Where(m => allowed is not { Count: > 0 } || allowed.Contains(m.Name))
            .OrderBy(m => m.Direction == Direction.Inbound ? 0 : 1)
            .ThenBy(m => m.Direction == Direction.Inbound && m.Port != PortKind.Sampling ? 1 : 0);

        foreach (var msg in ordered)
        {
            if (msg.Direction == Direction.Inbound) // DUT receives → peer sends → editable
            {
                var vm = new OutgoingMessageVm(channel, msg);
                _outgoing.Add(vm);
                AddCard(vm);
            }
            else // DUT sends → peer receives → monitor
            {
                var vm = new IncomingMessageVm(msg);
                _incomingByCmd[msg.Command] = vm;
                AddCard(vm);
            }
        }

        Catalog = CollectionViewSource.GetDefaultView(_cards);
        Catalog.GroupDescriptions.Add(new PropertyGroupDescription(nameof(IMessageCardVm.Group)));

        CloseCardCommand = new RelayCommand<IMessageCardVm>(c => { if (c is not null) c.IsOpen = false; });

        // Open the first message so the workspace is not empty on first view.
        if (_cards.Count > 0) _cards[0].IsOpen = true;

        channel.MessageDecoded += OnDecoded;
    }

    public string Id { get; }
    public string Ip { get; }
    public string Mac { get; }

    /// <summary>All messages as clickable catalog chips, grouped by section.</summary>
    public ICollectionView Catalog { get; }

    /// <summary>The messages the user has clicked open — the working set shown as full cards.</summary>
    public ObservableCollection<IMessageCardVm> Workspace { get; } = new();

    /// <summary>Closes a card from the workspace (the ✕ on a card); the chip un-highlights too.</summary>
    public RelayCommand<IMessageCardVm> CloseCardCommand { get; }

    private void AddCard(IMessageCardVm card)
    {
        _cards.Add(card);
        card.PropertyChanged += OnCardPropertyChanged;
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(IMessageCardVm.IsOpen) || sender is not IMessageCardVm card) return;
        if (card.IsOpen)
        {
            if (!Workspace.Contains(card)) Workspace.Add(card);
        }
        else
        {
            Workspace.Remove(card);
        }
    }

    private void OnDecoded(DecodedMessage m, long tMs)
    {
        if (!_incomingByCmd.TryGetValue(m.Definition.Command, out var vm)) return;
        Application.Current?.Dispatcher.BeginInvoke(() => vm.Update(m, tMs));
    }

    public void RefreshFreshness(long nowMs)
    {
        foreach (var vm in _incomingByCmd.Values)
            vm.RefreshFreshness(nowMs);
    }

    public void Dispose()
    {
        _channel.MessageDecoded -= OnDecoded;
        foreach (var card in _cards) card.PropertyChanged -= OnCardPropertyChanged;
        foreach (var o in _outgoing) o.Dispose();
    }
}
