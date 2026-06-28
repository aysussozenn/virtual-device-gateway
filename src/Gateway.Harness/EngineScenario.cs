using Gateway.Icd;

namespace Gateway.Harness;

/// <summary>
/// Second Phase-1 example, exercising features the elevator case did not: an <c>enum</c>
/// field and an ARINC&#160;653 <em>queuing</em> port.
///
/// throttle (emulated A653 queuing source) sends THROTTLE_CMD{lever, mode}; the DUT must
/// answer fuel with FUEL_SET{flow, valve} within 40&#160;ms, where flow tracks the lever
/// saturated to the injector's 0..500&#160;g/s range and valve is an open/closed enum.
/// The buggy DUT stand-in (1) fails to saturate flow on boost and (2) drives valve to an
/// undefined code at idle — producing a range violation and an enum violation that are
/// independent of the timing/correlation check.
/// </summary>
public static class EngineScenario
{
    private static readonly IcdSpec Throttle = new()
    {
        Device = "throttle",
        Messages = new[]
        {
            new IcdMessage("THROTTLE_CMD", 0x0010, Direction.Inbound, new[]
            {
                new IcdField("lever", FieldTypes.U16, "per-mille", 0, 1000),
                new IcdField("mode",  FieldTypes.U8, Enum: new Dictionary<long, string> { [0] = "idle", [1] = "cruise", [2] = "boost" }),
            }) { Port = PortKind.Queuing, PortDir = Direction.Outbound, Depth = 8 }
        }
    };

    private static readonly IcdSpec Fuel = new()
    {
        Device = "fuel",
        Messages = new[]
        {
            new IcdMessage("FUEL_SET", 0x0030, Direction.Outbound, new[]
            {
                new IcdField("flow",  FieldTypes.U16, "g/s", 0, 500),
                new IcdField("valve", FieldTypes.U8, Enum: new Dictionary<long, string> { [0] = "closed", [1] = "open" }),
            })
        }
    };

    private sealed record Stimulus(long TMs, ushort Lever, byte Mode, long ResponseDelayMs);

    public static int Run()
    {
        var thrCodec = new IcdCodec(Throttle);
        var fuelCodec = new IcdCodec(Fuel);
        var recorder = new ConformanceRecorder();
        var trace = new List<TraceEvent>();
        ushort seq = 0;

        var stimuli = new[]
        {
            new Stimulus(0,   300, 1, 10),   // cruise: correct -> flow 300, valve open
            new Stimulus(60,  900, 2, 15),   // boost:  bug -> flow 900 (>500, not saturated)
            new Stimulus(120, 0,   0, 12),   // idle:   bug -> valve=2 (undefined enum code)
        };

        Console.WriteLine("Scenario: engine-loop  (throttle THROTTLE_CMD -> DUT -> fuel FUEL_SET within 40ms)\n");
        Console.WriteLine("Timeline:");

        foreach (var s in stimuli)
        {
            var thrFrame = thrCodec.Encode(Throttle.Messages[0], seq++,
                new Dictionary<string, double> { ["lever"] = s.Lever, ["mode"] = s.Mode });
            var thrDec = thrCodec.TryDecode(thrFrame, s.TMs);
            recorder.AddRange(thrDec.Structural);
            if (thrDec.Message is { } tm)
            {
                recorder.AddRange(ConformanceValidator.Validate(tm, s.TMs));
                trace.Add(new TraceEvent(s.TMs, "throttle", tm));
                Console.WriteLine($"  t={s.TMs,3}ms  throttle -> THROTTLE_CMD lever={s.Lever} mode={s.Mode}");
            }

            // Buggy DUT stand-in: flow = lever (no saturation); valve = undefined code 2 at idle.
            var t = s.TMs + s.ResponseDelayMs;
            ushort flow = s.Lever;
            byte valve = (byte)(s.Mode == 0 ? 2 : 1);
            var fuelFrame = fuelCodec.Encode(Fuel.Messages[0], seq++,
                new Dictionary<string, double> { ["flow"] = flow, ["valve"] = valve });
            var fuelDec = fuelCodec.TryDecode(fuelFrame, t);
            recorder.AddRange(fuelDec.Structural);
            if (fuelDec.Message is { } fm)
            {
                recorder.AddRange(ConformanceValidator.Validate(fm, t));
                trace.Add(new TraceEvent(t, "fuel", fm));
                Console.WriteLine($"  t={t,3}ms  fuel     <- FUEL_SET     flow={flow} valve={valve}");
            }
        }

        // Choreography correlates only the flow law; valve correctness is left to the enum check,
        // so an enum-only failure (idle) does not mask a passing timing/correlation obligation.
        var monitor = new ResponseMonitor("THROTTLE_CMD", "FUEL_SET", 40,
            Where: (trig, resp) => resp["flow"] == Math.Clamp(trig["lever"], 0, 500),
            Description: "flow == clamp(lever, 0..500) within 40ms");
        recorder.AddRange(MonitorEngine.Evaluate(new[] { monitor }, trace));

        Console.WriteLine();
        Console.WriteLine(recorder.Report("engine-loop"));
        return recorder.AllPassed ? 0 : 1;
    }
}
