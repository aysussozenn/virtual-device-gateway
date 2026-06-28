namespace Gateway.Icd;

/// <summary>CRC-16/CCITT-FALSE (poly 0x1021, init 0xFFFF, no reflection) — a common ICD framing checksum.</summary>
public static class Crc16
{
    public static ushort Ccitt(ReadOnlySpan<byte> data)
    {
        ushort crc = 0xFFFF;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var i = 0; i < 8; i++)
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x1021 : crc << 1);
        }
        return crc;
    }
}
