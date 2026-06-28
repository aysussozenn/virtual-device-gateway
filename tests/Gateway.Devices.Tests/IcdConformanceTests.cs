using Gateway.Icd;
using Gateway.Verification;

namespace Gateway.Devices.Tests;

public class IcdConformanceTests
{
    private static readonly IcdSpec Surf = new()
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

    private static byte[] Frame(short deflection, ushort seq = 1)
        => new IcdCodec(Surf).Encode(Surf.Messages[0], seq,
            new Dictionary<string, double> { ["deflection"] = deflection });

    [Fact]
    public void Roundtrip_decodes_signed_field()
    {
        var dec = new IcdCodec(Surf).TryDecode(Frame(-120));
        Assert.NotNull(dec.Message);
        Assert.Equal(-120, dec.Message!["deflection"]);
    }

    [Fact]
    public void In_range_value_conforms()
    {
        var dec = new IcdCodec(Surf).TryDecode(Frame(-120));
        var results = ConformanceValidator.Validate(dec.Message!);
        Assert.DoesNotContain(results, r => r.IsFailure);
    }

    [Fact]
    public void Out_of_range_value_is_a_range_violation()
    {
        var dec = new IcdCodec(Surf).TryDecode(Frame(-340));
        var results = ConformanceValidator.Validate(dec.Message!);
        Assert.Contains(results, r => r.RuleId == "ICD-RANGE" && r.IsFailure);
    }

    [Fact]
    public void Corrupted_checksum_is_a_structural_violation()
    {
        var f = Frame(-120);
        f[^1] ^= 0xFF; // flip CRC low byte
        var dec = new IcdCodec(Surf).TryDecode(f);
        Assert.Null(dec.Message);
        Assert.Contains(dec.Structural, r => r.RuleId == "ICD-CRC");
    }

    [Fact]
    public void Unknown_command_is_rejected()
    {
        var f = Frame(0);
        f[2] = 0x99; f[3] = 0x99; // overwrite command id (CRC now also wrong, but command is checked first)
        var dec = new IcdCodec(Surf).TryDecode(f);
        Assert.Null(dec.Message);
        Assert.Contains(dec.Structural, r => r.RuleId == "ICD-CMD");
    }

    [Fact]
    public void Monitor_flags_timeout_when_response_missing()
    {
        var nav = new IcdMessage("NAV_STATE", 0x0001, Direction.Inbound, new[] { new IcdField("pitch", FieldTypes.I16) });
        var trig = new DecodedMessage(nav, 1, new Dictionary<string, double> { ["pitch"] = 50 });
        var trace = new[] { new TraceEvent(100, "nav", trig) }; // no SetElevator follows

        var monitor = new ResponseMonitor("NAV_STATE", "SetElevator", 30,
            (_, r) => r["deflection"] == 0, "test");
        var results = MonitorEngine.Evaluate(new[] { monitor }, trace);

        Assert.Contains(results, r => r.RuleId == "CHOREO-TIMEOUT" && r.IsFailure);
    }

