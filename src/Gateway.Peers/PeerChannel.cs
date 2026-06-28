using System.Net;
using System.Net.NetworkInformation;
using Gateway.Ethernet;
using Gateway.Icd;
using PacketDotNet;

namespace Gateway.Peers;

/// <summary>Static description of an emulated peer: its ICD plus raw-Ethernet addressing.</summary>
public sealed record PeerDescriptor(string Id, IcdSpec Spec, IPAddress Ip, PhysicalAddress Mac);

/// <summary>
/// One emulated peer's send/receive endpoint on the wire. It encodes a message from named
/// field values into an ICD frame (peer → DUT) and decodes inbound frames addressed to this
/// peer (DUT → peer) back into named values. The transport medium (in-memory bus or a real
/// adapter) is injected, so the same channel works for the demo and live paths unchanged.
/// </summary>
public sealed class PeerChannel : IDisposable
{
    private readonly IPacketTransport _transport;
    private readonly IFrameCodec _codec;
    private readonly IPAddress _peerIp;
    private readonly IPAddress _dutIp;
    private readonly PhysicalAddress _peerMac;
    private readonly PhysicalAddress _dutMac;

    public string PeerId { get; }
    public IcdSpec Spec { get; }
    public IPAddress PeerIp => _peerIp;

    /// <summary>Raised when a frame addressed to this peer decodes against its ICD. The long is a
    /// monotonic millisecond timestamp (<see cref="Environment.TickCount64"/>) for freshness math.</summary>
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
        _transport.Send(LinkEncap.WrapIpv4(_transport.LinkType, ip, _peerMac, _dutMac));
    }

    private void OnReceived(object? sender, PacketReceivedEventArgs e)
    {
        var ip = LinkEncap.UnwrapIpv4(_transport.LinkType, e.Data, out _, out _);
        if (ip is null || !ip.DestinationAddress.Equals(_peerIp)) return;

        var payload = LinkEncap.IpPayload(_transport.LinkType, e.Data);
        if (payload.Length == 0) return;

        var dec = _codec.TryDecode(payload);
        if (dec.Message is { } m)
            MessageDecoded?.Invoke(m, Environment.TickCount64);
    }

    public void Dispose() => _transport.PacketReceived -= OnReceived;

    internal static IPv4Packet BuildIp(IPAddress src, IPAddress dst, byte[] payload)
    {
        var ip = new IPv4Packet(src, dst) { Protocol = ProtocolType.Udp, TimeToLive = 64, PayloadData = payload };
        ip.UpdateIPChecksum();
        ip.UpdateCalculatedValues();
        return ip;
    }
}
