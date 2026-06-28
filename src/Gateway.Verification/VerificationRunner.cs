using Gateway.Icd;

namespace Gateway.Verification;

/// <summary>Outcome of a verification run: the verdicts, the cross-peer trace, and any load/parse error.</summary>
public sealed record RunResult(ConformanceRecorder Recorder, IReadOnlyList<TraceEvent> Trace, string Scenario, string? Error);

/// <summary>
/// Console-free verification core, shared by the CLI and the GUI so both drive the exact
/// same checks. Loads an ICD topology and a scenario file, then runs the data-driven
/// <see cref="ScenarioRunner"/>. Nothing here is specific to any one ICD — point it at your
/// own topology + scenario and it verifies those.
/// </summary>
public static class VerificationRunner
{
    public static RunResult RunFile(string topologyPath, string? scenarioPath = null)
    {
        SystemTopology topo;
        var specs = new List<IcdSpec>();
        ScenarioSpec scenario;
        try
        {
            topo = IcdLoader.LoadTopology(topologyPath);
            foreach (var p in topo.Neighbours())
                specs.Add(IcdLoader.LoadSpec(p.IcdPath));
            scenario = IcdLoader.LoadScenario(scenarioPath ?? DefaultScenarioBeside(topologyPath));
        }
        catch (IcdLoadException ex)
        {
            return new RunResult(new ConformanceRecorder(), Array.Empty<TraceEvent>(), "scenario", ex.Message);
        }

        try
        {
            return ScenarioRunner.Run(topo, specs, scenario);
        }
        catch (ExpressionException ex)
        {
            return new RunResult(new ConformanceRecorder(), Array.Empty<TraceEvent>(), scenario.Name, $"expression error: {ex.Message}");
        }
    }

    public static string DefaultScenarioBeside(string topologyPath)
        => Path.Combine(Path.GetDirectoryName(Path.GetFullPath(topologyPath)) ?? ".", "scenario.json");
}
