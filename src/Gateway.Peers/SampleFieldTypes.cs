using System.Buffers.Binary;
using System.Globalization;
using Gateway.Icd;

namespace Gateway.Peers;

/// <summary>
/// Sample third-party field type, written against the public <see cref="IFieldType"/> contract
/// only (no access to Gateway.Icd internals) — proof that new wire types plug in from the
/// outside. Q8.8 fixed-point: 16 bits on the wire, engineering value = raw / 256. The wire bytes
/// therefore differ from the displayed value, which both the codec and the generated GUI honor.
/// </summary>
public sealed class FixedPointFieldType : IFieldType
{
    public string Name => "q8_8";
    public int Size => 2;

    // Let the field's declared min/max bound the slider; the format is otherwise unbounded.
    public (double Min, double Max)? NaturalRange => null;

    public double Read(ReadOnlySpan<byte> src, Endianness endianness)
    {
        var raw = endianness == Endianness.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(src)
            : BinaryPrimitives.ReadUInt16LittleEndian(src);
        return raw / 256.0;
    }

    public void Write(Span<byte> dst, double value, Endianness endianness)
    {
        var raw = (ushort)Math.Clamp(Math.Round(value * 256.0), 0, ushort.MaxValue);
        if (endianness == Endianness.Big) BinaryPrimitives.WriteUInt16BigEndian(dst, raw);
        else BinaryPrimitives.WriteUInt16LittleEndian(dst, raw);
    }

    public string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>
/// The field-type registry the app loads ICDs with: the built-ins plus the sample custom types
/// this assembly ships. A real plugin would register its types here (or via DI) instead of
/// touching core.
/// </summary>
public static class IcdTypeCatalog
{
    public static FieldTypeRegistry Default { get; } = Build();

    private static FieldTypeRegistry Build()
    {
        var registry = FieldTypeRegistry.CreateDefault();
        registry.Register(new FixedPointFieldType());
        return registry;
    }
}
