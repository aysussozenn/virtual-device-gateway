using Gateway.Core;

namespace Gateway.Devices.Behaviors;

/// <summary>
/// Level 2 behavior: replays a fixed sequence of replies, one per request. Useful for
/// "the real device produced this trace" scenarios. Optionally loops; otherwise goes
/// silent once the sequence is exhausted.
/// </summary>
public sealed class PlaybackBehavior(IReadOnlyList<DeviceReply> sequence, bool loop = false) : IDeviceBehavior
{
    private readonly IReadOnlyList<DeviceReply> _sequence = sequence;
    private int _index;

    public ValueTask<DeviceReply?> RespondAsync(DeviceRequest request, IDeviceContext context, CancellationToken ct)
    {
        if (_sequence.Count == 0)
            return ValueTask.FromResult<DeviceReply?>(null);

        if (_index >= _sequence.Count)
        {
            if (!loop) return ValueTask.FromResult<DeviceReply?>(null);
            _index = 0;
        }

        return ValueTask.FromResult<DeviceReply?>(_sequence[_index++]);
    }

    public void Reset() => _index = 0;
}
