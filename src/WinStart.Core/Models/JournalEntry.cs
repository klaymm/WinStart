namespace WinStart.Core.Models;

public sealed class JournalEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.Now;

    public string TweakId { get; set; } = "";
    public string TitleKey { get; set; } = "";
    public TweakCategory Category { get; set; }
    public TweakDirection Direction { get; set; }
    public TweakStatus Status { get; set; }

    public bool Reversible { get; set; }

    public bool Reverted { get; set; }

    public string? Option { get; set; }

    public bool Extended { get; set; }

    public string? Message { get; set; }
    public bool HasIssues { get; set; }
    public List<string> Log { get; set; } = [];

    public List<RegistryValueSnapshot> RegistrySnapshots { get; set; } = [];
    public List<RegistryKeyBackup> KeyBackups { get; set; } = [];
}
