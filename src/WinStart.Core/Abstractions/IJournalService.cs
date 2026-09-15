using WinStart.Core.Models;

namespace WinStart.Core.Abstractions;

public interface IJournalService
{
    IReadOnlyList<JournalEntry> Entries { get; }
    event EventHandler? Changed;

    Task LoadAsync();
    Task AppendAsync(JournalEntry entry);
    Task UpdateAsync(JournalEntry entry);
    Task ClearAsync();
    string HistoryFile { get; }

    JournalEntry? FindLastApply(string tweakId);
}