    [Fact]
    public void Loader_parses_spec_and_codec_roundtrips()
    {
        var json = """
        { "device": "surf", "magic": "0xAA55", "endianness": "big", "checksum": "crc16-ccitt",
          "messages": [ { "name": "SetElevator", "command": "0x0021", "direction": "outbound",
            "fields": [ { "name": "deflection", "type": "i16", "min": -300, "max": 300 } ] } ] }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"icd_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var spec = IcdLoader.LoadSpec(path);
            var codec = new IcdCodec(spec);
            var frame = codec.Encode(spec.Messages[0], 1, new Dictionary<string, double> { ["deflection"] = -120 });
            var dec = codec.TryDecode(frame);
            Assert.Equal(-120, dec.Message!["deflection"]);
            Assert.Equal((ushort)0x0021, spec.Messages[0].Command);
        }
        finally { File.Delete(path); }
    }

    private sealed class FakeReader : IIcdFormatReader
    {
        public bool CanRead(string path) => path.EndsWith(".fake", StringComparison.OrdinalIgnoreCase);
        public IcdSpec Read(string path, FieldTypeRegistry types) =>
            new() { Device = "fake", Messages = new[] { new IcdMessage("M", 1, Direction.Inbound, new[] { new IcdField("x", FieldTypes.U8) }) } };
    }

    [Fact]
    public void Custom_format_reader_is_selected_by_registry()
    {
        var readers = IcdReaderRegistry.CreateDefault();
        readers.Register(new FakeReader());

        Assert.IsType<FakeReader>(readers.For("module.fake"));   // new format dispatched to the plugin
        Assert.IsType<JsonIcdReader>(readers.For("module.json")); // built-in still handles JSON
    }

    private sealed class NoopCodec : IFrameCodec
    {
        public byte[] Encode(IcdMessage message, ushort sequence, IReadOnlyDictionary<string, double> values) => Array.Empty<byte>();
        public DecodeResult TryDecode(ReadOnlySpan<byte> frame, long timestampMs = 0) => new(null, Array.Empty<ConformanceResult>());
    }

    [Fact]
    public void Custom_framing_codec_is_built_from_registry()
    {
        var registry = CodecRegistry.CreateDefault();
        registry.Register("vendor-x", _ => new NoopCodec());

        var msgs = new[] { new IcdMessage("M", 1, Direction.Inbound, new[] { new IcdField("x", FieldTypes.U8) }) };
        Assert.IsType<NoopCodec>(registry.Build(new IcdSpec { Device = "d", Framing = "vendor-x", Messages = msgs }));
        Assert.IsType<IcdCodec>(registry.Build(new IcdSpec { Device = "d", Messages = msgs })); // default framing → built-in
    }

    [Fact]
    public void Loader_reports_bad_field_type_with_context()
    {
        var json = """
        { "device": "x", "messages": [ { "name": "M", "command": "0x01", "direction": "inbound",
          "fields": [ { "name": "f", "type": "u17" } ] } ] }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"icd_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var ex = Assert.Throws<IcdLoadException>(() => IcdLoader.LoadSpec(path));
            Assert.Contains("M.f.type", ex.Message);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Expression_evaluates_clamp_comparison()
    {
        var e = Expression.Parse("resp.deflection == clamp(-trig.pitch, -300, 300)");
        Assert.True(e.EvalBool(n => n switch { "trig.pitch" => 340, "resp.deflection" => -300, _ => 0 }));
        Assert.False(e.EvalBool(n => n switch { "trig.pitch" => 340, "resp.deflection" => -340, _ => 0 }));
    }

    [Fact]
    public void Scenario_runner_flags_range_and_choreography_from_data()
    {
        var nav = new IcdSpec
        {
            Device = "nav",
            Messages = new[] { new IcdMessage("NAV_STATE", 0x0001, Direction.Inbound, new[] { new IcdField("pitch", FieldTypes.I16, "deci-deg", -900, 900) }) }
        };
        var topo = new SystemTopology
        {
            Dut = "fcc",
            Participants = new[] { new Participant("nav", "nav"), new Participant("surf", "surf") },
            Interfaces = new[] { new Interface("nav", "fcc", new[] { "NAV_STATE" }), new Interface("fcc", "surf", new[] { "SetElevator" }) }
        };
        var scn = new ScenarioSpec
        {
            Name = "t",
            Events = new[]
            {
                new ScenarioEvent(0, "NAV_STATE", new Dictionary<string, double> { ["pitch"] = 340 }),
                new ScenarioEvent(10, "SetElevator", new Dictionary<string, double> { ["deflection"] = -340 }),
            },
            Expectations = new[] { new ScenarioExpectation("NAV_STATE", "SetElevator", 30, "resp.deflection == clamp(-trig.pitch, -300, 300)") }
        };

        var result = ScenarioRunner.Run(topo, new[] { nav, Surf }, scn);

        Assert.Null(result.Error);
        Assert.Contains(result.Recorder.Results, r => r.RuleId == "ICD-RANGE" && r.IsFailure);
        Assert.Contains(result.Recorder.Results, r => r.RuleId == "CHOREO-WHERE" && r.IsFailure);
    }

    [Fact]
    public void Reports_render_failures_in_json_and_junit()
    {
        var rec = new ConformanceRecorder();
        rec.Add(ConformanceResult.Pass("ICD-OK", "ok", "M"));
        rec.Add(ConformanceResult.Violation("ICD-RANGE", "out of range", "M", 2, 70, "[-300,300]", "-340"));

        var junit = System.Xml.Linq.XDocument.Parse(ReportWriter.ToJUnit(rec, "s"));
        var suite = junit.Root!.Element("testsuite")!;
        Assert.Equal("2", suite.Attribute("tests")!.Value);
        Assert.Equal("1", suite.Attribute("failures")!.Value);
        Assert.Single(suite.Elements("testcase"), tc => tc.Element("failure") is not null);

        using var json = System.Text.Json.JsonDocument.Parse(ReportWriter.ToJson(rec, "s"));
        Assert.Equal(1, json.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public void Topology_flags_undelivered_interface_message()
    {
        var topo = new SystemTopology
        {
            Dut = "fcc",
            Participants = new[] { new Participant("surf", "surf.json"), new Participant("logsvc", "logsvc.json") },
            Interfaces = new[]
            {
                new Interface("fcc", "surf", new[] { "SetElevator" }),
                new Interface("fcc", "logsvc", new[] { "Heartbeat" }),
            }
        };
        var surf = Surf.Messages[0];
        var trace = new[] { new TraceEvent(0, "surf", new DecodedMessage(surf, 1, new Dictionary<string, double> { ["deflection"] = 0 })) };

        var results = TopologyValidator.Check(topo, trace);

        Assert.Contains(results, r => r.RuleId == "COVERAGE" && r.Message.Contains("SetElevator"));
        Assert.Contains(results, r => r.RuleId == "COVERAGE-MISSING" && r.Message.Contains("Heartbeat"));
    }

    [Fact]
    public void Monitor_passes_when_correlated_response_in_window()
    {
        var nav = new IcdMessage("NAV_STATE", 0x0001, Direction.Inbound, new[] { new IcdField("pitch", FieldTypes.I16) });
        var surf = Surf.Messages[0];
        var trig = new DecodedMessage(nav, 1, new Dictionary<string, double> { ["pitch"] = 120 });
        var resp = new DecodedMessage(surf, 2, new Dictionary<string, double> { ["deflection"] = -120 });
        var trace = new[]
        {
            new TraceEvent(0, "nav", trig),
            new TraceEvent(8, "surf", resp),
        };

        var monitor = new ResponseMonitor("NAV_STATE", "SetElevator", 30,
            (t, r) => r["deflection"] == Math.Clamp(-t["pitch"], -300, 300), "counter-pitch");
        var results = MonitorEngine.Evaluate(new[] { monitor }, trace);

        Assert.Contains(results, r => r.RuleId == "CHOREO" && r.Severity == Severity.Pass);
        Assert.DoesNotContain(results, r => r.IsFailure);
    }
}
