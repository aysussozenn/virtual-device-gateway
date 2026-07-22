using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;

namespace Gateway.Ethernet;

/// <summary>
/// Stand-in for the "App B" client: periodically injects IPv4 requests toward the
/// simulated device IPs on a transport port. Used to drive live traffic (demo mode
/// over an in-memory bus, or against a real adapter).
/// </summary>
public sealed class AppBSimulator : IAsyncDisposable
{
    public readonly record struct Target(IPAddress Ip, PhysicalAddress Mac);

    private readonly IPacketTransport _port;
    private readonly IReadOnlyList<Target> _targets;
    private readonly IPAddress _srcIp;
    private readonly PhysicalAddress _srcMac;
    private readonly TimeSpan _interval;

    private Timer? _timer;
    private int _tick;

    public AppBSimulator(
        IPacketTransport port,
        IReadOnlyList<Target> targets,
        IPAddress srcIp,
        PhysicalAddress srcMac,
        TimeSpan interval)
    {
        _port = port;
        _targets = targets;
        _srcIp = srcIp;
        _srcMac = srcMac;
        _interval = interval;
    }

    public void Start()
    {
        _port.Start();
        _timer = new Timer(_ => Poll(), null, TimeSpan.Zero, _interval);
    }

    private void Poll()
    {
        _tick++;
        foreach (var target in _targets)
            SendTo(target);
    }

    private void SendTo(Target target)
    {
        var ip = LinkEncap.BuildUdpIp(_srcIp, target.Ip, new[] { (byte)(_tick & 0xFF) });
        _port.Send(LinkEncap.WrapIpv4(_port.LinkType, ip, _srcMac, target.Mac));
    }

    public async ValueTask DisposeAsync()
    {
        if (_timer is not null) await _timer.DisposeAsync();
        await _port.DisposeAsync();
    }
}
