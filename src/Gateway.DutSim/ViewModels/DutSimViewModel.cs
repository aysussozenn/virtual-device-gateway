using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows;
using Gateway.Ethernet;
using Gateway.Icd;
using Gateway.Peers;
using Microsoft.Win32;

namespace Gateway.DutSim.ViewModels;

/// <summary>
/// Drives the DUT simulator — the second GUI. It joins the same capture adapter as the peer
/// console, loads the same system topology, and stands in for the developer's real code (the DUT):
/// it monitors every peer → DUT message and can send DUT → peer messages by hand. With Auto-reply
/// on, an <see cref="EchoDut"/> answers each inbound message automatically.
/// </summary>
public sealed class DutSimViewModel : ObservableObject
{
    private const int MaxLogLines = 500;

    private SystemTopology? _topology;
    private IPacketTransport? _transport;
    private DutEndpoint? _endpoint;
    private EchoDut? _echo;
    private List<PeerDescriptor> _descriptors = new();
    private IPAddress _dutIp = IPAddress.Loopback;
    private PhysicalAddress _dutMac = PhysicalAddress.Parse("02-00-00-00-00-01");

    private string _systemPath;
    private string _status = "Select a system.json (same one the peer console uses), then Load.";
    private bool _isRunning;
    private bool _autoReply = true;
    private AdapterItem? _selectedAdapter;

    public DutSimViewModel()
    {
        // Optional launch-time overrides: point at a scenario (GATEWAY_SYSTEM) and start
        // capturing on the default loopback adapter (GATEWAY_AUTOSTART=1) with no clicks.
        // Absent env vars keep the old defaults.
        _systemPath = Environment.GetEnvironmentVariable("GATEWAY_SYSTEM") is { Length: > 0 } sys
            ? sys
            : Path.Combine(AppContext.BaseDirectory, "examples", "abc", "system.json");

        RefreshAdaptersCommand = new RelayCommand(RefreshAdapters);
        BrowseCommand = new RelayCommand(Browse);
        LoadCommand = new RelayCommand(Load);
        StartCommand = new RelayCommand(Start, () => _topology is not null && !IsRunning && SelectedAdapter is not null);
        StopCommand = new RelayCommand(Stop, () => IsRunning);

        RefreshAdapters();
        if (File.Exists(_systemPath)) Load();
        if (Environment.GetEnvironmentVariable("GATEWAY_AUTOSTART") == "1" && StartCommand.CanExecute(null))
            Start();
    }

    public ObservableCollection<AdapterItem> Adapters { get; } = new();
    public ObservableCollection<PeerPanelVm> Peers { get; } = new();
    public ObservableCollection<string> Log { get; } = new();

    public RelayCommand RefreshAdaptersCommand { get; }
    public RelayCommand BrowseCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    public string SystemPath { get => _systemPath; set => Set(ref _systemPath, value); }
    public string DutStatus { get => _status; private set => Set(ref _status, value); }

