namespace Gateway.Icd;

/// <summary>Thrown when an ICD or topology file cannot be parsed into the domain model.</summary>
public sealed class IcdLoadException(string message) : Exception(message);

/// <summary>
/// Loads <see cref="IcdSpec"/>, <see cref="SystemTopology"/> and <see cref="ScenarioSpec"/> from
/// files. ICD parsing is delegated to a pluggable <see cref="IcdReaderRegistry"/> (so new source
/// formats plug in), and field types to a <see cref="FieldTypeRegistry"/> (so new wire types plug
/// in). Topology and scenario remain JSON. Errors carry the file and offending element.
/// </summary>
public static class IcdLoader
{
    /// <summary>The readers used by <see cref="LoadSpec"/>. Register your own format reader here.</summary>
    public static IcdReaderRegistry DefaultReaders { get; } = IcdReaderRegistry.CreateDefault();

    /// <summary>Built-in field types, used when a caller does not supply a custom registry.</summary>
    private static readonly FieldTypeRegistry DefaultTypes = FieldTypeRegistry.CreateDefault();

    /// <summary>Loads an ICD by dispatching to the registered reader for its format, resolving field
    /// types through <paramref name="types"/> (pass a registry with your own types for custom types).</summary>
    public static IcdSpec LoadSpec(string path, FieldTypeRegistry? types = null)
    {
        var reader = DefaultReaders.For(path)
            ?? throw new IcdLoadException($"No ICD reader registered for '{Path.GetFileName(path)}'.");
        return reader.Read(path, types ?? DefaultTypes);
    }

    public static SystemTopology LoadTopology(string path)
    {
        var dto = IcdParse.Deserialize<TopologyDto>(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(dto.Dut)) throw new IcdLoadException($"{name}: 'dut' is required.");
        if (dto.Participants is not { Count: > 0 }) throw new IcdLoadException($"{name}: 'participants' is required.");

        var dir = Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".";
        var participants = dto.Participants!.Select(p =>
        {
            if (string.IsNullOrWhiteSpace(p.Id)) throw new IcdLoadException($"{name}: a participant is missing 'id'.");
            if (string.IsNullOrWhiteSpace(p.Icd)) throw new IcdLoadException($"{name}: participant '{p.Id}' is missing 'icd'.");
            return new Participant(p.Id!, Path.Combine(dir, p.Icd!), p.Ip, p.Mac);
        }).ToList();

        var interfaces = (dto.Interfaces ?? new()).Select(i =>
            new Interface(i.From ?? "", i.To ?? "", i.Messages ?? new())).ToList();

        var topo = new SystemTopology { Dut = dto.Dut!, Participants = participants, Interfaces = interfaces };
        if (!string.IsNullOrWhiteSpace(dto.DutIp)) topo.DutIp = dto.DutIp!.Trim();
        if (!string.IsNullOrWhiteSpace(dto.DutMac)) topo.DutMac = dto.DutMac!.Trim();
        return topo;
    }

    public static ScenarioSpec LoadScenario(string path)
    {
        var dto = IcdParse.Deserialize<ScenarioDto>(path);
        var name = Path.GetFileName(path);
        if (dto.Events is not { Count: > 0 }) throw new IcdLoadException($"{name}: 'events' is required.");

        var events = dto.Events!.Select(e =>
        {
            if (string.IsNullOrWhiteSpace(e.Message)) throw new IcdLoadException($"{name}: an event is missing 'message'.");
            return new ScenarioEvent(e.T, e.Message!, e.Fields ?? new());
        }).ToList();

        var expectations = (dto.Expectations ?? new()).Select(x =>
        {
            if (string.IsNullOrWhiteSpace(x.On) || string.IsNullOrWhiteSpace(x.Expect) || string.IsNullOrWhiteSpace(x.Where))
                throw new IcdLoadException($"{name}: an expectation needs 'on', 'expect' and 'where'.");
            return new ScenarioExpectation(x.On!, x.Expect!, x.Within, x.Where!);
        }).ToList();

        return new ScenarioSpec { Name = dto.Scenario ?? Path.GetFileNameWithoutExtension(path), Events = events, Expectations = expectations };
    }

    // ---- JSON DTOs (topology + scenario) ----

    private sealed class TopologyDto
    {
        public string? Dut { get; set; }
        public string? DutIp { get; set; }
        public string? DutMac { get; set; }
        public List<PartDto>? Participants { get; set; }
        public List<IfaceDto>? Interfaces { get; set; }
    }

    private sealed class PartDto
    {
        public string? Id { get; set; }
        public string? Icd { get; set; }
        public string? Ip { get; set; }
        public string? Mac { get; set; }
    }

    private sealed class IfaceDto
    {
        public string? From { get; set; }
        public string? To { get; set; }
        public List<string>? Messages { get; set; }
    }

    private sealed class ScenarioDto
    {
        public string? Scenario { get; set; }
        public List<EventDto>? Events { get; set; }
        public List<ExpectDto>? Expectations { get; set; }
    }

    private sealed class EventDto
    {
        public long T { get; set; }
        public string? Message { get; set; }
        public Dictionary<string, double>? Fields { get; set; }
    }

    private sealed class ExpectDto
    {
        public string? On { get; set; }
        public string? Expect { get; set; }
        public long Within { get; set; }
        public string? Where { get; set; }
    }
}
