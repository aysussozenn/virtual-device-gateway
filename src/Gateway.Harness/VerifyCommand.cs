using Gateway.Icd;

namespace Gateway.Harness;

/// <summary>
/// CI entry point: <c>verify [--system &lt;path&gt;] [--json &lt;path&gt;] [--junit &lt;path&gt;] [--strict]</c>.
/// Loads the ICD/topology, runs the conformance check, optionally writes JSON and JUnit
/// artifacts, and returns a process exit code so a pipeline gates on it: 0 when clean,
/// 1 on any violation (or, with <c>--strict</c>, on warnings too), 2 on a load error.
/// </summary>
public static class VerifyCommand
{
    public static async Task<int> Run(string[] args)
    {
        string? system = null, scenario = null, json = null, junit = null;
        bool strict = false, live = false;

        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--system": system = Next(args, ref i); break;
                case "--scenario": scenario = Next(args, ref i); break;
                case "--json": json = Next(args, ref i); break;
                case "--junit": junit = Next(args, ref i); break;
                case "--strict": strict = true; break;
                case "--live": live = true; break;
                default:
                    if (!args[i].StartsWith("--") && system is null) system = args[i];
                    break;
            }
        }

        var outcome = live
            ? await LiveChoreographyDemo.ExecuteAsync(system, print: true)
            : FileConformanceDemo.Execute(system, print: true, scenario);
        if (outcome.Recorder is not { } rec) return outcome.LoadError;

        if (json is not null) { File.WriteAllText(json, ReportWriter.ToJson(rec, outcome.Scenario)); Console.WriteLine($"  wrote JSON report  -> {json}"); }
        if (junit is not null) { File.WriteAllText(junit, ReportWriter.ToJUnit(rec, outcome.Scenario)); Console.WriteLine($"  wrote JUnit report -> {junit}"); }

        var gateFailed = rec.Failed > 0 || (strict && rec.Warnings > 0);
        Console.WriteLine($"\nverify: {(gateFailed ? "FAIL" : "PASS")} (pass={rec.Passed} warn={rec.Warnings} fail={rec.Failed}{(strict ? ", strict" : "")})");
        return gateFailed ? 1 : 0;
    }

    private static string? Next(string[] args, ref int i) => i + 1 < args.Length ? args[++i] : null;
}
