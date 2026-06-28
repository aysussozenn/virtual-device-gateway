namespace Gateway.Icd;

/// <summary>
/// Semantic conformance layer: given a frame already decoded against the ICD, checks
/// every field against its declared range and enumeration. Structural failures (magic,
/// command, length, CRC) are produced earlier by <see cref="IcdCodec.TryDecode"/>; this
/// validator covers "the bytes parsed, but do the *values* honor the contract?".
/// </summary>
public static class ConformanceValidator
{
    public static IReadOnlyList<ConformanceResult> Validate(DecodedMessage msg, long timestampMs = 0)
    {
        var results = new List<ConformanceResult>();
        foreach (var f in msg.Definition.Fields)
        {
            var value = msg[f.Name];

            if (f.Min is { } min && value < min || f.Max is { } max && value > max)
            {
                results.Add(ConformanceResult.Violation("ICD-RANGE",
                    $"{msg.Name}.{f.Name} out of range.", msg.Name, msg.Sequence, timestampMs,
                    expected: $"[{f.Min}, {f.Max}]{Unit(f)}", actual: $"{Trim(value)}{Unit(f)}"));
            }

            if (f.Enum is { } e && !e.ContainsKey((long)value))
            {
                results.Add(ConformanceResult.Violation("ICD-ENUM",
                    $"{msg.Name}.{f.Name} not an allowed value.", msg.Name, msg.Sequence, timestampMs,
                    expected: string.Join("/", e.Keys), actual: Trim(value).ToString()));
            }
        }

        if (results.Count == 0)
            results.Add(ConformanceResult.Pass("ICD-OK", $"{msg.Name} conforms.", msg.Name, timestampMs));

        return results;
    }

    private static string Unit(IcdField f) => string.IsNullOrEmpty(f.Unit) ? "" : " " + f.Unit;
    private static double Trim(double v) => Math.Round(v, 3);
}