    public AdapterItem? SelectedAdapter
    {
        get => _selectedAdapter;
        set { if (Set(ref _selectedAdapter, value)) StartCommand.RaiseCanExecuteChanged(); }
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!Set(ref _isRunning, value)) return;
            StartCommand.RaiseCanExecuteChanged();
            StopCommand.RaiseCanExecuteChanged();
        }
    }

    /// <summary>When on, an <see cref="EchoDut"/> auto-answers inbound messages. Live-toggleable.</summary>
    public bool AutoReply
    {
        get => _autoReply;
        set
        {
            if (!Set(ref _autoReply, value) || !IsRunning || _transport is null) return;
            if (value)
            {
                _echo = new EchoDut(_transport, _descriptors, _dutIp, _dutMac);
                AppendLog("(auto-reply enabled)");
            }
            else
            {
                _echo?.Dispose();
                _echo = null;
                AppendLog("(auto-reply disabled)");
            }
        }
    }

    private void RefreshAdapters()
    {
        Adapters.Clear();
        try
        {
            foreach (var d in AdapterDiscovery.List())
                Adapters.Add(new AdapterItem(d));
        }
        catch (Exception ex)
        {
            DutStatus = $"Adapter discovery failed (Npcap needed): {ex.Message}";
        }
        SelectedAdapter ??= Adapters.FirstOrDefault(a =>
            a.Display.Contains("loopback", StringComparison.OrdinalIgnoreCase)) ?? Adapters.FirstOrDefault();
    }

    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select system topology (system.json)",
            Filter = "ICD topology (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(SystemPath)) ? Path.GetDirectoryName(SystemPath) : null
        };
        if (dlg.ShowDialog() == true) { SystemPath = dlg.FileName; Load(); }
    }

    private void Load()
    {
        if (IsRunning) Stop();
        try
        {
            _topology = IcdLoader.LoadTopology(SystemPath);
            var peers = _topology.Neighbours().ToList();
            DutStatus = $"Loaded {peers.Count} peer(s). This app answers as DUT '{_topology.Dut}' ({_topology.DutIp}). Press Start.";
        }
        catch (IcdLoadException ex)
        {
            _topology = null;
            DutStatus = $"Load error: {ex.Message}";
        }
        StartCommand.RaiseCanExecuteChanged();
    }

    private void Start()
    {
        if (_topology is null || SelectedAdapter is null) return;
        try
        {
            _descriptors = BuildDescriptors(_topology);
            _dutIp = IPAddress.Parse(_topology.DutIp);
            _dutMac = ParseMac(_topology.DutMac);

            _transport = new PcapTransport(SelectedAdapter.Device);
            _endpoint = new DutEndpoint(_transport, _descriptors, _dutIp, _dutMac);
            _endpoint.MessageReceived += OnPeerMessage;
            if (AutoReply)
                _echo = new EchoDut(_transport, _descriptors, _dutIp, _dutMac);

            foreach (var desc in _descriptors)
                Peers.Add(new PeerPanelVm(_endpoint, desc, AppendLog));

            _transport.Start();
            IsRunning = true;
            DutStatus = $"Running on '{SelectedAdapter.Display}' as DUT {_dutIp}. {Peers.Count} peer(s). Auto-reply {(AutoReply ? "ON" : "OFF")}.";
            AppendLog($"=== started on {SelectedAdapter.Display} ===");
        }
        catch (Exception ex)
        {
            DutStatus = $"Start failed: {ex.Message}";
            CleanupSession();
        }
    }

    private async void Stop()
    {
        IsRunning = false;
        DutStatus = "Stopped.";
        var transport = _transport;
        CleanupSession();
        if (transport is not null)
        {
            try { await transport.DisposeAsync(); } catch { /* ignore */ }
        }
    }

    private void CleanupSession()
    {
        _echo?.Dispose();
        _echo = null;
        if (_endpoint is not null) _endpoint.MessageReceived -= OnPeerMessage;
        _endpoint?.Dispose();
        _endpoint = null;
        _transport = null;
        Peers.Clear();
    }

    private void OnPeerMessage(string peerId, DecodedMessage m, long tMs)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            foreach (var p in Peers)
                if (p.Id == peerId) { p.OnReceived(m); break; }
            var shown = string.Join(", ", m.Fields.Select(kv => $"{kv.Key}={kv.Value:0.###}"));
            AppendLog($"← {peerId}  {m.Name} (seq {m.Sequence}) [{shown}]");
        });
    }

    private void AppendLog(string line)
    {
        Log.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {line}");
        while (Log.Count > MaxLogLines) Log.RemoveAt(Log.Count - 1);
    }

    private static List<PeerDescriptor> BuildDescriptors(SystemTopology topo)
    {
        var list = new List<PeerDescriptor>();
        foreach (var p in topo.Neighbours())
        {
            if (string.IsNullOrWhiteSpace(p.Ip) || string.IsNullOrWhiteSpace(p.Mac))
                throw new IcdLoadException($"Peer '{p.Id}' needs both 'ip' and 'mac' in the topology.");
            var spec = IcdLoader.LoadSpec(p.IcdPath, IcdTypeCatalog.Default);
            list.Add(new PeerDescriptor(p.Id, spec, IPAddress.Parse(p.Ip!), ParseMac(p.Mac!)));
        }
        if (list.Count == 0) throw new IcdLoadException("Topology has no peers neighbouring the DUT.");
        return list;
    }

    private static PhysicalAddress ParseMac(string s)
        => PhysicalAddress.Parse(s.Replace(":", "-").ToUpperInvariant());
}
