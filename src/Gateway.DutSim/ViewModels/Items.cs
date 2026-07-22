using System.Collections.ObjectModel;
using System.Globalization;
using Gateway.Icd;
using Gateway.Peers;
using SharpPcap;

namespace Gateway.DutSim.ViewModels;

/// <summary>A capture adapter shown in the dropdown.</summary>
public sealed class AdapterItem
{
    public AdapterItem(ILiveDevice device) => Device = device;
    public ILiveDevice Device { get; }
    public string Display => Device.Description ?? Device.Name;
    public override string ToString() => Display;
}

/// <summary>Formatting helpers shared by the generated field controls.</summary>
internal static class FieldFormat
{
    public static string Display(IcdField f, double v)
    {
        if (f.Enum is { } e && e.TryGetValue((long)v, out var label))
            return $"{(long)v} ({label})";
        var s = f.Type.Format(v);
        return string.IsNullOrEmpty(f.Unit) ? s : $"{s} {f.Unit}";
    }

    public static string EnumHint(IcdField f)
        => f.Enum is { Count: > 0 } e
            ? "enum: " + string.Join(", ", e.Select(kv => $"{kv.Key}={kv.Value}"))
            : string.Empty;
}

/// <summary>One editable field of a DUT → peer message. Value is a raw double the codec packs.</summary>
public sealed class SendFieldVm : ObservableObject
{
    private readonly IcdField _field;
    private readonly Action _onChanged;
    private double _value;

    public SendFieldVm(IcdField field, Action onChanged)
    {
        _field = field;
        _onChanged = onChanged;
        _value = field.Enum is { Count: > 0 } e ? e.Keys.First() : field.Min ?? 0;
    }

    public string Name => _field.Name;
    public string TypeText => _field.Type.Name;
    public string Unit => _field.Unit;
    public string EnumHint => FieldFormat.EnumHint(_field);
    public bool HasHint => EnumHint.Length > 0;

    public double Value
    {
        get => _value;
        set { if (Set(ref _value, value)) _onChanged(); }
    }
}

/// <summary>
/// One DUT → peer message the user can edit and transmit. Mirrors the peer console's outgoing
/// card, but from the DUT's side of the wire. A sampling message can be streamed periodically
/// (Stream toggle) AND still sent one-shot; a queuing/aperiodic message is one-shot only.
/// </summary>
public sealed class SendMessageVm : ObservableObject, IDisposable
{
    private readonly DutEndpoint _endpoint;
    private readonly string _peerId;
    private readonly IcdMessage _message;
    private readonly Action<string> _log;
    private readonly DutStream? _stream;
    private ushort _seq;
    private string _hexPreview = "";
    private bool _isStreaming;

    public SendMessageVm(DutEndpoint endpoint, string peerId, IcdMessage message, Action<string> log)
    {
        _endpoint = endpoint;
        _peerId = peerId;
        _message = message;
        _log = log;
        IsSampling = message.Port == PortKind.Sampling;

        foreach (var f in message.Fields)
            Fields.Add(new SendFieldVm(f, OnFieldChanged));

        if (IsSampling)
            _stream = new DutStream(endpoint, peerId, message, message.RefreshMs ?? 100);

        SendCommand = new RelayCommand(Send);
        OnFieldChanged();
    }

    public string Name => _message.Name;
    public string CommandText => $"0x{_message.Command:X4}";
    public bool IsSampling { get; }
    public string PortText => IsSampling ? $"sampling · {_message.RefreshMs ?? 100} ms" : "aperiodic";
    public override string ToString() => $"{Name}  ({CommandText})";

    public ObservableCollection<SendFieldVm> Fields { get; } = new();
    public RelayCommand SendCommand { get; }

    public string HexPreview { get => _hexPreview; private set => Set(ref _hexPreview, value); }

    /// <summary>When on, a <see cref="DutStream"/> pushes the latest field values every refreshMs.</summary>
    public bool IsStreaming
    {
        get => _isStreaming;
        set
        {
            if (!Set(ref _isStreaming, value)) return;
            if (value)
            {
                OnFieldChanged();
                _stream?.Start();
                _log($"⟳ {_peerId}  {Name} streaming every {_message.RefreshMs ?? 100} ms");
            }
            else
            {
                _stream?.Stop();
                _log($"⏹ {_peerId}  {Name} stream stopped");
            }
        }
    }

