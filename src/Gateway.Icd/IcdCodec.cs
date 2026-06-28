using System.Buffers.Binary;

namespace Gateway.Icd;

/// <summary>Outcome of decoding a frame: the structured message (null if it could not be parsed) plus structural verdicts.</summary>
public sealed record DecodeResult(DecodedMessage? Message, IReadOnlyList<ConformanceResult> Structural);

/// <summary>
/// Spec-driven codec: turns named field values into wire bytes and back, using the
/// framing (magic / endianness / checksum) and field layout declared by an
/// <see cref="IcdSpec"/>. This is the seam the (pending) protocol spec plugs into — the
/// ICD *is* that spec. Frame layout: [magic][command][seq][fields...][crc16].
/// </summary>
public sealed class IcdCodec(IcdSpec spec) : IFrameCodec
{
    private const int HeaderSize = 6;   // magic(2) + command(2) + seq(2)
    private int TrailerSize => spec.Checksum == ChecksumAlgo.None ? 0 : 2;

    public byte[] Encode(IcdMessage message, ushort sequence, IReadOnlyDictionary<string, double> values)
    {
        var body = HeaderSize + message.Fields.Sum(f => f.Type.Size);
        var buf = new byte[body + TrailerSize];

        WriteU16(buf, 0, spec.Magic);
        WriteU16(buf, 2, message.Command);
        WriteU16(buf, 4, sequence);

        var off = HeaderSize;
        foreach (var f in message.Fields)
        {
            f.Type.Write(buf.AsSpan(off, f.Type.Size), values.TryGetValue(f.Name, out var v) ? v : 0, spec.Endianness);
            off += f.Type.Size;
        }
        if (TrailerSize == 2)
            WriteU16(buf, off, Crc16.Ccitt(buf.AsSpan(0, off)));
        return buf;
    }

    public DecodeResult TryDecode(ReadOnlySpan<byte> frame, long timestampMs = 0)
    {
        if (frame.Length < HeaderSize + TrailerSize)
            return Fail(ConformanceResult.Violation("ICD-LEN", $"Frame too short ({frame.Length} bytes).", t: timestampMs));

        var magic = ReadU16(frame, 0);
        if (magic != spec.Magic)
            return Fail(ConformanceResult.Violation("ICD-MAGIC", "Bad framing magic.", t: timestampMs,
                expected: $"0x{spec.Magic:X4}", actual: $"0x{magic:X4}"));

        var command = ReadU16(frame, 2);
        var seq = ReadU16(frame, 4);
        var def = spec.FindByCommand(command);
        if (def is null)
            return Fail(ConformanceResult.Violation("ICD-CMD", $"Unknown command 0x{command:X4}.", seq: seq, t: timestampMs));

        var expectedLen = HeaderSize + def.Fields.Sum(f => f.Type.Size) + TrailerSize;
        if (frame.Length != expectedLen)
            return Fail(ConformanceResult.Violation("ICD-LEN", $"Length mismatch for {def.Name}.",
                def.Name, seq, timestampMs, expected: $"{expectedLen} B", actual: $"{frame.Length} B"));

        if (TrailerSize == 2)
        {
            var got = ReadU16(frame, frame.Length - 2);
            var calc = Crc16.Ccitt(frame[..(frame.Length - 2)]);
            if (got != calc)
                return Fail(ConformanceResult.Violation("ICD-CRC", $"Checksum mismatch for {def.Name}.",
                    def.Name, seq, timestampMs, expected: $"0x{calc:X4}", actual: $"0x{got:X4}"));
        }

        var fields = new Dictionary<string, double>();
        var off = HeaderSize;
        foreach (var f in def.Fields)
        {
            fields[f.Name] = f.Type.Read(frame.Slice(off, f.Type.Size), spec.Endianness);
            off += f.Type.Size;
        }
        return new DecodeResult(new DecodedMessage(def, seq, fields), Array.Empty<ConformanceResult>());
    }

    private static DecodeResult Fail(ConformanceResult r) => new(null, new[] { r });

    // ---- numeric IO honoring the spec's endianness ----

    private void WriteU16(byte[] b, int off, ushort v)
    {
        if (spec.Endianness == Endianness.Big) BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(off), v);
        else BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(off), v);
    }

    private ushort ReadU16(ReadOnlySpan<byte> b, int off)
        => spec.Endianness == Endianness.Big
            ? BinaryPrimitives.ReadUInt16BigEndian(b[off..])
            : BinaryPrimitives.ReadUInt16LittleEndian(b[off..]);
}
