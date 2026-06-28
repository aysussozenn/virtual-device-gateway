using Gateway.Icd;

namespace Gateway.Peers;

/// <summary>
/// Periodic sender for one sampling (ARINC 653) message: every <c>periodMs</c> it reads the
/// latest atomically-published <see cref="FieldSnapshot"/> and transmits it, advancing the
/// sequence number. This is the "periodic message keeps flowing" half — a slider edit just
/// rides the next tick out onto the wire. Aperiodic messages do not use this; they are sent
/// one-shot via <see cref="PeerChannel.Send"/>.
/// </summary>
public sealed class PeerStream : IDisposable
{
    private readonly PeerChannel _channel;
    private readonly IcdMessage _message;
    private readonly int _periodMs;

    private FieldSnapshot _snapshot = FieldSnapshot.Empty;
    private Timer? _timer;
    private ushort _seq;

    public PeerStream(PeerChannel channel, IcdMessage message, int periodMs)
    {
        _channel = channel;
        _message = message;
        _periodMs = Math.Max(1, periodMs);
    }

    public bool IsRunning => _timer is not null;

    /// <summary>Atomically publishes the values the next tick will send.</summary>
    public void UpdateValues(FieldSnapshot snapshot) => Interlocked.Exchange(ref _snapshot, snapshot);

    public void Start() => _timer ??= new Timer(_ => Tick(), null, 0, _periodMs);

    public void Stop()
    {
        _timer?.Dispose();
        _timer = null;
    }

    private void Tick()
    {
        var snapshot = Volatile.Read(ref _snapshot);
        try { _channel.Send(_message, snapshot.Values, _seq++); }
        catch { /* a transient send failure must not kill the timer */ }
    }

    public void Dispose() => Stop();
}
