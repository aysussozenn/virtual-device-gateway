namespace Gateway.Icd;

/// <summary>
/// One entry in the global, time-ordered trace: a decoded message observed on the wire,
/// tagged with the participant it belongs to and a virtual timestamp. Both per-frame
/// conformance and cross-participant choreography are evaluated over this single stream,
/// which is why the monitors don't care which transport carried the frame.
/// </summary>
public sealed record TraceEvent(long TimestampMs, string Peer, DecodedMessage Message)
{
    public string Name => Message.Name;
}
