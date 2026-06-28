namespace Gateway.Core;

/// <summary>
/// A simulated device: identity plus a delegated <see cref="IDeviceBehavior"/>.
/// The gateway resolves a device by IP and calls <see cref="HandleAsync"/>; how the
/// reply is produced is entirely the behavior's concern.
/// </summary>
public interface ISimulatedDevice
{
    DeviceIdentity Identity { get; }

    /// <summary>Current named parameter values, if the behavior exposes any; otherwise empty.</summary>
    IReadOnlyList<DeviceParameter> Parameters { get; }

    ValueTask<DeviceReply?> HandleAsync(DeviceRequest request, CancellationToken ct);

    /// <summary>Resets the device (and its behavior) to a known initial state.</summary>
    void Reset();
}