    private IReadOnlyDictionary<string, double> Values()
        => Fields.ToDictionary(f => f.Name, f => f.Value);

    /// <summary>Publish the latest values to the stream (if any) and refresh the hex preview.</summary>
    private void OnFieldChanged()
    {
        var values = Values();
        _stream?.UpdateValues(new FieldSnapshot(values));
        HexPreview = Convert.ToHexString(_endpoint.Encode(_peerId, _message, values, _seq));
    }

    private void Send()
    {
        var values = Values();
        _endpoint.Send(_peerId, _message, values, _seq);
        var shown = string.Join(", ", values.Select(kv => $"{kv.Key}={kv.Value:0.###}"));
        _log($"→ {_peerId}  {Name} (seq {_seq}) [{shown}]");
        _seq++;
        OnFieldChanged();
    }

    public void Dispose() => _stream?.Dispose();
}

/// <summary>A read-only incoming field (peer → DUT), shown as a live label.</summary>
public sealed class MonitorFieldVm : ObservableObject
{
    private string _value = "—";
    public required string Name { get; init; }
    public string Value { get => _value; set => Set(ref _value, value); }
}

/// <summary>One peer → DUT message monitor: live field values + last sequence + hit count.</summary>
public sealed class MonitorMessageVm : ObservableObject
{
    private readonly IcdMessage _message;
    private readonly Dictionary<string, MonitorFieldVm> _byName = new();
    private string _lastSeq = "—";
    private int _count;
    private bool _hasData;

    public MonitorMessageVm(IcdMessage message)
    {
        _message = message;
        foreach (var f in message.Fields)
        {
            var vm = new MonitorFieldVm { Name = f.Name };
            _byName[f.Name] = vm;
            Fields.Add(vm);
        }
    }

    public string Name => _message.Name;
    public string CommandText => $"0x{_message.Command:X4}";
    public ObservableCollection<MonitorFieldVm> Fields { get; } = new();

    public string LastSeq { get => _lastSeq; private set => Set(ref _lastSeq, value); }
    public int Count { get => _count; private set => Set(ref _count, value); }
    public bool HasData { get => _hasData; private set => Set(ref _hasData, value); }

    public void Update(DecodedMessage m)
    {
        foreach (var f in _message.Fields)
            if (_byName.TryGetValue(f.Name, out var vm) && m.Fields.TryGetValue(f.Name, out var v))
                vm.Value = FieldFormat.Display(f, v);

        LastSeq = m.Sequence.ToString(CultureInfo.InvariantCulture);
        Count++;
        HasData = true;
    }
}

/// <summary>
/// One peer's DUT-side panel: the messages the DUT can send to that peer (outbound, editable) and
/// the messages the DUT receives from it (inbound, monitored). Built entirely from the peer's ICD.
/// </summary>
public sealed class PeerPanelVm : ObservableObject, IDisposable
{
    private readonly Dictionary<ushort, MonitorMessageVm> _monitorsByCmd = new();
    private SendMessageVm? _selectedSend;

    public PeerPanelVm(DutEndpoint endpoint, PeerDescriptor desc, Action<string> log)
    {
        Id = desc.Id;
        Ip = desc.Ip.ToString();
        Mac = desc.Mac.ToString();

        foreach (var msg in desc.Spec.Messages)
        {
            if (msg.Direction == Direction.Outbound) // DUT sends → peer receives → editable
            {
                Sends.Add(new SendMessageVm(endpoint, desc.Id, msg, log));
            }
            else // peer sends → DUT receives → monitor
            {
                var vm = new MonitorMessageVm(msg);
                _monitorsByCmd[msg.Command] = vm;
                Monitors.Add(vm);
            }
        }
        _selectedSend = Sends.FirstOrDefault();
    }

    public string Id { get; }
    public string Ip { get; }
    public string Mac { get; }

    public ObservableCollection<SendMessageVm> Sends { get; } = new();
    public ObservableCollection<MonitorMessageVm> Monitors { get; } = new();

    public SendMessageVm? SelectedSend
    {
        get => _selectedSend;
        set => Set(ref _selectedSend, value);
    }

    /// <summary>Apply a freshly decoded peer → DUT frame (called on the UI thread).</summary>
    public void OnReceived(DecodedMessage m)
    {
        if (_monitorsByCmd.TryGetValue(m.Definition.Command, out var vm))
            vm.Update(m);
    }

    public void Dispose()
    {
        foreach (var s in Sends) s.Dispose();
    }
}
