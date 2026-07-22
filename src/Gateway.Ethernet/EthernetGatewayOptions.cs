using System.Net.NetworkInformation;

namespace Gateway.Ethernet;

/// <summary>Runtime options for the capture/send engine.</summary>
public sealed class EthernetGatewayOptions
{
    /// <summary>libpcap/Npcap adapter name to bind to (e.g. the Npcap Loopback Adapter).</summary>
    public string AdapterName { get; set; } = string.Empty;

    /// <summary>BPF capture filter. Default limits traffic to IPv4 and ARP.</summary>
    public string CaptureFilter { get; set; } = "ip or arp";

    /// <summary>Whether the gateway answers ARP requests on behalf of simulated devices.</summary>
    public bool AnswerArp { get; set; } = true;

    public int ReadTimeoutMs { get; set; } = 1000;

    /// <summary>
    /// Fixed source MAC address used on all outgoing frames (bytes 6-11 of the Ethernet header).
    /// When null, each device's own MAC is used as the source.
    /// Set this to the constant MAC your protocol requires.
    /// Example: PhysicalAddress.Parse("AA-BB-CC-DD-EE-FF")
    /// </summary>
    public PhysicalAddress? FixedSrcMac { get; set; } = null;
}
