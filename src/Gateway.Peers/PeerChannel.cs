using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;
using PacketDotNet;

namespace Gateway.Peers;

/// <summary>Static description of an emulated peer: its ICD plus raw-Ethernet addressing.</summary>
public sealed record PeerDescriptor(string Id, IcdSpec Spec, IPAddress Ip, PhysicalAddress Mac);

/// <summary>
/// One emulated peer's send/receive endpoint on the wire. Routes by destination MAC.
/// Outgoing frames use <see cref="FixedSrcMac"/> as the source MAC when set.
/// </summary>
public sealed class PeerChannel : IDisposable
{
    private readonly IPacketTransport _transport;
    private readonly IFrameCodec _codec;
    private readonly IPAddress _peerIp;
    private readonly IPAddress _dutIp;
    private readonly PhysicalAddress _peerMac;
    private readonly PhysicalAddress _dutMac;

    /// <summary>
    /// Fixed source MAC for outgoing frames (bytes 6-11).
    /// When null, _peerMac is used as source.
    /// Set this to the constant MAC your protocol requires.
    /// </summary>
    public PhysicalAddress? FixedSrcMac { get; set; } = null;

    public string PeerId { get; }
    public IcdSpec Spec { get; }
    public IPAddress PeerIp => _peerIp;

    public event Action<DecodedMessage, long>? MessageDecoded;

    public PeerChannel(string peerId, IcdSpec spec, IPacketTransport transport,
        IPAddress peerIp, PhysicalAddress peerMac, IPAddress dutIp, PhysicalAddress dutMac)
    {
        PeerId = peerId;
        Spec = spec;
        _codec = CodecRegistry.Default.Build(spec);
        _transport = transport;
        _peerIp = peerIp;
        _peerMac = peerMac;
        _dutIp = dutIp;
        _dutMac = dutMac;
        _transport.PacketReceived += OnReceived;
    }

    /// <summary>Encodes the message to wire bytes without sending — used for the live hex preview.</summary>
    public byte[] Encode(IcdMessage msg, IReadOnlyDictionary<string, double> fields, ushort seq)
        => _codec.Encode(msg, seq, fields);

    /// <summary>Encodes and transmits a message peer → DUT.</summary>
    public void Send(IcdMessage msg, IReadOnlyDictionary<string, double> fields, ushort seq)
    {
        var payload = _codec.Encode(msg, seq, fields);
        var ip = BuildIp(_peerIp, _dutIp, payload);
        // Src MAC: fixed if set, otherwise peer's own MAC
        var srcMac = FixedSrcMac ?? _peerMac;
        _transport.Send(LinkEncap.WrapIpv4(_transport.LinkType, ip, srcMac, _dutMac));
    }

    private void OnReceived(object? sender, PacketReceivedEventArgs e)
    {
        var ip = LinkEncap.UnwrapIpv4(_transport.LinkType, e.Data, out var eth, out _);
        if (ip is null) return;

        // Primary: route by destination MAC
        var dstMac = eth?.DestinationHardwareAddress;
        if (dstMac is not null && !dstMac.Equals(_peerMac)) return;

        // Fallback: if no eth header (loopback), filter by destination IP
        if (dstMac is null && !ip.DestinationAddress.Equals(_peerIp)) return;

        var payload = LinkEncap.IpPayload(_transport.LinkType, e.Data);
        if (payload.Length == 0) return;

        var dec = _codec.TryDecode(payload);
        if (dec.Message is { } m)
            MessageDecoded?.Invoke(m, Environment.TickCount64);
    }

    public void Dispose() => _transport.PacketReceived -= OnReceived;

    internal static IPv4Packet BuildIp(IPAddress src, IPAddress dst, byte[] payload)
        => LinkEncap.BuildUdpIp(src, dst, payload);
}
