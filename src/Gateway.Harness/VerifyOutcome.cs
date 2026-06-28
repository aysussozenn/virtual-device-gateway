using Gateway.Icd;

namespace Gateway.Harness;

/// <summary>
/// Result of a verification run, shared by the file-driven and live-choreography paths:
/// the collected verdicts plus the scenario name, or a non-zero load error.
/// </summary>
public readonly record struct VerifyOutcome(ConformanceRecorder? Recorder, int LoadError, string Scenario);
