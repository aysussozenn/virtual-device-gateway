namespace Gateway.Ethernet;

public enum FrameDirection { Inbound, Outbound }

public enum FrameKind { Arp, Ipv4Request, Ipv4Reply, Ignored }

/// <summary>Raised for every frame the engine handles, so a UI/console can show live traffic.</summary>
public sealed class FrameObservedEventArgs : EventArgs
{
    public FrameObservedEventArgs(
        FrameDirection direction,
        FrameKind kind,
        string summary,
        string? deviceId = null,
        string? value = null,
        ReadOnlyMemory<byte> payload = default)
    {
        Direction = direction;
        Kind = kind;
        Summary = summary;
        DeviceId = deviceId;
        Value = value;
        Payload = payload;
        TimestampUtc = DateTime.UtcNow;
    }

    public FrameDirection Direction { get; }
    public FrameKind Kind { get; }
    public string Summary { get; }
    public string? DeviceId { get; }

    /// <summary>For Ipv4Reply: the device's output payload as a hex string (the "parameters" it produced).</summary>
    public string? Value { get; }

    /// <summary>
    /// The raw IP-payload bytes for Ipv4Request/Ipv4Reply frames (empty otherwise). This is
    /// the seam a conformance listener decodes against the ICD — the engine itself stays
    /// unaware of any protocol spec.
    /// </summary>
    public ReadOnlyMemory<byte> Payload { get; }

    public DateTime TimestampUtc { get; }
}
