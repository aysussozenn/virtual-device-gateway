using System.Text;

namespace Gateway.Icd;

/// <summary>
/// Collects every verdict produced during a verification run and renders a report.
/// In a live run it is fed from the frame observer (per-frame conformance) and the
/// monitor engine (choreography); here it also knows how to print a console summary.
/// </summary>
public sealed class ConformanceRecorder
{
    private readonly List<ConformanceResult> _results = new();

    public void Add(ConformanceResult r) => _results.Add(r);
    public void AddRange(IEnumerable<ConformanceResult> rs) => _results.AddRange(rs);

    public IReadOnlyList<ConformanceResult> Results => _results;
    public int Passed => _results.Count(r => r.Severity == Severity.Pass);
    public int Warnings => _results.Count(r => r.Severity == Severity.Warning);
    public int Failed => _results.Count(r => r.IsFailure);
    public bool AllPassed => Failed == 0;

    /// <summary>Human-readable console report (the PASS/FAIL summary table).</summary>
    public string Report(string scenario)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"CONFORMANCE REPORT  scenario={scenario}  verdicts={_results.Count}");
        sb.AppendLine($"  PASS {Passed}   FAIL {Failed}");
        foreach (var r in _results.Where(r => r.IsFailure))
        {
            var ev = r.Expected is null && r.Actual is null ? "" : $"  (expected {r.Expected}, actual {r.Actual})";
            var seq = r.Sequence is { } s ? $" seq={s}" : "";
            sb.AppendLine($"  FAIL [{r.RuleId}] {r.Message}{ev}  @t={r.TimestampMs}ms{seq}");
        }
        return sb.ToString();
    }
}
