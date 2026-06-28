using System.Net;
using System.Net.NetworkInformation;

namespace Gateway.Core;

/// <summary>
/// Identity of a simulated device. The gateway routes incoming IPv4 frames to a
/// device by matching <see cref="Ip"/>; <see cref="Mac"/> is used when answering
/// ARP and when building reply frames (it becomes the source MAC).
/// </summary>
public sealed record DeviceIdentity(string Id, IPAddress Ip, PhysicalAddress Mac);

/// <summary>
/// The remote peer that sent a request (i.e. the "App B" side). We keep its IP and
/// MAC so a reply frame can be addressed straight back to it without an ARP lookup.
/// </summary>
public sealed record PeerEndpoint(IPAddress Ip, PhysicalAddress Mac);
