using System.Diagnostics;
using Gateway.Ethernet;
using Gateway.Icd;

namespace Gateway.Harness;

/// <summary>
/// Live per-frame conformance: subscribes to <see cref="GatewayEngine.FrameObserved"/> and,
/// for every IPv4 request the DUT sends to a managed device, decodes the IP payload against
/// the device's ICD and validates it — in real time, as frames traverse the engine. The
/// engine stays oblivious to the ICD; this listener is a pure observer over the frame tap,
/// which is exactly how the design keeps the protocol spec confined to one seam.
/// </summary>
public sealed class LiveConformanceMonitor(IcdSpec spec, ConformanceRecorder recorder)
{
    private readonly IcdCodec _codec = new(spec);
    private readonly List<TraceEvent> _trace = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public IReadOnlyList<TraceEvent> Trace => _trace;

    public void OnFrame(object? sender, FrameObservedEventArgs e)
    {
        // The DUT's outbound traffic arrives at the engine as Ipv4Request frames; the device's
        // own replies (Ipv4Reply) belong to a different message and are not checked here.
        if (e.Kind != FrameKind.Ipv4Request || e.Payload.Length == 0) return;

        var t = (long)_clock.Elapsed.TotalMilliseconds;
        var dec = _codec.TryDecode(e.Payload.Span, t);
        recorder.AddRange(dec.Structural);

        if (dec.Message is not { } m)
        {
            var fail = dec.Structural.FirstOrDefault(r => r.IsFailure);
            Console.WriteLine($"  t={t,4}ms  {e.DeviceId,-5} <- (undecodable)  [FAIL {fail?.RuleId}]");
            return;
        }

        var results = ConformanceValidator.Validate(m, t);
        recorder.AddRange(results);
        _trace.Add(new TraceEvent(t, e.DeviceId ?? spec.Device, m));

        var failures = results.Where(r => r.IsFailure).ToList();
        var fields = string.Join(" ", m.Fields.Select(kv => $"{kv.Key}={(int)kv.Value}"));
        var verdict = failures.Count == 0 ? "PASS" : "FAIL " + string.Join(",", failures.Select(r => r.RuleId));
        Console.WriteLine($"  t={t,4}ms  {e.DeviceId,-5} <- {m.Name} {fields}  [{verdict}]");
    }
}
