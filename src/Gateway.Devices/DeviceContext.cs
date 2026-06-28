using Gateway.Core;
using Microsoft.Extensions.Logging;

namespace Gateway.Devices;

/// <summary>Default <see cref="IDeviceContext"/> backing a <see cref="SimulatedDevice"/>.</summary>
public sealed class DeviceContext(DeviceIdentity identity, TimeProvider clock, ILogger logger) : IDeviceContext
{
    public DeviceIdentity Identity { get; } = identity;
    public TimeProvider Clock { get; } = clock;
    public ILogger Logger { get; } = logger;
    public IDictionary<string, object?> State { get; } = new Dictionary<string, object?>();
}
