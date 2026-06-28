namespace Gateway.Icd;

/// <summary>
/// A promiscuous, multi-ICD conformance tap — the live counterpart to a packet capture
/// that sees every frame on the medium regardless of who it is addressed to. Each observed
/// IP payload is decoded against the whole set of participant ICDs (matched by command),
/// validated, and appended to one global trace. That single cross-peer trace is what the
/// <see cref="MonitorEngine"/> (choreography) and <see cref="TopologyValidator"/> (coverage)
/// then evaluate, so the same checks work over real asynchronous traffic.
/// </summary>
public sealed class SystemConformanceTap
{
    private readonly IReadOnlyList<(IcdSpec Spec, IFrameCodec Codec)> _codecs;
    private readonly ConformanceRecorder _recorder;
    private readonly List<TraceEvent> _trace = new();
    private readonly object _gate = new();

    public SystemConformanceTap(IEnumerable<IcdSpec> specs, ConformanceRecorder recorder)
    {
        _codecs = specs.Select(s => (s, CodecRegistry.Default.Build(s))).ToList();
        _recorder = recorder;
    }

    public IReadOnlyList<TraceEvent> Trace { get { lock (_gate) return _trace.ToList(); } }

    /// <summary>Decodes/validates one frame; returns the recognized message name, or null if none matched.</summary>
    public string? Observe(ReadOnlySpan<byte> payload, long timestampMs)
    {
        lock (_gate)
        {
            DecodeResult? owned = null;
            foreach (var (spec, codec) in _codecs)
            {
                var dec = codec.TryDecode(payload, timestampMs);
                if (dec.Message is { } m)
                {
                    _recorder.AddRange(ConformanceValidator.Validate(m, timestampMs));
                    _trace.Add(new TraceEvent(timestampMs, spec.Device, m));
                    return m.Name;
                }
                // Remember the spec that recognized the command but failed structurally (CRC/length),
                // so we report that instead of a generic "unknown" when no spec fully decodes.
                if (owned is null && dec.Structural.Any(r => r.RuleId != "ICD-CMD")) owned = dec;
            }

            if (owned is { } o) _recorder.AddRange(o.Structural);
            else _recorder.Add(ConformanceResult.Violation("ICD-UNKNOWN", "frame not recognized by any ICD", t: timestampMs));
            return null;
        }
    }
}
