namespace Gateway.Core;

/// <summary>
/// A request handed to a simulated device after the wire layers (Ethernet/IP and,
/// later, any L4) have been stripped by the protocol codec.
/// </summary>
public sealed record DeviceRequest(
    PeerEndpoint Source,
    ushort Command,
    ReadOnlyMemory<byte> Data,
    uint Sequence);

/// <summary>
/// A device's response. Returning <c>null</c> from the behavior means "stay silent"
/// (used to simulate a timeout / disconnected device).
/// </summary>
public sealed record DeviceReply(ushort Status, ReadOnlyMemory<byte> Data)
{
    public static readonly DeviceReply Empty = new(0, ReadOnlyMemory<byte>.Empty);
}
