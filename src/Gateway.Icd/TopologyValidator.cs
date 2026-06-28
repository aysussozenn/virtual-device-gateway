namespace Gateway.Icd;

/// <summary>
/// Conformance derived from the topology graph rather than a single frame: did the DUT
/// actually exercise every interface its ICD declares (coverage), and did any message
/// flow that the ICD does not declare at all (unexpected)? This catches whole-interface
/// omissions — e.g. a code path that never talks to a required partner — that per-frame
/// range/enum checks cannot see.
/// </summary>
public static class TopologyValidator
{
    public static IReadOnlyList<ConformanceResult> Check(SystemTopology topology, IReadOnlyList<TraceEvent> trace)
    {
        var seen = trace.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var results = new List<ConformanceResult>();

        foreach (var iface in topology.Interfaces)
        foreach (var msg in iface.Messages)
        {
            declared.Add(msg);
            results.Add(seen.Contains(msg)
                ? ConformanceResult.Pass("COVERAGE", $"{iface.From}->{iface.To}:{msg} exercised", msg)
                : ConformanceResult.Violation("COVERAGE-MISSING",
                    $"declared interface {iface.From}->{iface.To} message {msg} was never observed", msg));
        }

        foreach (var name in seen.Where(n => !declared.Contains(n)))
            results.Add(ConformanceResult.Violation("COVERAGE-UNEXPECTED",
                $"observed message {name} is not declared on any interface", name));

        return results;
    }
}
