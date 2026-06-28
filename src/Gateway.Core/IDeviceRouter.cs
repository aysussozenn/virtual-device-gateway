using System.Net;

namespace Gateway.Core;

/// <summary>
/// Resolves the destination IP of an incoming frame to a simulated device.
/// </summary>
public interface IDeviceRouter
{
    bool TryResolve(IPAddress destinationIp, out ISimulatedDevice device);

    /// <summary>True if the given IP belongs to one of the simulated devices (used by the ARP responder).</summary>
    bool Owns(IPAddress ip);

    IReadOnlyCollection<ISimulatedDevice> Devices { get; }
}
