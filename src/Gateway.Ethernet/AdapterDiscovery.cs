using SharpPcap;
using SharpPcap.LibPcap;

namespace Gateway.Ethernet;

/// <summary>Helpers for enumerating and selecting capture adapters.</summary>
public static class AdapterDiscovery
{
    /// <summary>All capture devices visible to Npcap/libpcap.</summary>
    public static IReadOnlyList<ILiveDevice> List()
    {
        try
        {
            return CaptureDeviceList.Instance.Cast<ILiveDevice>().ToList();
        }
        catch (DllNotFoundException ex)
        {
            throw new InvalidOperationException(
                "Npcap does not appear to be installed. Install Npcap (with 'WinPcap API-compatible mode' " +
                "and loopback support) from https://npcap.com, then retry.", ex);
        }
    }

    /// <summary>Best-effort lookup of the Npcap loopback adapter.</summary>
    public static ILiveDevice? FindLoopback()
        => List().FirstOrDefault(d =>
            (d.Description?.Contains("loopback", StringComparison.OrdinalIgnoreCase) ?? false) ||
            (d.Name?.Contains("loopback", StringComparison.OrdinalIgnoreCase) ?? false));

    public static ILiveDevice? FindByName(string name)
        => List().FirstOrDefault(d => string.Equals(d.Name, name, StringComparison.OrdinalIgnoreCase));
}
