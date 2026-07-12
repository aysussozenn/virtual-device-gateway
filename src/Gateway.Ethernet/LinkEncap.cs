using System.Net;
using System.Net.NetworkInformation;
using PacketDotNet;
using PacketDotNet.Utils;

namespace Gateway.Ethernet;

/// <summary>
/// Abstracts the link-layer encapsulation so the engine works both on real Ethernet
/// adapters (with MAC headers + ARP) and on the Windows localhost capture adapter
/// (<c>\Device\NPF_Loopback</c>), which is a BSD-style Null link: a 4-byte address
/// family header followed by the IP packet — no MAC, no ARP.
/// </summary>
public static class LinkEncap
{
    private const uint AfInet = 2; // AF_INET, host byte order (little-endian on Windows)

    /// <summary>Size of the UDP header carried inside every IPv4 frame (src/dst port, length, checksum).</summary>
    public const int UdpHeaderBytes = 8;

    // The emulator identifies frames by destination MAC, not by UDP port, so the port value is
    // arbitrary — but it must be stable so the 8-byte header shape is constant on every frame.
    private const ushort UdpPort = 0xC000;

    public static bool IsEthernet(LinkLayers type) => type == LinkLayers.Ethernet;

    /// <summary>
    /// Returns the opaque IP payload (everything after the IPv4 header) by reading the
    /// IPv4 header fields (IHL + Total Length) straight from the raw captured frame.
    /// We never touch PacketDotNet's <c>Bytes</c>/<c>PayloadPacket</c> here because it
    /// auto-parses (and, for short/odd UDP/TCP payloads, throws when serializing).
    /// </summary>
    public static byte[] IpPayload(LinkLayers linkType, byte[] frame)
    {
        var ipOffset = IsEthernet(linkType) ? 14 : 4; // Ethernet header vs Null family header
        if (frame.Length < ipOffset + 20) return Array.Empty<byte>();

        var headerBytes = (frame[ipOffset] & 0x0F) * 4;
        var totalLength = (frame[ipOffset + 2] << 8) | frame[ipOffset + 3];

        var ipEnd = ipOffset + totalLength;
        if (totalLength < headerBytes || ipEnd > frame.Length)
            ipEnd = frame.Length;                 // fall back to whatever bytes we actually have

        var payloadStart = ipOffset + headerBytes + UdpHeaderBytes; // skip the UDP header too
        return payloadStart > ipEnd ? Array.Empty<byte>() : frame[payloadStart..ipEnd];
    }

    /// <summary>
    /// Builds an IPv4/UDP packet carrying <paramref name="payload"/>. A minimal 8-byte UDP header is
    /// prepended so the on-wire header is Ethernet(14) + IP(20) + UDP(8) = 42 bytes, which is what the
    /// application expects. <see cref="IpPayload"/> strips this header symmetrically on receive.
    /// </summary>
    public static IPv4Packet BuildUdpIp(IPAddress src, IPAddress dst, byte[] payload)
    {
        var ip = new IPv4Packet(src, dst)
        {
            Protocol = ProtocolType.Udp,
            TimeToLive = 64,
            PayloadData = UdpDatagram(payload)
        };
        ip.UpdateIPChecksum();
        ip.UpdateCalculatedValues();
        return ip;
    }

    /// <summary>
    /// Prepends an 8-byte UDP header (src/dst port, length, zero checksum) to a raw payload.
    /// The checksum is left 0, which is valid and means "not computed" for IPv4 UDP.
    /// </summary>
    private static byte[] UdpDatagram(byte[] payload)
    {
        var datagram = new byte[UdpHeaderBytes + payload.Length];
        var length = (ushort)datagram.Length;

        datagram[0] = (byte)(UdpPort >> 8); datagram[1] = (byte)(UdpPort & 0xFF); // source port
        datagram[2] = (byte)(UdpPort >> 8); datagram[3] = (byte)(UdpPort & 0xFF); // destination port
        datagram[4] = (byte)(length >> 8);  datagram[5] = (byte)(length & 0xFF);  // UDP length (header + payload)
        // datagram[6..7] checksum stays 0

        Buffer.BlockCopy(payload, 0, datagram, UdpHeaderBytes, payload.Length);
        return datagram;
    }

    /// <summary>Extracts the IPv4 packet (and, on Ethernet, the Ethernet/ARP packets) from a captured frame.</summary>
    public static IPv4Packet? UnwrapIpv4(LinkLayers linkType, byte[] data, out EthernetPacket? eth, out ArpPacket? arp)
    {
        eth = null;
        arp = null;

        if (IsEthernet(linkType))
        {
            eth = Packet.ParsePacket(linkType, data) as EthernetPacket;
            if (eth is null) return null;

            // Switch on EtherType and take only the immediate child. We must NOT use
            // Extract<T>() here: it walks the whole packet chain and would lazily parse
            // the L4 child (UDP/TCP), which throws on short/odd payloads.
            switch (eth.Type)
            {
                case EthernetType.Arp:
                    arp = eth.PayloadPacket as ArpPacket;
                    return null;
                case EthernetType.IPv4:
                    return eth.PayloadPacket as IPv4Packet;
                default:
                    return null;
            }
        }

        // Null/loopback: skip the 4-byte family header, parse the rest as IPv4.
        if (data.Length <= 4 || data[0] != AfInet)
            return null;

        return new IPv4Packet(new ByteArraySegment(data[4..]));
    }

    /// <summary>Wraps an IPv4 packet into a link-layer frame ready for <c>SendPacket</c>.</summary>
    public static byte[] WrapIpv4(LinkLayers type, IPv4Packet ip, PhysicalAddress source, PhysicalAddress destination)
    {
        if (IsEthernet(type))
            return new EthernetPacket(source, destination, EthernetType.IPv4) { PayloadPacket = ip }.Bytes;

        var ipBytes = ip.Bytes;
        var frame = new byte[4 + ipBytes.Length];
        frame[0] = (byte)AfInet;
        Buffer.BlockCopy(ipBytes, 0, frame, 4, ipBytes.Length);
        return frame;
    }
}
