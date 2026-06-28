using Microsoft.Extensions.Logging;

namespace Gateway.Core;

/// <summary>
/// Pluggable strategy describing how a device responds to requests. This is the
/// single seam for device behavior: canned responses, recorded playback, a state
/// machine, or (later) a user-supplied plugin all implement this interface.
/// Deliberately models the <em>interface contract</em>, not device physics.
/// </summary>
public interface IDeviceBehavior
{
    /// <summary>
    /// Produces a reply for the given request, or <c>null</c> to stay silent
    /// (simulating a timeout / unreachable device).
    /// </summary>
    ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct);

    /// <summary>Resets any internal state so test scenarios are repeatable.</summary>
    void Reset() { }
}

/// <summary>
/// Ambient services available to a behavior: the device's own identity, a testable
/// clock, a logger, and a per-device scratch state bag.
/// </summary>
public interface IDeviceContext
{
    DeviceIdentity Identity { get; }
    TimeProvider Clock { get; }
    ILogger Logger { get; }
    IDictionary<string, object?> State { get; }
}
