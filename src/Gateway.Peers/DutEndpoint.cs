using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;

namespace Gateway.Peers;

/// <summary>
/// The DUT side of the wire — the mirror of <see cref="PeerChannel"/>. It listens for frames
/// addressed to the DUT, decodes each against the sending peer's ICD, and raises
/// <see cref="MessageReceived"/> for peer → DUT (inbound) traffic. It also encodes and transmits
/// DUT → peer (outbound) messages on demand, so a second GUI can act as the developer's real code.
/// Automatic replies are a separate concern handled by <see cref="EchoDut"/>.
/// </summary>
public sealed class DutEndpoint : IDisposable
{
    private readonly record struct Peer(PeerDescriptor Desc, IFrameCodec Codec);

    private readonly IPacketTransport _transport;
    private readonly IPAddress _dutIp;
    private readonly PhysicalAddress _dutMac;
    private readonly List<Peer> _peers;

    /// <summary>Fixed source MAC for outgoing frames. When null, _dutMac is used.</summary>
    public PhysicalAddress? FixedSrcMac { get; set; } = null;

    /// <summary>Raised for each decoded peer → DUT message: peer id, message, arrival tick (ms).</summary>
    public event Action<string, DecodedMessage, long>? MessageReceived;

    public DutEndpoint(IPacketTransport transport, IReadOnlyList<PeerDescriptor> peers,
        IPAddress dutIp, PhysicalAddress dutMac)
    {
        _transport = transport;
        _dutIp = dutIp;
        _dutMac = dutMac;
        _peers = peers.Select(p => new Peer(p, CodecRegistry.Default.Build(p.Spec))).ToList();
        _transport.PacketReceived += OnReceived;
    }

    /// <summary>Encodes a DUT → peer message to wire bytes without sending — for the live hex preview.</summary>
    public byte[] Encode(string peerId, IcdMessage msg, IReadOnlyDictionary<string, double> fields, ushort seq)
        => Find(peerId).Codec.Encode(msg, seq, fields);

    /// <summary>Encodes and transmits a DUT → peer message.</summary>
    public void Send(string peerId, IcdMessage msg, IReadOnlyDictionary<string, double> fields, ushort seq)
    {
        var peer = Find(peerId);
        var payload = peer.Codec.Encode(msg, seq, fields);
        var ip = PeerChannel.BuildIp(_dutIp, peer.Desc.Ip, payload);
        var srcMac = FixedSrcMac ?? _dutMac;
        _transport.Send(LinkEncap.WrapIpv4(_transport.LinkType, ip, srcMac, peer.Desc.Mac));
    }

    private void OnReceived(object? sender, PacketReceivedEventArgs e)
    {
        var ip = LinkEncap.UnwrapIpv4(_transport.LinkType, e.Data, out var eth, out _);
        if (ip is null) return;

        // Route by destination MAC (primary) or destination IP (fallback/loopback).
        var dstMac = eth?.DestinationHardwareAddress;
        if (dstMac is not null && !dstMac.Equals(_dutMac)) return;
        if (dstMac is null && !ip.DestinationAddress.Equals(_dutIp)) return;

        // Identify the sending peer by source IP.
        Peer? match = null;
        foreach (var p in _peers)
            if (p.Desc.Ip.Equals(ip.SourceAddress)) { match = p; break; }
        if (match is not { } peer) return;

        var payload = LinkEncap.IpPayload(_transport.LinkType, e.Data);
        if (payload.Length == 0) return;

        var dec = peer.Codec.TryDecode(payload);
        if (dec.Message is { } m && m.Definition.Direction == Direction.Inbound)
            MessageReceived?.Invoke(peer.Desc.Id, m, Environment.TickCount64);
    }

    private Peer Find(string peerId)
    {
        foreach (var p in _peers)
            if (p.Desc.Id == peerId) return p;
        throw new ArgumentException($"Unknown peer '{peerId}'.", nameof(peerId));
    }

    public void Dispose() => _transport.PacketReceived -= OnReceived;
}
