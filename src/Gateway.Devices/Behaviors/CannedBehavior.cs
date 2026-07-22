using Gateway.Core;

namespace Gateway.Devices.Behaviors;

/// <summary>
/// Level 1 behavior: returns a fixed reply per command code. Covers most functional
/// tests ("when my code sends command X, the device answers Y").
/// </summary>
public sealed class CannedBehavior : IDeviceBehavior
{
    private readonly IReadOnlyDictionary<ushort, DeviceReply> _responses;

    public CannedBehavior(IReadOnlyDictionary<ushort, DeviceReply> responses)
    {
        _responses = responses;
    }

    public ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct)
        => ValueTask.FromResult(_responses.TryGetValue(request.Command, out var reply) ? reply : null);
}
