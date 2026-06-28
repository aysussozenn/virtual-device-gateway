using Gateway.Icd;

namespace Gateway.Verification;

/// <summary>
/// Generic, ICD-agnostic verification: drives a <see cref="ScenarioSpec"/> timeline through
/// the loaded ICDs (per-frame conformance), evaluates the scenario's expectations as
/// choreography monitors with file-defined <c>where</c> expressions, and adds topology
/// coverage. No message names or rules are hard-coded — everything comes from the files,
/// so it runs against any developer's own ICD + scenario.
/// </summary>
public static class ScenarioRunner
{
    public static RunResult Run(SystemTopology topology, IReadOnlyList<IcdSpec> specs, ScenarioSpec scenario)
    {
        var recorder = new ConformanceRecorder();
        var trace = new List<TraceEvent>();

        // Index every message by name across all ICDs (names are unique within a system).
        var byName = new Dictionary<string, (IcdSpec Spec, IcdMessage Msg, IcdCodec Codec)>(StringComparer.Ordinal);
        foreach (var s in specs)
        {
            var codec = new IcdCodec(s);
            foreach (var m in s.Messages)
                byName.TryAdd(m.Name, (s, m, codec));
        }

        ushort seq = 0;
        foreach (var ev in scenario.Events.OrderBy(e => e.T))
        {
            if (!byName.TryGetValue(ev.Message, out var def))
            {
                recorder.Add(ConformanceResult.Violation("SCN-MSG", $"event message '{ev.Message}' is not defined in any ICD", t: ev.T));
                continue;
            }
            // Round-trip through real bytes so framing/CRC/range/enum are all exercised.
            var dec = def.Codec.TryDecode(def.Codec.Encode(def.Msg, seq++, ev.Fields), ev.T);
            recorder.AddRange(dec.Structural);
            if (dec.Message is { } m)
            {
                recorder.AddRange(ConformanceValidator.Validate(m, ev.T));
                trace.Add(new TraceEvent(ev.T, def.Spec.Device, m));
            }
        }

        var monitors = scenario.Expectations.Select(BuildMonitor).ToList();
        recorder.AddRange(MonitorEngine.Evaluate(monitors, trace));
        recorder.AddRange(TopologyValidator.Check(topology, trace));

        return new RunResult(recorder, trace, scenario.Name, null);
    }

    private static ResponseMonitor BuildMonitor(ScenarioExpectation e)
    {
        var expr = Expression.Parse(e.Where);   // throws ExpressionException on a bad rule
        return new ResponseMonitor(e.On, e.Expect, e.Within,
            (trig, resp) => expr.EvalBool(name => Resolve(name, trig, resp)),
            e.Where);
    }

    private static double Resolve(string name, DecodedMessage trig, DecodedMessage resp)
    {
        var dot = name.IndexOf('.');
        if (dot < 0) throw new ExpressionException($"identifier '{name}' must be trig.<field> or resp.<field>");
        var scope = name[..dot];
        var field = name[(dot + 1)..];
        var msg = scope switch
        {
            "trig" => trig,
            "resp" => resp,
            _ => throw new ExpressionException($"unknown scope '{scope}' (use trig/resp)")
        };
        return msg.Fields.TryGetValue(field, out var v)
            ? v
            : throw new ExpressionException($"field '{field}' is not in message '{msg.Name}'");
    }
}
