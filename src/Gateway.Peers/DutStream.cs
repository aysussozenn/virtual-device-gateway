using Gateway.Icd;

namespace Gateway.Peers;

/// <summary>
/// Periodic sender for one DUT → peer sampling message — the DUT-side mirror of
/// <see cref="PeerStream"/>. Every <c>periodMs</c> it transmits the latest atomically-published
/// <see cref="FieldSnapshot"/> via <see cref="DutEndpoint.Send"/>, advancing the sequence number.
/// A field edit just rides the next tick out onto the wire. Aperiodic (queuing) messages do not
/// use this; they are sent one-shot via <see cref="DutEndpoint.Send"/> directly.
/// </summary>
public sealed class DutStream : IDisposable
{
    private readonly DutEndpoint _endpoint;
    private readonly string _peerId;
    private readonly IcdMessage _message;
    private readonly int _periodMs;

    private FieldSnapshot _snapshot = FieldSnapshot.Empty;
    private Timer? _timer;
    private ushort _seq;

    public DutStream(DutEndpoint endpoint, string peerId, IcdMessage message, int periodMs)
    {
        _endpoint = endpoint;
        _peerId = peerId;
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
        try { _endpoint.Send(_peerId, _message, snapshot.Values, _seq++); }
        catch { /* a transient send failure must not kill the timer */ }
    }

    public void Dispose() => Stop();
}
