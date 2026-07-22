using System.Collections.Concurrent;
using System.Net;
using System.Net.NetworkInformation;

namespace Gateway.Core;

/// <summary>
/// In-memory device registry / router. Devices are keyed by their MAC address
/// (destination MAC routing) with IP as a secondary key.
/// </summary>
public sealed class DeviceRegistry : IDeviceRouter
{
    private readonly ConcurrentDictionary<IPAddress, ISimulatedDevice> _byIp = new();
    private readonly ConcurrentDictionary<string, ISimulatedDevice> _byMac = new();

    public void Register(ISimulatedDevice device)
    {
        if (_byIp.ContainsKey(device.Identity.Ip))
            throw new InvalidOperationException(
                $"A device with IP {device.Identity.Ip} is already registered (id '{_byIp[device.Identity.Ip].Identity.Id}').");

        var macKey = device.Identity.Mac.ToString();
        if (!_byMac.TryAdd(macKey, device))
            throw new InvalidOperationException(
                $"A device with MAC {macKey} is already registered.");

        // Only publish to the IP index once the MAC slot is reserved, so a rejected
        // registration never leaves a half-registered device behind.
        _byIp[device.Identity.Ip] = device;
    }

    /// <summary>Route by destination MAC (primary routing key).</summary>
    public bool TryResolveByMac(PhysicalAddress destinationMac, out ISimulatedDevice device)
        => _byMac.TryGetValue(destinationMac.ToString(), out device!);

    /// <summary>Route by destination IP (fallback).</summary>
    public bool TryResolve(IPAddress destinationIp, out ISimulatedDevice device)
        => _byIp.TryGetValue(destinationIp, out device!);

    public bool Owns(IPAddress ip) => _byIp.ContainsKey(ip);

    public bool OwnsMac(PhysicalAddress mac) => _byMac.ContainsKey(mac.ToString());

    public IReadOnlyCollection<ISimulatedDevice> Devices => (IReadOnlyCollection<ISimulatedDevice>)_byIp.Values;
}
