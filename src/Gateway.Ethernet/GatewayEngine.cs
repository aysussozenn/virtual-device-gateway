using System.Net.NetworkInformation;
using System.Threading.Channels;
using Gateway.Core;
using Microsoft.Extensions.Logging;
using PacketDotNet;

namespace Gateway.Ethernet;

/// <summary>
/// The capture/send engine. Routes incoming frames by destination MAC (primary)
/// or destination IP (fallback). Uses a fixed source MAC on all outgoing frames
/// when configured via <see cref="EthernetGatewayOptions.FixedSrcMac"/>.
/// </summary>
public sealed class GatewayEngine : IAsyncDisposable
{
    private readonly IPacketTransport _transport;
    private readonly IDeviceRouter _router;
    private readonly IProtocolCodec _codec;
    private readonly EthernetGatewayOptions _options;
    private readonly ILogger<GatewayEngine> _logger;

    private readonly Channel<byte[]> _queue =
        Channel.CreateUnbounded<byte[]>(new UnboundedChannelOptions { SingleReader = true });

    private Task? _worker;
    private CancellationTokenSource? _cts;
    private bool _ethernet;

    public event EventHandler<FrameObservedEventArgs>? FrameObserved;

    public GatewayEngine(
        IPacketTransport transport,
        IDeviceRouter router,
        IProtocolCodec codec,
        EthernetGatewayOptions options,
        ILogger<GatewayEngine> logger)
    {
        _transport = transport;
        _router = router;
        _codec = codec;
        _options = options;
        _logger = logger;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _transport.PacketReceived += OnPacketReceived;
        _transport.Start();
        _ethernet = LinkEncap.IsEthernet(_transport.LinkType);
        _worker = Task.Run(() => ProcessLoopAsync(_cts.Token));
        _logger.LogInformation("Gateway started (link: {Link}) with {Count} device(s).",
            _transport.LinkType, _router.Devices.Count);
    }

    private void OnPacketReceived(object? sender, PacketReceivedEventArgs e) => _queue.Writer.TryWrite(e.Data);

    private async Task ProcessLoopAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var data in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                try
                {
                    await HandleAsync(data, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling captured frame.");
                }
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
    }

    private async Task HandleAsync(byte[] data, CancellationToken ct)
    {
        var ip = LinkEncap.UnwrapIpv4(_transport.LinkType, data, out var eth, out var arp);

        if (_ethernet && _options.AnswerArp && arp is { Operation: ArpOperation.Request })
        {
            HandleArpRequest(arp);
            return;
        }

        if (ip is not null)
        {
            // Route by destination MAC (primary key); keep the source MAC so the reply
            // can be addressed back to the requester.
            var dstMac = eth?.DestinationHardwareAddress ?? PhysicalAddress.None;
            var srcMac = eth?.SourceHardwareAddress ?? PhysicalAddress.None;
            await HandleIpv4Async(data, srcMac, dstMac, ip, ct).ConfigureAwait(false);
        }
    }

    private void HandleArpRequest(ArpPacket arp)
    {
        if (!_router.TryResolve(arp.TargetProtocolAddress, out var device))
            return;

        var reply = new ArpPacket(
            ArpOperation.Response,
            targetHardwareAddress: arp.SenderHardwareAddress,
            targetProtocolAddress: arp.SenderProtocolAddress,
            senderHardwareAddress: device.Identity.Mac,
            senderProtocolAddress: device.Identity.Ip);

        var frame = new EthernetPacket(device.Identity.Mac, arp.SenderHardwareAddress, EthernetType.Arp)
        {
            PayloadPacket = reply
        };

        _transport.Send(frame.Bytes);
        Observe(FrameDirection.Outbound, FrameKind.Arp,
            $"ARP reply {device.Identity.Ip} is-at {device.Identity.Mac} -> {arp.SenderProtocolAddress}", device.Identity.Id);
    }

    private async Task HandleIpv4Async(byte[] rawFrame, PhysicalAddress srcMac, PhysicalAddress dstMac, IPv4Packet ip, CancellationToken ct)
    {
        // Primary: route by destination MAC
        if (!_router.TryResolveByMac(dstMac, out var device))
        {
            // Fallback: route by destination IP
            if (!_router.TryResolve(ip.DestinationAddress, out device))
            {
                Observe(FrameDirection.Inbound, FrameKind.Ignored,
                    $"Frame to unmanaged dst {dstMac} / {ip.DestinationAddress}, ignored");
                return;
            }
        }

        // Reply is addressed back to the requester (its source MAC). The outgoing source MAC
        // is overridden with FixedSrcMac in SendIpv4 when configured.
        var peer = new PeerEndpoint(ip.SourceAddress, srcMac);
        var payload = LinkEncap.IpPayload(_transport.LinkType, rawFrame);
        Observe(FrameDirection.Inbound, FrameKind.Ipv4Request,
            $"{ip.SourceAddress} -> {ip.DestinationAddress} ({device.Identity.Id}), {payload.Length} bytes",
            device.Identity.Id, payload: payload);

        if (!_codec.TryDecode(payload, out var frame))
        {
            _logger.LogWarning("Codec could not decode payload for device {Id}.", device.Identity.Id);
            return;
        }

        var request = new DeviceRequest(peer, frame.Command, frame.Data, frame.Sequence);
        var reply = await device.HandleAsync(request, ct).ConfigureAwait(false);
        if (reply is null)
        {
            _logger.LogDebug("Device {Id} chose to stay silent.", device.Identity.Id);
            return;
        }

        var replyPayload = _codec.Encode(frame, reply);
        SendIpv4(device.Identity, peer, replyPayload);
        Observe(FrameDirection.Outbound, FrameKind.Ipv4Reply,
            $"{device.Identity.Ip} -> {peer.Ip} ({device.Identity.Id}), status {reply.Status}, {replyPayload.Length} bytes",
            device.Identity.Id, Convert.ToHexString(replyPayload), replyPayload);
    }

    private void SendIpv4(DeviceIdentity from, PeerEndpoint to, byte[] payload)
    {
        // Ethernet(14) + IP(20) + UDP(8) = 42-byte header, built centrally.
        var ip = LinkEncap.BuildUdpIp(from.Ip, to.Ip, payload);

        // Src MAC: fixed source MAC if configured, otherwise device's own MAC.
        var srcMac = _options.FixedSrcMac ?? from.Mac;
        _transport.Send(LinkEncap.WrapIpv4(_transport.LinkType, ip, srcMac, to.Mac));
    }

    private void Observe(FrameDirection direction, FrameKind kind, string summary,
        string? deviceId = null, string? value = null, ReadOnlyMemory<byte> payload = default)
    {
        _logger.LogInformation("[{Dir}] {Summary}", direction, summary);
        FrameObserved?.Invoke(this, new FrameObservedEventArgs(direction, kind, summary, deviceId, value, payload));
    }

    public async ValueTask DisposeAsync()
    {
        _transport.PacketReceived -= OnPacketReceived;
        _queue.Writer.TryComplete();
        if (_cts is not null) { _cts.Cancel(); await Task.CompletedTask.ConfigureAwait(false); }
        if (_worker is not null)
        {
            try { await _worker.ConfigureAwait(false); } catch { /* ignore */ }
        }
        await _transport.DisposeAsync().ConfigureAwait(false);
        _cts?.Dispose();
    }
}
