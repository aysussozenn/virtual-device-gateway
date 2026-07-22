using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;

namespace Gateway.Peers;

/// <summary>
/// Demo-only stand-in for the developer's real code (the DUT). When no real DUT is on the
/// wire, it keeps the Incoming panel alive: for every peer → DUT message it receives, it
/// replies with that peer's first DUT → peer message, copying any same-named fields. So an
/// edit to <c>a</c> on the outgoing side comes straight back as <c>a</c> on the incoming side,
/// which is the whole point of the tool. It is never used in live mode — the real DUT answers.
/// </summary>
public sealed class EchoDut : IDisposable
{
    private readonly record struct Entry(PeerDescriptor Peer, IFrameCodec Codec, IcdMessage? Reply);

    private readonly IPacketTransport _transport;
    private readonly IPAddress _dutIp;
    private readonly PhysicalAddress _dutMac;
    private readonly List<Entry> _entries;
    private ushort _seq = 50000;

    /// <summary>Fixed source MAC for outgoing frames. When null, _dutMac is used.</summary>
    public PhysicalAddress? FixedSrcMac { get; set; } = null;

    public EchoDut(IPacketTransport transport, IReadOnlyList<PeerDescriptor> peers, IPAddress dutIp, PhysicalAddress dutMac)
    {
        _transport = transport;
        _dutIp = dutIp;
        _dutMac = dutMac;
        _entries = peers.Select(p => new Entry(
            p,
            CodecRegistry.Default.Build(p.Spec),
            p.Spec.Messages.FirstOrDefault(m => m.Direction == Direction.Outbound))).ToList();
        _transport.PacketReceived += OnReceived;
    }

    private void OnReceived(object? sender, PacketReceivedEventArgs e)
    {
        var ip = LinkEncap.UnwrapIpv4(_transport.LinkType, e.Data, out var eth, out _);
        if (ip is null) return;

        // Route by destination MAC (primary) or destination IP (fallback/loopback)
        var dstMac = eth?.DestinationHardwareAddress;
        if (dstMac is not null && !dstMac.Equals(_dutMac)) return;
        if (dstMac is null && !ip.DestinationAddress.Equals(_dutIp)) return;

        var entry = _entries.FirstOrDefault(x => x.Peer.Ip.Equals(ip.SourceAddress));
        if (entry.Peer is null || entry.Reply is null) return;

        var payload = LinkEncap.IpPayload(_transport.LinkType, e.Data);
        if (payload.Length == 0) return;

        var dec = entry.Codec.TryDecode(payload);
        if (dec.Message is not { } m || m.Definition.Direction != Direction.Inbound) return;

        var fields = new Dictionary<string, double>();
        foreach (var f in entry.Reply.Fields)
            fields[f.Name] = m.Fields.TryGetValue(f.Name, out var v) ? v : 0;

        var replyPayload = entry.Codec.Encode(entry.Reply, _seq++, fields);
        var replyIp = PeerChannel.BuildIp(_dutIp, entry.Peer.Ip, replyPayload);

        // Src MAC: fixed if set, otherwise dut's own MAC
        var srcMac = FixedSrcMac ?? _dutMac;
        _transport.Send(LinkEncap.WrapIpv4(_transport.LinkType, replyIp, srcMac, entry.Peer.Mac));
    }

    public void Dispose() => _transport.PacketReceived -= OnReceived;
}
