using Gateway.Core;

namespace Gateway.Protocol;

/// <summary>
/// Placeholder codec used until the real device protocol spec is available. It treats
/// the entire IP payload as opaque <see cref="ProtocolFrame.Data"/> with command 0,
/// and echoes the reply payload back unchanged. This lets the whole pipeline run
/// end-to-end; replace with a spec-driven codec without touching any other layer.
/// </summary>
public sealed class PassthroughProtocolCodec : IProtocolCodec
{
    public bool TryDecode(ReadOnlyMemory<byte> ipPayload, out ProtocolFrame frame)
    {
        frame = new ProtocolFrame(Command: 0, Sequence: 0, Data: ipPayload);
        return true;
    }

    public byte[] Encode(in ProtocolFrame request, DeviceReply reply) => reply.Data.ToArray();
}
