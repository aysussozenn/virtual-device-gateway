namespace Gateway.Core;

/// <summary>
/// A protocol-level frame decoded from (or to be encoded into) the IP payload.
/// This is the seam where the (pending) protocol spec plugs in.
/// </summary>
public readonly record struct ProtocolFrame(ushort Command, uint Sequence, ReadOnlyMemory<byte> Data);

/// <summary>
/// Translates between raw IP-payload bytes and a structured <see cref="ProtocolFrame"/>.
/// The concrete implementation is determined by the device protocol specification;
/// until that arrives, a passthrough codec is used so the pipeline works end-to-end.
/// </summary>
public interface IProtocolCodec
{
    /// <summary>Attempts to decode the IP payload into a protocol frame.</summary>
    bool TryDecode(ReadOnlyMemory<byte> ipPayload, out ProtocolFrame frame);

    /// <summary>Encodes a device reply (correlated to the originating request) back into IP-payload bytes.</summary>
    byte[] Encode(in ProtocolFrame request, DeviceReply reply);
}
