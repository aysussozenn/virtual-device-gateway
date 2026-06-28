namespace Gateway.Icd;

/// <summary>One node in the system: a participant with its ICD and (for raw-eth) addressing.</summary>
public sealed record Participant(string Id, string IcdPath, string? Ip = null, string? Mac = null);

/// <summary>A declared interface edge: <see cref="From"/> sends <see cref="Messages"/> to <see cref="To"/>.</summary>
public sealed record Interface(string From, string To, IReadOnlyList<string> Messages);

/// <summary>
/// The "who talks to whom" graph an ICD already documents: participants plus the
/// directed interface edges between them. The DUT is one node; the participants we must
/// emulate are exactly its neighbours. This drives which peers to stand up and the
/// coverage / unexpected-partner conformance checks.
/// </summary>
public sealed class SystemTopology
{
    public required string Dut { get; init; }
    public required IReadOnlyList<Participant> Participants { get; init; }
    public required IReadOnlyList<Interface> Interfaces { get; init; }

    /// <summary>DUT endpoint addressing, so emulated peers know where to send. Optional in the
    /// file; defaults to the in-memory demo address when absent.</summary>
    public string DutIp { get; set; } = "192.168.50.1";
    public string DutMac { get; set; } = "02-00-00-00-00-01";

    /// <summary>Participants the DUT has an interface with (in either direction) — the set we emulate.</summary>
    public IEnumerable<Participant> Neighbours()
    {
        var ids = Interfaces
            .Where(i => i.From == Dut || i.To == Dut)
            .Select(i => i.From == Dut ? i.To : i.From)
            .ToHashSet(StringComparer.Ordinal);
        return Participants.Where(p => ids.Contains(p.Id));
    }

    public Participant? Find(string id) => Participants.FirstOrDefault(p => p.Id == id);
}
