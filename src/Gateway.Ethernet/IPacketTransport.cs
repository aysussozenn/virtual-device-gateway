using PacketDotNet;

namespace Gateway.Ethernet;

/// <summary>Raw frame received from the wire (link-layer bytes, no decoding).</summary>
public sealed class PacketReceivedEventArgs : EventArgs
{
    public PacketReceivedEventArgs(byte[] data)
    {
        Data = data;
    }

    public byte[] Data { get; }
}

/// <summary>
/// Abstracts the raw send/receive medium so the engine is decoupled from SharpPcap.
/// Production uses <see cref="PcapTransport"/>; tests use an in-memory fake to drive
/// the full pipeline deterministically without relying on OS loopback reflection.
/// </summary>
public interface IPacketTransport : IAsyncDisposable
{
    /// <summary>Link-layer type of the medium (valid after <see cref="Start"/>).</summary>
    LinkLayers LinkType { get; }

    event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    void Start();

    void Send(byte[] frame);
}
