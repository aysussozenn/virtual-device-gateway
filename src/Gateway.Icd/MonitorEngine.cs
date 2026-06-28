namespace Gateway.Icd;

/// <summary>
/// A choreography obligation: "when <see cref="Trigger"/> is observed, expect
/// <see cref="Expect"/> within <see cref="WithinMs"/>, satisfying <see cref="Where"/>".
/// The <see cref="Where"/> predicate sees both the triggering message and the candidate
/// response, so it can express cross-message data correlation (e.g. value relationships).
/// </summary>
public sealed record ResponseMonitor(
    string Trigger,
    string Expect,
    long WithinMs,
    Func<DecodedMessage, DecodedMessage, bool> Where,
    string Description);

/// <summary>
/// Evaluates response monitors against the global trace. Each triggering event spawns an
/// independent obligation, so overlapping/parallel flows are checked concurrently: the
/// obligation from one trigger neither masks nor depends on another's.
/// </summary>
public static class MonitorEngine
{
    public static IReadOnlyList<ConformanceResult> Evaluate(
        IReadOnlyList<ResponseMonitor> monitors, IReadOnlyList<TraceEvent> trace)
    {
        var results = new List<ConformanceResult>();
        foreach (var m in monitors)
        {
            foreach (var trig in trace.Where(e => e.Name == m.Trigger))
            {
                var window = trace.Where(e =>
                    e.Name == m.Expect &&
                    e.TimestampMs > trig.TimestampMs &&
                    e.TimestampMs <= trig.TimestampMs + m.WithinMs).ToList();

                var satisfying = window.FirstOrDefault(e => m.Where(trig.Message, e.Message));
                if (satisfying is not null)
                {
                    results.Add(ConformanceResult.Pass("CHOREO",
                        $"{m.Trigger}->{m.Expect}: {m.Description}", m.Expect, satisfying.TimestampMs));
                }
                else if (window.Count > 0)
                {
                    var got = window[0];
                    results.Add(ConformanceResult.Violation("CHOREO-WHERE",
                        $"{m.Trigger}->{m.Expect} arrived but failed: {m.Description}",
                        m.Expect, got.Message.Sequence, got.TimestampMs));
                }
                else
                {
                    results.Add(ConformanceResult.Violation("CHOREO-TIMEOUT",
                        $"no {m.Expect} within {m.WithinMs}ms of {m.Trigger}",
                        m.Expect, trig.Message.Sequence, trig.TimestampMs + m.WithinMs));
                }
            }
        }
        return results;
    }
}
