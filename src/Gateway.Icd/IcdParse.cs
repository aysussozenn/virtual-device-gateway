using System.Globalization;
using System.Text.Json;

namespace Gateway.Icd;

/// <summary>Shared JSON + scalar parsing helpers used by the built-in readers (spec, topology, scenario).</summary>
internal static class IcdParse
{
    public static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static T Deserialize<T>(string path)
    {
        if (!File.Exists(path)) throw new IcdLoadException($"File not found: {path}");
        try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json) ?? throw new IcdLoadException($"{path}: empty."); }
        catch (JsonException ex) { throw new IcdLoadException($"{Path.GetFileName(path)}: invalid JSON — {ex.Message}"); }
    }

    public static ushort ParseU16(string? s, string file, string what, ushort fallback)
    {
        if (string.IsNullOrWhiteSpace(s)) return fallback;
        s = s.Trim();
        var ok = s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
            ? ushort.TryParse(s.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var v)
            : ushort.TryParse(s, out v);
        return ok ? v : throw new IcdLoadException($"{file}: '{what}' value '{s}' is not a 16-bit number.");
    }

    public static ChecksumAlgo ParseChecksum(string? s, string file) => (s ?? "crc16-ccitt").Trim().ToLowerInvariant() switch
    {
        "" or "crc16-ccitt" or "crc16" => ChecksumAlgo.Crc16Ccitt,
        "none" => ChecksumAlgo.None,
        _ => throw new IcdLoadException($"{file}: unknown checksum '{s}'.")
    };

    public static TEnum ParseEnumOr<TEnum>(string? s, string file, string what, TEnum fallback) where TEnum : struct, Enum
        => string.IsNullOrWhiteSpace(s) ? fallback : ParseEnumReq<TEnum>(s, file, what);

    public static TEnum ParseEnumReq<TEnum>(string? s, string file, string what) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(s)) throw new IcdLoadException($"{file}: '{what}' is required.");
        return Enum.TryParse<TEnum>(s.Replace("-", "").Trim(), ignoreCase: true, out var v)
            ? v
            : throw new IcdLoadException($"{file}: '{what}' value '{s}' is not a valid {typeof(TEnum).Name}.");
    }
}
