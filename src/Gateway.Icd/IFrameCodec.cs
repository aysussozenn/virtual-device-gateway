namespace Gateway.Icd;

/// <summary>
/// Encodes named field values into a wire frame and decodes them back. This is the framing
/// extension point: the built-in <see cref="IcdCodec"/> implements the
/// <c>[magic][command][seq][fields…][crc16]</c> layout, but a third party can implement a
/// completely different header/checksum/bit-packing scheme and register it under a framing name
/// in a <see cref="CodecRegistry"/>. Field bytes can still be shared via <see cref="IFieldType"/>.
/// </summary>
public interface IFrameCodec
{
    byte[] Encode(IcdMessage message, ushort sequence, IReadOnlyDictionary<string, double> values);

    DecodeResult TryDecode(ReadOnlySpan<byte> frame, long timestampMs = 0);
}

/// <summary>
/// Maps a spec's declared <see cref="IcdSpec.Framing"/> to the codec that implements it. Start
/// from <see cref="Default"/> (or <see cref="CreateDefault"/>) and <see cref="Register"/> your
/// own framing — the runtime builds a spec's codec through <see cref="Build"/>, so a custom
/// framing flows everywhere without core changes.
/// </summary>
public sealed class CodecRegistry
{
    private readonly Dictionary<string, Func<IcdSpec, IFrameCodec>> _factories = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string framing, Func<IcdSpec, IFrameCodec> factory) => _factories[framing] = factory;

    public IFrameCodec Build(IcdSpec spec)
    {
        var framing = string.IsNullOrWhiteSpace(spec.Framing) ? "v1" : spec.Framing.Trim();
        return _factories.TryGetValue(framing, out var factory)
            ? factory(spec)
            : throw new InvalidOperationException(
                $"No codec registered for framing '{framing}'. Known: {string.Join(", ", _factories.Keys)}.");
    }

    public static CodecRegistry Default { get; } = CreateDefault();

    public static CodecRegistry CreateDefault()
    {
        var registry = new CodecRegistry();
        registry.Register("v1", spec => new IcdCodec(spec));
        return registry;
    }
}
