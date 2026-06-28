using Gateway.Verification;

namespace Gateway.Harness;

/// <summary>
/// Console wrapper around <see cref="VerificationRunner"/>: loads the ICD topology and
/// scenario from file, runs the data-driven verification, and prints the timeline + report.
/// The checks live in the shared runner so the GUI and CLI stay in lockstep.
/// </summary>
public static class FileConformanceDemo
{
    public static string DefaultTopologyPath =>
        Path.Combine(AppContext.BaseDirectory, "examples", "elevator", "system.json");

    public static int Run(string? topologyPath)
    {
        var outcome = Execute(topologyPath, print: true);
        return outcome.Recorder is { } rec ? (rec.AllPassed ? 0 : 1) : outcome.LoadError;
    }

    public static VerifyOutcome Execute(string? topologyPath, bool print, string? scenarioPath = null)
    {
        var path = topologyPath ?? DefaultTopologyPath;
        void Log(string s = "") { if (print) Console.WriteLine(s); }

        var run = VerificationRunner.RunFile(path, scenarioPath);
        if (run.Error is { } err) { Console.WriteLine($"Error: {err}"); return new VerifyOutcome(null, 2, run.Scenario); }

        Log($"Loaded topology '{path}'  scenario '{run.Scenario}'");
        Log("Timeline:");
        foreach (var e in run.Trace)
            Log($"  t={e.TimestampMs,3}ms  {e.Peer,-4} {e.Name}");

        Log();
        Log(run.Recorder.Report(run.Scenario));
        return new VerifyOutcome(run.Recorder, 0, run.Scenario);
    }
}
