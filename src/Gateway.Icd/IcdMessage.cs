namespace Gateway.Icd;

/// <summary>Direction of a message relative to the device-under-test (DUT).</summary>
public enum Direction { Inbound, Outbound }   // Inbound = DUT receives, Outbound = DUT sends

/// <summary>ARINC&#160;653 port flavor, when the message is carried over an A653 port.</summary>
public enum PortKind { None, Sampling, Queuing }

/// <summary>
/// A single ICD message definition: a command/port id, an ordered field layout, and
/// (when applicable) the ARINC&#160;653 port attributes. The field order is the wire
/// order; the codec reads/writes fields in this sequence after the framing header.
/// </summary>
public sealed record IcdMessage(
    string Name,
    ushort Command,
    Direction Direction,
    IReadOnlyList<IcdField> Fields)
{
    // --- ARINC 653 port attributes (modeled now; refresh/depth enforcement lands in a later phase) ---
    public PortKind Port { get; init; } = PortKind.None;
    public Direction PortDir { get; init; } = Direction.Inbound;
    public int? RefreshMs { get; init; }   // sampling: max age before data is INVALID
    public int? Depth { get; init; }       // queuing: max queued messages
    public int? MaxSize { get; init; }     // max payload size in bytes (port-declared)
}
