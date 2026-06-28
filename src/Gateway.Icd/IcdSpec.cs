namespace Gateway.Icd;

/// <summary>Checksum algorithm protecting a frame.</summary>
public enum ChecksumAlgo { None, Crc16Ccitt }

/// <summary>
/// A complete Interface Control Document for one participant: the framing rules
/// (magic word, endianness, checksum) shared by every message, plus the message set.
/// One <see cref="IcdSpec"/> is the single source of truth that drives the codec,
/// the conformance validator, and (above them) the choreography monitors.
/// </summary>
public sealed class IcdSpec
{
    public required string Device { get; init; }
    public ushort Magic { get; init; } = 0xAA55;
    public Endianness Endianness { get; init; } = Endianness.Big;
    public ChecksumAlgo Checksum { get; init; } = ChecksumAlgo.Crc16Ccitt;

    /// <summary>Names the wire framing this spec uses; a <see cref="CodecRegistry"/> resolves it to a codec.</summary>
    public string Framing { get; init; } = "v1";

    public required IReadOnlyList<IcdMessage> Messages { get; init; }

    private readonly Dictionary<ushort, IcdMessage> _byCommand = new();

    public IcdMessage? FindByCommand(ushort command)
        => (_byCommand.Count == 0 ? Index() : _byCommand).GetValueOrDefault(command);

    private Dictionary<ushort, IcdMessage> Index()
    {
        foreach (var m in Messages) _byCommand[m.Command] = m;
        return _byCommand;
    }
}
