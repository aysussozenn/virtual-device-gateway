using System.Buffers.Binary;
using System.Globalization;

namespace Gateway.Icd;

/// <summary>Byte ordering for multi-byte fields and framing words.</summary>
public enum Endianness { Big, Little }

/// <summary>
/// A wire field type: how many bytes it occupies on the wire and how a numeric value is read
/// from / written to those bytes. This is the primary extension point — a third party adds a
/// new type (bit-field, scaled/engineering, string-as-code…) by implementing this interface
/// and registering it in a <see cref="FieldTypeRegistry"/>. The codec and the generated GUI
/// both consume the type through this contract, so neither has to change.
/// </summary>
public interface IFieldType
{
    /// <summary>Canonical name as written in an ICD file (e.g. "u16"). Case-insensitive on lookup.</summary>
    string Name { get; }

    /// <summary>Number of bytes this field occupies on the wire.</summary>
    int Size { get; }

    /// <summary>Reads the value from exactly <see cref="Size"/> bytes.</summary>
    double Read(ReadOnlySpan<byte> src, Endianness endianness);

    /// <summary>Writes the value into exactly <see cref="Size"/> bytes.</summary>
    void Write(Span<byte> dst, double value, Endianness endianness);

    /// <summary>Bounded natural range for a slider editor; null means unbounded → free-text entry.</summary>
    (double Min, double Max)? NaturalRange { get; }

    /// <summary>Base numeric formatting for labels; unit/enum decoration is layered on by the caller.</summary>
    string Format(double value);
}

/// <summary>Signed/unsigned integers of 1, 2 or 4 bytes.</summary>
public sealed class IntegerFieldType(string name, int size, bool signed, double min, double max) : IFieldType
{
    public string Name { get; } = name;
    public int Size { get; } = size;
    public (double Min, double Max)? NaturalRange { get; } = (min, max);

    public double Read(ReadOnlySpan<byte> src, Endianness endianness)
    {
        var big = endianness == Endianness.Big;
        return Size switch
        {
            1 => signed ? (sbyte)src[0] : src[0],
            2 => signed ? (short)ByteIo.ReadU16(src, big) : ByteIo.ReadU16(src, big),
            4 => signed ? (int)ByteIo.ReadU32(src, big) : ByteIo.ReadU32(src, big),
            _ => 0
        };
    }

    public void Write(Span<byte> dst, double value, Endianness endianness)
    {
        var big = endianness == Endianness.Big;
        var l = (long)value;
        switch (Size)
        {
            case 1: dst[0] = signed ? (byte)(sbyte)l : (byte)l; break;
            case 2: ByteIo.WriteU16(dst, signed ? (ushort)(short)l : (ushort)l, big); break;
            case 4: ByteIo.WriteU32(dst, signed ? (uint)(int)l : (uint)l, big); break;
        }
    }

    public string Format(double value) => ((long)value).ToString(CultureInfo.InvariantCulture);
}

/// <summary>IEEE-754 single-precision float (4 bytes).</summary>
public sealed class Float32FieldType : IFieldType
{
    public string Name => "f32";
    public int Size => 4;
    public (double Min, double Max)? NaturalRange => null;

    public double Read(ReadOnlySpan<byte> src, Endianness endianness)
        => BitConverter.UInt32BitsToSingle(ByteIo.ReadU32(src, endianness == Endianness.Big));

    public void Write(Span<byte> dst, double value, Endianness endianness)
        => ByteIo.WriteU32(dst, BitConverter.SingleToUInt32Bits((float)value), endianness == Endianness.Big);

    public string Format(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>The built-in field types, also usable directly when building an <see cref="IcdSpec"/> in code.</summary>
public static class FieldTypes
{
    public static readonly IFieldType U8 = new IntegerFieldType("u8", 1, false, 0, 255);
    public static readonly IFieldType I8 = new IntegerFieldType("i8", 1, true, -128, 127);
    public static readonly IFieldType U16 = new IntegerFieldType("u16", 2, false, 0, 65535);
    public static readonly IFieldType I16 = new IntegerFieldType("i16", 2, true, -32768, 32767);
    public static readonly IFieldType U32 = new IntegerFieldType("u32", 4, false, 0, 4294967295);
    public static readonly IFieldType I32 = new IntegerFieldType("i32", 4, true, -2147483648, 2147483647);
    public static readonly IFieldType F32 = new Float32FieldType();

    public static IReadOnlyList<IFieldType> All { get; } = new[] { U8, I8, U16, I16, U32, I32, F32 };
}

/// <summary>
/// Resolves an ICD type name to its <see cref="IFieldType"/>. Start from
/// <see cref="CreateDefault"/> and <see cref="Register"/> your own types before loading an ICD
/// that uses them — no core code changes required.
/// </summary>
public sealed class FieldTypeRegistry
{
    private readonly Dictionary<string, IFieldType> _byName = new(StringComparer.OrdinalIgnoreCase);

    public void Register(IFieldType type) => _byName[type.Name] = type;

    public IFieldType? Find(string name) => _byName.TryGetValue(name.Trim(), out var t) ? t : null;

    public IEnumerable<string> Names => _byName.Keys;

    public static FieldTypeRegistry CreateDefault()
    {
        var registry = new FieldTypeRegistry();
        foreach (var t in FieldTypes.All) registry.Register(t);
        return registry;
    }
}

internal static class ByteIo
{
    public static ushort ReadU16(ReadOnlySpan<byte> s, bool big)
        => big ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);

    public static uint ReadU32(ReadOnlySpan<byte> s, bool big)
        => big ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);

    public static void WriteU16(Span<byte> d, ushort v, bool big)
    { if (big) BinaryPrimitives.WriteUInt16BigEndian(d, v); else BinaryPrimitives.WriteUInt16LittleEndian(d, v); }

    public static void WriteU32(Span<byte> d, uint v, bool big)
    { if (big) BinaryPrimitives.WriteUInt32BigEndian(d, v); else BinaryPrimitives.WriteUInt32LittleEndian(d, v); }
}
