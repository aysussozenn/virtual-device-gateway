using System.Collections.ObjectModel;
using SharpPcap;

namespace Gateway.App.ViewModels;

/// <summary>A capture adapter shown in the dropdown.</summary>
public sealed class AdapterItem(ILiveDevice device)
{
    public ILiveDevice Device { get; } = device;
    public string Display => Device.Description ?? Device.Name;
    public override string ToString() => Display;
}

/// <summary>A participant (emulated peer) shown in the ICD inspector, with its parsed messages.</summary>
public sealed class PeerInspect
{
    public required string Id { get; init; }
    public required string IcdFile { get; init; }
    public ObservableCollection<string> Messages { get; } = new();
}

/// <summary>One conformance verdict row (PASS/violation) shown in the Conformance tab.</summary>
public sealed class VerdictRow
{
    public required string Severity { get; init; }   // "Pass" | "Warning" | "Violation"
    public required string Rule { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }
    public long TimestampMs { get; init; }
    public bool IsFailure => Severity == "Violation";
}

