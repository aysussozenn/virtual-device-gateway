using Gateway.Core;

namespace Gateway.Devices.Behaviors;

/// <summary>
/// Level 3 behavior base: subclass to model a device whose reply depends on command
/// and accumulated state (counters, registers, a state machine). Still behavioral,
/// never physical. State should live in <see cref="IDeviceContext.State"/> so
/// <see cref="Reset"/> via the device clears it.
/// </summary>
public abstract class StateMachineBehavior : IDeviceBehavior
{
    public abstract ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct);

    public virtual void Reset() { }
}
