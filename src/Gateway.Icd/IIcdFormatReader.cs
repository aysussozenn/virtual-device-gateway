namespace Gateway.Icd;

/// <summary>
/// Parses an ICD source file into the domain <see cref="IcdSpec"/>. This is the extension point
/// for new ICD source formats: implement it (XML, YAML, a spreadsheet export, a vendor format),
/// register it in an <see cref="IcdReaderRegistry"/>, and the loader picks it up by file — no
/// core change. Field types are resolved through the supplied <see cref="FieldTypeRegistry"/>.
/// </summary>
public interface IIcdFormatReader
{
    /// <summary>True if this reader recognises the file (typically by extension or a content sniff).</summary>
    bool CanRead(string path);

    IcdSpec Read(string path, FieldTypeRegistry types);
}

/// <summary>
/// Chooses an <see cref="IIcdFormatReader"/> for a given file. Start from
/// <see cref="CreateDefault"/> and <see cref="Register"/> your own reader; the most recently
/// registered reader that accepts a file wins.
/// </summary>
public sealed class IcdReaderRegistry
{
    private readonly List<IIcdFormatReader> _readers = new();

    public void Register(IIcdFormatReader reader) => _readers.Insert(0, reader);

    public IIcdFormatReader? For(string path) => _readers.FirstOrDefault(r => r.CanRead(path));

    public static IcdReaderRegistry CreateDefault()
    {
        var registry = new IcdReaderRegistry();
        registry.Register(new JsonIcdReader());
        return registry;
    }
}
