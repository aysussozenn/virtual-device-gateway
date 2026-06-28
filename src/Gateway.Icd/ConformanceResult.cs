namespace Gateway.Icd;

public enum Severity { Pass, Warning, Violation }

/// <summary>
/// One verdict produced while checking a frame (or a choreography obligation) against
/// the ICD. Carries enough context — rule id, the offending message/field, and an
/// expected-vs-actual pair — for a developer to locate the problem in their code.
/// </summary>
public sealed record ConformanceResult(
    Severity Severity,
    string RuleId,
    string Message,
    string? MessageName = null,
    int? Sequence = null,
    long TimestampMs = 0,
    string? Expected = null,
    string? Actual = null)
{
    public bool IsFailure => Severity == Severity.Violation;

    public static ConformanceResult Pass(string ruleId, string message, string? msgName = null, long t = 0)
        => new(Severity.Pass, ruleId, message, msgName, TimestampMs: t);

    public static ConformanceResult Violation(string ruleId, string message,
        string? msgName = null, int? seq = null, long t = 0, string? expected = null, string? actual = null)
        => new(Severity.Violation, ruleId, message, msgName, seq, t, expected, actual);
}
