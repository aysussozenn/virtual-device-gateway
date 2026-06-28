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
}
