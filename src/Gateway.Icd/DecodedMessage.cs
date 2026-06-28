namespace Gateway.Icd;

/// <summary>
/// A frame decoded against an <see cref="IcdSpec"/>: the matched message definition,
/// the correlation sequence number, and the named field values (numeric, carried as
/// <see cref="double"/>). Field values are read by name by validators and monitors.
/// </summary>
public sealed class DecodedMessage(IcdMessage definition, ushort sequence, IReadOnlyDictionary<string, double> fields)
{
    public IcdMessage Definition { get; } = definition;
    public string Name => Definition.Name;
    public ushort Sequence { get; } = sequence;
    public IReadOnlyDictionary<string, double> Fields { get; } = fields;

    public double this[string field] => Fields[field];
}
