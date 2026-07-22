using Microsoft.Extensions.Logging;

namespace Gateway.Core;

/// <summary>
/// Minimal clock abstraction — replaces System.TimeProvider (a .NET 8 type)
/// so the project can target net7.0 without any extra NuGet package.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
    Task DelayAsync(TimeSpan delay, CancellationToken ct);
}

/// <summary>Production clock backed by the real wall clock.</summary>
public sealed class SystemClock : IClock
{
    public static readonly IClock Instance = new SystemClock();
    private SystemClock() { }
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    public Task DelayAsync(TimeSpan delay, CancellationToken ct) => Task.Delay(delay, ct);
}

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
    IClock Clock { get; }
    ILogger Logger { get; }
    IDictionary<string, object?> State { get; }
}
