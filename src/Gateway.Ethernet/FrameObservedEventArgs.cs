namespace Gateway.Ethernet;

public enum FrameDirection { Inbound, Outbound }

public enum FrameKind { Arp, Ipv4Request, Ipv4Reply, Ignored }

/// <summary>Raised for every frame the engine handles, so a UI/console can show live traffic.</summary>
public sealed class FrameObservedEventArgs(
    FrameDirection direction,
    FrameKind kind,
    string summary,
    string? deviceId = null,
    string? value = null,
    ReadOnlyMemory<byte> payload = default)
    : EventArgs
{
    public FrameDirection Direction { get; } = direction;
    public FrameKind Kind { get; } = kind;
    public string Summary { get; } = summary;
    public string? DeviceId { get; } = deviceId;

    /// <summary>For Ipv4Reply: the device's output payload as a hex string (the "parameters" it produced).</summary>
    public string? Value { get; } = value;

    /// <summary>
    /// The raw IP-payload bytes for Ipv4Request/Ipv4Reply frames (empty otherwise). This is
    /// the seam a conformance listener decodes against the ICD — the engine itself stays
    /// unaware of any protocol spec.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; } = payload;

    public DateTime TimestampUtc { get; } = DateTime.UtcNow;
}
