using System.Collections.ObjectModel;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Windows.Threading;
using Gateway.Ethernet;
using Gateway.Icd;
using Gateway.Peers;
using Microsoft.Win32;

namespace Gateway.App.ViewModels;

/// <summary>
/// Drives the Peer Console tab. A developer points it at their system topology; for each
/// emulated peer it generates an interactive tab straight from the ICD — editable outgoing
/// messages (periodic streaming / aperiodic send) and live incoming monitors. In demo mode an
/// in-memory bus plus an echo DUT stand-in lets the whole loop run with no adapter; live mode
/// binds the same channels to a real capture adapter.
/// </summary>
public sealed class PeerConsoleViewModel : ObservableObject
{
    private readonly DispatcherTimer _freshness = new() { Interval = TimeSpan.FromMilliseconds(250) };

    private SystemTopology? _topology;
    private PeerSession? _session;
    private string _systemPath;
    private string _status = "Select a system.json that lists your peers, then Load.";
    private bool _demoMode = true;
    private bool _isRunning;
    private AdapterItem? _selectedAdapter;
    private PeerTabViewModel? _selectedPeer;

    public PeerConsoleViewModel(ObservableCollection<AdapterItem> adapters)
    {
        Adapters = adapters;
        _systemPath = Path.Combine(AppContext.BaseDirectory, "examples", "abc", "system.json");

        BrowseCommand = new RelayCommand(Browse);
        LoadCommand = new RelayCommand(Load);
        StartCommand = new RelayCommand(Start, () => _topology is not null && !IsRunning && (DemoMode || SelectedAdapter is not null));
        StopCommand = new RelayCommand(Stop, () => IsRunning);
        _freshness.Tick += (_, _) => RefreshFreshness();

        SelectDefaultAdapter();
        if (File.Exists(_systemPath)) Load();
    }

    /// <summary>Picks a sensible default capture adapter (prefer a loopback) if none chosen yet.</summary>
    public void SelectDefaultAdapter()
    {
        SelectedAdapter ??= Adapters.FirstOrDefault(a =>
            a.Display.Contains("loopback", StringComparison.OrdinalIgnoreCase)) ?? Adapters.FirstOrDefault();
    }

    public ObservableCollection<AdapterItem> Adapters { get; }
    public ObservableCollection<PeerTabViewModel> Peers { get; } = new();

    public PeerTabViewModel? SelectedPeer
    {
        get => _selectedPeer;
        set => Set(ref _selectedPeer, value);
    }

    public RelayCommand BrowseCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand StartCommand { get; }
    public RelayCommand StopCommand { get; }

    public string SystemPath { get => _systemPath; set => Set(ref _systemPath, value); }
    public string PcStatus { get => _status; private set => Set(ref _status, value); }

    public bool DemoMode
    {
        get => _demoMode;
        set { if (Set(ref _demoMode, value)) StartCommand.RaiseCanExecuteChanged(); }
    }

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
            PcStatus = $"Loaded {peers.Count} peer(s) for DUT '{_topology.Dut}' → {_topology.DutIp}. Press Start.";
        }
        catch (IcdLoadException ex)
        {
            _topology = null;
            PcStatus = $"Load error: {ex.Message}";
        }
        StartCommand.RaiseCanExecuteChanged();
    }

    private void Start()
    {
        if (_topology is null) return;
        try
        {
            var descriptors = BuildDescriptors(_topology);
            var dutIp = IPAddress.Parse(_topology.DutIp);
            var dutMac = ParseMac(_topology.DutMac);

            _session = DemoMode
                ? PeerSession.CreateDemo(descriptors, dutIp, dutMac)
                : PeerSession.CreateLive(new PcapTransport(SelectedAdapter!.Device), descriptors, dutIp, dutMac);

            foreach (var channel in _session.Channels)
            {
                var desc = descriptors.First(d => d.Id == channel.PeerId);
                Peers.Add(new PeerTabViewModel(channel, desc.Mac.ToString(), AllowedMessages(_topology, channel.PeerId)));
            }
            SelectedPeer = Peers.FirstOrDefault();

            _session.Start();
            _freshness.Start();
            IsRunning = true;
            PcStatus = DemoMode
                ? $"Running (demo bus + echo DUT). {Peers.Count} peer(s)."
                : $"Running on '{SelectedAdapter!.Display}'. {Peers.Count} peer(s).";
        }
        catch (Exception ex)
        {
            PcStatus = $"Start failed: {ex.Message}";
            CleanupSession();
        }
    }

    private async void Stop()
    {
        _freshness.Stop();
        IsRunning = false;
        PcStatus = "Stopped.";
        var session = _session;
        CleanupSession();
        if (session is not null)
        {
            try { await session.DisposeAsync(); } catch { /* ignore */ }
        }
    }

    private void CleanupSession()
    {
        SelectedPeer = null;
        foreach (var tab in Peers) tab.Dispose();
        Peers.Clear();
        _session = null;
    }

    private void RefreshFreshness()
    {
        var now = Environment.TickCount64;
        foreach (var tab in Peers) tab.RefreshFreshness(now);
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

    private static IReadOnlySet<string> AllowedMessages(SystemTopology topo, string peerId)
        => topo.Interfaces
            .Where(i => i.From == peerId || i.To == peerId)
            .SelectMany(i => i.Messages)
            .ToHashSet(StringComparer.Ordinal);

    private static PhysicalAddress ParseMac(string s)
        => PhysicalAddress.Parse(s.Replace(":", "-").ToUpperInvariant());
}
