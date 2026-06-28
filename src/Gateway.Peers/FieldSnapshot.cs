namespace Gateway.Peers;

/// <summary>
/// An immutable set of field values for one message. The UI builds a complete snapshot on
/// every edit and swaps it atomically into a streaming sender, so the sender thread never
/// observes a half-updated multi-field struct (no torn read of a/b/c).
/// </summary>
public sealed class FieldSnapshot
{
    public static readonly FieldSnapshot Empty = new(new Dictionary<string, double>());

    public FieldSnapshot(IReadOnlyDictionary<string, double> values) => Values = values;

    public IReadOnlyDictionary<string, double> Values { get; }
}
