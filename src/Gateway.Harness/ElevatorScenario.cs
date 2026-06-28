using Gateway.Icd;

namespace Gateway.Harness;

/// <summary>
/// Phase-1 conformance demonstration: the "elevator loop" walkthrough, run for real.
///
/// nav (emulated A653 sampling source) publishes NAV_STATE; the DUT is expected to
/// answer surf with SetElevator within 30&#160;ms, deflecting to counter the pitch and
/// saturating at &#177;30&#176;. Here the DUT is stood in by a deliberately buggy model
/// (no clamping, one dropped response) so the run exercises a structural pass, a range
/// violation, a choreography value-mismatch, and a timeout. In a live run these bytes
/// come off the wire from the real DUT via the gateway; only the producer changes.
/// </summary>
public static class ElevatorScenario
{
    // --- ICDs (transcribed field tables) ---
    private static readonly IcdSpec Nav = new()
    {
        Device = "nav",
        Messages = new[]
        {
            new IcdMessage("NAV_STATE", 0x0001, Direction.Inbound, new[]
            {
                new IcdField("heading", FieldTypes.U16, "deci-deg", 0, 3599),
                new IcdField("pitch",   FieldTypes.I16, "deci-deg", -900, 900),
            }) { Port = PortKind.Sampling, PortDir = Direction.Outbound, RefreshMs = 50 }
        }
    };

    /// <summary>Public so the live conformance demo drives real frames against the very same ICD.</summary>
    public static readonly IcdSpec Surf = new()
    {
        Device = "surf",
        Messages = new[]
        {
            new IcdMessage("SetElevator", 0x0021, Direction.Outbound, new[]
            {
                new IcdField("deflection", FieldTypes.I16, "deci-deg", -300, 300),
            })
        }
    };

    private sealed record Stimulus(long TMs, short Pitch, long? ResponseDelayMs);

    public static int Run()
    {
        var navCodec = new IcdCodec(Nav);
        var surfCodec = new IcdCodec(Surf);
        var recorder = new ConformanceRecorder();
        var trace = new List<TraceEvent>();
        ushort seq = 0;

        // (time, pitch, DUT response delay) — index 2 has no response => timeout.
        var stimuli = new[]
        {
            new Stimulus(0,   120, 8),    // correct: deflection -120
            new Stimulus(50,  340, 20),   // bug: -340, out of range AND not clamped
            new Stimulus(100, 50,  null), // bug: dropped response
        };

        Console.WriteLine("Scenario: elevator-loop  (nav NAV_STATE -> DUT -> surf SetElevator within 30ms)\n");
        Console.WriteLine("Timeline:");

        foreach (var s in stimuli)
        {
            // nav publishes NAV_STATE (the stimulus) — goes through the real codec + validator.
            var navFrame = navCodec.Encode(Nav.Messages[0], seq++,
                new Dictionary<string, double> { ["heading"] = 900, ["pitch"] = s.Pitch });
            var navDec = navCodec.TryDecode(navFrame, s.TMs);
            recorder.AddRange(navDec.Structural);
            if (navDec.Message is { } nm)
            {
                recorder.AddRange(ConformanceValidator.Validate(nm, s.TMs));
                trace.Add(new TraceEvent(s.TMs, "nav", nm));
                Console.WriteLine($"  t={s.TMs,3}ms  nav  -> NAV_STATE  pitch={s.Pitch}");
            }

            if (s.ResponseDelayMs is not { } delay) { Console.WriteLine($"           (DUT sends no SetElevator)"); continue; }

            // Buggy DUT stand-in: deflection = -pitch with NO saturation.
            var t = s.TMs + delay;
            short deflection = (short)(-s.Pitch);
            var surfFrame = surfCodec.Encode(Surf.Messages[0], seq++,
                new Dictionary<string, double> { ["deflection"] = deflection });
            var surfDec = surfCodec.TryDecode(surfFrame, t);
            recorder.AddRange(surfDec.Structural);
            if (surfDec.Message is { } sm)
            {
                recorder.AddRange(ConformanceValidator.Validate(sm, t));
                trace.Add(new TraceEvent(t, "surf", sm));
                Console.WriteLine($"  t={t,3}ms  surf <- SetElevator deflection={deflection}");
            }
        }

        // Choreography: cross-message causal + data-correlation obligation.
        var monitor = new ResponseMonitor("NAV_STATE", "SetElevator", 30,
            Where: (trig, resp) => resp["deflection"] == Math.Clamp(-trig["pitch"], -300, 300),
            Description: "deflection == clamp(-pitch, +/-300) within 30ms");
        recorder.AddRange(MonitorEngine.Evaluate(new[] { monitor }, trace));

        Console.WriteLine();
        Console.WriteLine(recorder.Report("elevator-loop"));
        return recorder.AllPassed ? 0 : 1;
    }
}
