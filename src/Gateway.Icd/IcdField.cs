namespace Gateway.Icd;

/// <summary>
/// One field in an ICD message: its wire type plus the semantic contract the
/// developer's code must honor — valid numeric range and/or an enumeration of
/// allowed coded values. This is the transcription target for an ICD field-table row.
/// </summary>
public sealed record IcdField(
    string Name,
    IFieldType Type,
    string Unit = "",
    double? Min = null,
    double? Max = null,
    IReadOnlyDictionary<long, string>? Enum = null,
    string? Widget = null);   // optional GUI editor hint (e.g. "slider", "dial"); null = derive from type
