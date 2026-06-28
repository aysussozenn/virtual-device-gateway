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

        var payloadStart = ipOffset + headerBytes;
        return payloadStart > ipEnd ? Array.Empty<byte>() : frame[payloadStart..ipEnd];
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
