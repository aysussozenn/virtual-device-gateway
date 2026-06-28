using Gateway.Core;

namespace Gateway.Devices.Behaviors;

/// <summary>
/// Device-level fault policy. This is a deliberate test feature (robustness testing),
/// not environmental physics. Transport-level faults (bad checksum, dropped frame,
/// corrupted IP header) live in the Ethernet pipeline instead.
/// </summary>
public sealed record FaultPolicy(
    double DropProbability = 0,        // return null -> device stays silent (timeout)
    TimeSpan ExtraLatency = default,   // delay before replying
    double CorruptProbability = 0);    // flip a byte in the reply payload

/// <summary>
/// Decorator that wraps any <see cref="IDeviceBehavior"/> and injects faults around it.
/// </summary>
public sealed class FaultInjectingBehavior(IDeviceBehavior inner, FaultPolicy policy, Random? rng = null) : IDeviceBehavior
{
    private readonly Random _rng = rng ?? Random.Shared;

    public async ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct)
    {
        if (policy.DropProbability > 0 && _rng.NextDouble() < policy.DropProbability)
            return null;

        if (policy.ExtraLatency > TimeSpan.Zero)
            await Task.Delay(policy.ExtraLatency, context.Clock, ct).ConfigureAwait(false);

        var reply = await inner.RespondAsync(request, context, ct).ConfigureAwait(false);
        if (reply is null)
            return null;

        if (policy.CorruptProbability > 0 && reply.Data.Length > 0 && _rng.NextDouble() < policy.CorruptProbability)
            reply = reply with { Data = Corrupt(reply.Data.Span) };

        return reply;
    }

    public void Reset() => inner.Reset();

    private byte[] Corrupt(ReadOnlySpan<byte> data)
    {
        var copy = data.ToArray();
        var i = _rng.Next(copy.Length);
        copy[i] ^= (byte)(1 << _rng.Next(8));
        return copy;
    }
}
