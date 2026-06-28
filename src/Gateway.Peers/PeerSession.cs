using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using PacketDotNet;

namespace Gateway.Peers;

/// <summary>
/// Owns the transport medium and one <see cref="PeerChannel"/> per emulated peer for a loaded
/// topology. Two factory paths share the same channels:
/// <list type="bullet">
/// <item>Demo: an in-memory bus with a port per peer plus an <see cref="EchoDut"/> stand-in.</item>
/// <item>Live: a single shared adapter transport; every channel filters by its own IP, no echo.</item>
/// </list>
/// </summary>
public sealed class PeerSession : IAsyncDisposable
{
    private readonly List<PeerChannel> _channels = new();
    private readonly List<IPacketTransport> _ports = new();
    private EchoDut? _echo;

    public IReadOnlyList<PeerChannel> Channels => _channels;

    private PeerSession() { }

    public static PeerSession CreateDemo(IReadOnlyList<PeerDescriptor> peers, IPAddress dutIp, PhysicalAddress dutMac)
    {
        var session = new PeerSession();
        var bus = new InMemoryBus(LinkLayers.Ethernet);
        foreach (var p in peers)
        {
            var port = bus.CreatePort();
            session._ports.Add(port);
            session._channels.Add(new PeerChannel(p.Id, p.Spec, port, p.Ip, p.Mac, dutIp, dutMac));
        }

        var echoPort = bus.CreatePort();
        session._ports.Add(echoPort);
        session._echo = new EchoDut(echoPort, peers, dutIp, dutMac);
        return session;
    }

    public static PeerSession CreateLive(IPacketTransport transport, IReadOnlyList<PeerDescriptor> peers,
        IPAddress dutIp, PhysicalAddress dutMac)
    {
        var session = new PeerSession();
        session._ports.Add(transport);
        foreach (var p in peers)
            session._channels.Add(new PeerChannel(p.Id, p.Spec, transport, p.Ip, p.Mac, dutIp, dutMac));
        return session;
    }

    public void Start()
    {
        foreach (var port in _ports)
            port.Start();
    }

    public async ValueTask DisposeAsync()
    {
        _echo?.Dispose();
        foreach (var channel in _channels)
            channel.Dispose();
        foreach (var port in _ports)
            await port.DisposeAsync();
    }
}
