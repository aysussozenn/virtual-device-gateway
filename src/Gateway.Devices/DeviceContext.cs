using Gateway.Core;
using Microsoft.Extensions.Logging;

namespace Gateway.Devices;

/// <summary>Default <see cref="IDeviceContext"/> backing a <see cref="SimulatedDevice"/>.</summary>
public sealed class DeviceContext : IDeviceContext
{
    public DeviceContext(DeviceIdentity identity, IClock clock, ILogger logger)
    {
        Identity = identity;
        Clock = clock;
        Logger = logger;
    }

    public DeviceIdentity Identity { get; }
    public IClock Clock { get; }
    public ILogger Logger { get; }
    public IDictionary<string, object?> State { get; } = new Dictionary<string, object?>();
}
