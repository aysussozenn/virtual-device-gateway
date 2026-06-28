using System.Collections.ObjectModel;
using System.IO;
using Gateway.Icd;
using Gateway.Peers;
using Gateway.Verification;
using Microsoft.Win32;

namespace Gateway.App.ViewModels;

/// <summary>
/// Drives the Conformance tab. This is where a developer points the tool at <em>their</em>
/// module's ICD: pick a system topology JSON, see the parsed participants/messages (so a
/// transcription mistake shows up immediately), then run the verification and read the
/// PASS/violation verdicts. Loading and running both go through the shared
/// <see cref="VerificationRunner"/>/<see cref="IcdLoader"/> — same engine as the CLI.
/// </summary>
public sealed class ConformanceViewModel : ObservableObject
{
    private string _systemPath;
    private string _scenarioPath;
    private string _status = "Select a system.json that lists your module's interfaces, then Load.";
    private string _summary = "";
    private string _dut = "—";

    public ObservableCollection<PeerInspect> Peers { get; } = new();
    public ObservableCollection<VerdictRow> Verdicts { get; } = new();

    public RelayCommand BrowseCommand { get; }
    public RelayCommand BrowseScenarioCommand { get; }
    public RelayCommand LoadCommand { get; }
    public RelayCommand RunCommand { get; }

    public ConformanceViewModel()
    {
        _systemPath = Path.Combine(AppContext.BaseDirectory, "examples", "elevator", "system.json");
        _scenarioPath = Path.Combine(Path.GetDirectoryName(_systemPath)!, "scenario.json");
        BrowseCommand = new RelayCommand(Browse);
        BrowseScenarioCommand = new RelayCommand(BrowseScenario);
        LoadCommand = new RelayCommand(Load);
        RunCommand = new RelayCommand(Run, () => Peers.Count > 0);
        if (File.Exists(_systemPath)) Load();
    }

    public string SystemPath { get => _systemPath; set => Set(ref _systemPath, value); }
    public string ScenarioPath { get => _scenarioPath; set => Set(ref _scenarioPath, value); }
    public string ConfStatus { get => _status; private set => Set(ref _status, value); }
    public string Summary { get => _summary; private set => Set(ref _summary, value); }
    public string Dut { get => _dut; private set => Set(ref _dut, value); }

    private void Browse()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select system topology (system.json)",
            Filter = "ICD topology (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(SystemPath)) ? Path.GetDirectoryName(SystemPath) : null
        };
        if (dlg.ShowDialog() == true)
        {
            SystemPath = dlg.FileName;
            ScenarioPath = Path.Combine(Path.GetDirectoryName(dlg.FileName)!, "scenario.json"); // default to sibling
            Load();
        }
    }

    private void BrowseScenario()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Select scenario (scenario.json)",
            Filter = "Scenario (*.json)|*.json|All files (*.*)|*.*",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(ScenarioPath)) ? Path.GetDirectoryName(ScenarioPath) : null
        };
        if (dlg.ShowDialog() == true) ScenarioPath = dlg.FileName;
    }

    /// <summary>Loads the topology and each peer's ICD, surfacing the parsed message set for review.</summary>
    private void Load()
    {
        Peers.Clear();
        Verdicts.Clear();
        Summary = "";
        try
        {
            var topo = IcdLoader.LoadTopology(SystemPath);
            Dut = topo.Dut;
            foreach (var p in topo.Neighbours())
            {
                var peer = new PeerInspect { Id = p.Id, IcdFile = Path.GetFileName(p.IcdPath) };
                var spec = IcdLoader.LoadSpec(p.IcdPath, IcdTypeCatalog.Default);
                foreach (var m in spec.Messages)
                    peer.Messages.Add($"{m.Name} (0x{m.Command:X4}) {m.Direction}: {Fields(m)}");
                Peers.Add(peer);
            }
            ConfStatus = $"Loaded {Peers.Count} peer ICD(s) for DUT '{Dut}'. Review the messages, then Run verify.";
        }
        catch (IcdLoadException ex)
        {
            ConfStatus = $"ICD load error: {ex.Message}";
        }
        RunCommand.RaiseCanExecuteChanged();
    }

    private void Run()
    {
        Verdicts.Clear();
        var scenario = string.IsNullOrWhiteSpace(ScenarioPath) ? null : ScenarioPath;
        var result = VerificationRunner.RunFile(SystemPath, scenario);
        if (result.Error is { } err) { ConfStatus = $"Run error: {err}"; Summary = ""; return; }

        foreach (var r in result.Recorder.Results)
            Verdicts.Add(new VerdictRow
            {
                Severity = r.Severity.ToString(),
                Rule = r.RuleId,
                Message = r.Message,
                Detail = r.Expected is null && r.Actual is null ? null : $"expected {r.Expected}, actual {r.Actual}",
                TimestampMs = r.TimestampMs
            });

        var rec = result.Recorder;
        Summary = $"{(rec.AllPassed ? "PASS" : "FAIL")}  ·  pass {rec.Passed} · warn {rec.Warnings} · fail {rec.Failed}";
        ConfStatus = $"Verified '{result.Scenario}' against loaded ICDs.";
    }

    private static string Fields(IcdMessage m) =>
        string.Join(", ", m.Fields.Select(f =>
        {
            var range = f.Min is null && f.Max is null ? "" : $"[{f.Min}..{f.Max}]";
            var en = f.Enum is { Count: > 0 } ? $"{{{string.Join("/", f.Enum.Keys)}}}" : "";
            return $"{f.Name} {f.Type.Name}{range}{en}".Trim();
        }));
}
