using PacketDotNet;

namespace Gateway.Ethernet;

/// <summary>
/// A shared in-memory L2 segment. Multiple <see cref="IPacketTransport"/> ports attach
/// to it; a frame sent on one port is delivered to every other port. This models two
/// applications on the same wire without depending on OS loopback injection, and is
/// used for deterministic end-to-end testing and the harness self-test.
/// </summary>
public sealed class InMemoryBus
{
    private readonly List<InMemoryTransport> _ports = new();
    private readonly LinkLayers _linkType;
    private readonly object _gate = new();

    public InMemoryBus(LinkLayers linkType = LinkLayers.Ethernet) => _linkType = linkType;

    public InMemoryTransport CreatePort()
    {
        var port = new InMemoryTransport(this, _linkType);
        lock (_gate) _ports.Add(port);
        return port;
    }

    internal void Broadcast(InMemoryTransport from, byte[] frame)
    {
        InMemoryTransport[] targets;
        lock (_gate) targets = _ports.Where(p => !ReferenceEquals(p, from)).ToArray();
        foreach (var p in targets)
            p.Deliver(frame);
    }
}

/// <summary>A single port on an <see cref="InMemoryBus"/>.</summary>
public sealed class InMemoryTransport : IPacketTransport
{
    private readonly InMemoryBus _bus;

    internal InMemoryTransport(InMemoryBus bus, LinkLayers linkType)
    {
        _bus = bus;
        LinkType = linkType;
    }

    public LinkLayers LinkType { get; }

    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;

    public void Start() { }

    public void Send(byte[] frame) => _bus.Broadcast(this, frame);

    internal void Deliver(byte[] frame) => PacketReceived?.Invoke(this, new PacketReceivedEventArgs(frame));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
