using System.Net;
using System.Net.NetworkInformation;

namespace Gateway.Core;

/// <summary>
/// Resolves the destination MAC (primary) or IP (fallback) of an incoming frame to a simulated device.
/// </summary>
public interface IDeviceRouter
{
    /// <summary>Route by destination MAC — primary routing key.</summary>
    bool TryResolveByMac(PhysicalAddress destinationMac, out ISimulatedDevice device);

    /// <summary>Route by destination IP — fallback.</summary>
    bool TryResolve(IPAddress destinationIp, out ISimulatedDevice device);

    /// <summary>True if the given MAC belongs to one of the simulated devices.</summary>
    bool OwnsMac(PhysicalAddress mac);

    /// <summary>True if the given IP belongs to one of the simulated devices (used by the ARP responder).</summary>
    bool Owns(IPAddress ip);

    IReadOnlyCollection<ISimulatedDevice> Devices { get; }
}
