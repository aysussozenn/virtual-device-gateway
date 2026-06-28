namespace Gateway.Icd;

/// <summary>Built-in reader for the JSON ICD format. The canonical example of an
/// <see cref="IIcdFormatReader"/>; a new format mirrors its shape against a different parser.</summary>
public sealed class JsonIcdReader : IIcdFormatReader
{
    public bool CanRead(string path) => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

    public IcdSpec Read(string path, FieldTypeRegistry types)
    {
        var dto = IcdParse.Deserialize<IcdDto>(path);
        var name = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(dto.Device)) throw new IcdLoadException($"{name}: 'device' is required.");
        if (dto.Messages is not { Count: > 0 }) throw new IcdLoadException($"{name}: at least one message is required.");

        return new IcdSpec
        {
            Device = dto.Device!,
            Magic = IcdParse.ParseU16(dto.Magic, name, "magic", 0xAA55),
            Endianness = IcdParse.ParseEnumOr(dto.Endianness, name, "endianness", Endianness.Big),
            Checksum = IcdParse.ParseChecksum(dto.Checksum, name),
            Framing = string.IsNullOrWhiteSpace(dto.Framing) ? "v1" : dto.Framing!.Trim(),
            Messages = dto.Messages!.Select(m => MapMessage(m, name, types)).ToList()
        };
    }

    private static IcdMessage MapMessage(MsgDto m, string file, FieldTypeRegistry types)
    {
        if (string.IsNullOrWhiteSpace(m.Name)) throw new IcdLoadException($"{file}: a message is missing 'name'.");
        if (m.Fields is not { Count: > 0 }) throw new IcdLoadException($"{file}: message '{m.Name}' has no fields.");

        var fields = m.Fields!.Select(f => MapField(f, file, m.Name!, types)).ToList();
        var msg = new IcdMessage(m.Name!, IcdParse.ParseU16(m.Command, file, $"{m.Name}.command", 0),
            IcdParse.ParseEnumOr(m.Direction, file, $"{m.Name}.direction", Direction.Inbound), fields);

        if (m.Port is { } p)
        {
            msg = msg with
            {
                Port = IcdParse.ParseEnumOr(p.Kind, file, $"{m.Name}.port.kind", PortKind.None),
                PortDir = IcdParse.ParseEnumOr(p.Dir, file, $"{m.Name}.port.dir", Direction.Inbound),
                RefreshMs = p.RefreshMs,
                Depth = p.Depth,
                MaxSize = p.MaxSize
            };
        }
        return msg;
    }

    private static IcdField MapField(FieldDto f, string file, string msg, FieldTypeRegistry types)
    {
        if (string.IsNullOrWhiteSpace(f.Name)) throw new IcdLoadException($"{file}: a field in '{msg}' is missing 'name'.");
        if (string.IsNullOrWhiteSpace(f.Type)) throw new IcdLoadException($"{file}: {msg}.{f.Name} is missing 'type'.");
        var type = types.Find(f.Type!)
            ?? throw new IcdLoadException($"{file}: {msg}.{f.Name}.type — unknown field type '{f.Type}'. Known: {string.Join(", ", types.Names)}.");
        IReadOnlyDictionary<long, string>? @enum = null;
        if (f.Enum is { Count: > 0 })
        {
            @enum = f.Enum.ToDictionary(
                kv => long.TryParse(kv.Key, out var k) ? k
                    : throw new IcdLoadException($"{file}: {msg}.{f.Name} enum key '{kv.Key}' is not an integer."),
                kv => kv.Value);
        }
        return new IcdField(f.Name!, type, f.Unit ?? "", f.Min, f.Max, @enum, f.Widget);
    }

    // ---- JSON DTOs (ICD spec shape) ----

    private sealed class IcdDto
    {
        public string? Device { get; set; }
        public string? Magic { get; set; }
        public string? Endianness { get; set; }
        public string? Checksum { get; set; }
        public string? Framing { get; set; }
        public List<MsgDto>? Messages { get; set; }
    }

    private sealed class MsgDto
    {
        public string? Name { get; set; }
        public string? Command { get; set; }
        public string? Direction { get; set; }
        public PortDto? Port { get; set; }
        public List<FieldDto>? Fields { get; set; }
    }

    private sealed class PortDto
    {
        public string? Kind { get; set; }
        public string? Dir { get; set; }
        public int? RefreshMs { get; set; }
        public int? Depth { get; set; }
        public int? MaxSize { get; set; }
    }

    private sealed class FieldDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Unit { get; set; }
        public double? Min { get; set; }
        public double? Max { get; set; }
        public Dictionary<string, string>? Enum { get; set; }
        public string? Widget { get; set; }
    }
}
