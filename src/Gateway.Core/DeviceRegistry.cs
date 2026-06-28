using System.Collections.Concurrent;
using System.Net;

namespace Gateway.Core;

/// <summary>
/// In-memory device registry / router. Devices are keyed by their IPv4 address,
/// which is how the gateway routes incoming frames.
/// </summary>
public sealed class DeviceRegistry : IDeviceRouter
{
    private readonly ConcurrentDictionary<IPAddress, ISimulatedDevice> _byIp = new();

    public void Register(ISimulatedDevice device)
    {
        if (!_byIp.TryAdd(device.Identity.Ip, device))
            throw new InvalidOperationException(
                $"A device with IP {device.Identity.Ip} is already registered (id '{_byIp[device.Identity.Ip].Identity.Id}').");
    }

    public bool TryResolve(IPAddress destinationIp, out ISimulatedDevice device)
        => _byIp.TryGetValue(destinationIp, out device!);

    public bool Owns(IPAddress ip) => _byIp.ContainsKey(ip);

    public IReadOnlyCollection<ISimulatedDevice> Devices => (IReadOnlyCollection<ISimulatedDevice>)_byIp.Values;
}
