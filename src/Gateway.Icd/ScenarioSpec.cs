namespace Gateway.Icd;

/// <summary>One message on the timeline: a time, the message name, and its field values.</summary>
public sealed record ScenarioEvent(long T, string Message, IReadOnlyDictionary<string, double> Fields);

/// <summary>
/// A choreography obligation in file form: when <see cref="On"/> is seen, expect
/// <see cref="Expect"/> within <see cref="Within"/> ms satisfying the <see cref="Where"/>
/// expression (which may reference <c>trig.&lt;field&gt;</c> and <c>resp.&lt;field&gt;</c>).
/// </summary>
public sealed record ScenarioExpectation(string On, string Expect, long Within, string Where);

/// <summary>
/// A data-driven verification scenario: the message timeline plus the cross-message
/// expectations. Authoring this (instead of hard-coding it) is what lets a developer run
/// the verifier against their own ICD — their messages, their values, their rules.
/// </summary>
public sealed class ScenarioSpec
{
    public required string Name { get; init; }
    public required IReadOnlyList<ScenarioEvent> Events { get; init; }
    public required IReadOnlyList<ScenarioExpectation> Expectations { get; init; }
}
