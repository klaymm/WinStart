using System.Text.Json;
using System.Text.Json.Serialization;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;

namespace WinStart.Core.Services;

public sealed class JournalService : IJournalService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly List<JournalEntry> _entries = [];

    public JournalService(IPathProvider paths)
    {
        HistoryFile = Path.Combine(paths.UserData, "history.json");
    }

    public string HistoryFile { get; }

    public IReadOnlyList<JournalEntry> Entries
    {
        get { lock (_entries) return _entries.ToArray(); }
    }

    public event EventHandler? Changed;

    public async Task LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (!File.Exists(HistoryFile)) return;

            await using var stream = File.OpenRead(HistoryFile);
            var loaded = await JsonSerializer
                .DeserializeAsync<List<JournalEntry>>(stream, JsonOptions).ConfigureAwait(false);

            lock (_entries)
            {
                _entries.Clear();
                if (loaded is not null) _entries.AddRange(loaded);
            }
        }
        catch
        {
            TryBackupCorruptFile();
        }
        finally { _gate.Release(); }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void TryBackupCorruptFile()
    {
        try
        {
            if (File.Exists(HistoryFile))
                File.Move(HistoryFile, HistoryFile + ".corrupt", true);
        }
        catch { }
    }

    public async Task AppendAsync(JournalEntry entry)
    {
        lock (_entries) _entries.Add(entry);
        await SaveAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task UpdateAsync(JournalEntry entry)
    {
        lock (_entries)
        {
            var idx = _entries.FindIndex(e => e.Id == entry.Id);
            if (idx >= 0) _entries[idx] = entry;
        }
        await SaveAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public async Task ClearAsync()
    {
        lock (_entries) _entries.Clear();
        await SaveAsync().ConfigureAwait(false);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private async Task SaveAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            JournalEntry[] snapshot;
            lock (_entries) snapshot = _entries.ToArray();

            Directory.CreateDirectory(Path.GetDirectoryName(HistoryFile)!);

            var temp = HistoryFile + ".tmp";
            await using (var stream = File.Create(temp))
                await JsonSerializer.SerializeAsync(stream, snapshot, JsonOptions).ConfigureAwait(false);

            File.Move(temp, HistoryFile, true);
        }
        catch { }
        finally { _gate.Release(); }
    }

    public IReadOnlyList<JournalEntry> FindActiveApplies(string tweakId)
    {
        lock (_entries)
        {
            return _entries
                .Where(e => e.TweakId == tweakId
                            && e.Direction == TweakDirection.Apply
                            && e.Status == TweakStatus.Success
                            && !e.Reverted)
                .OrderByDescending(e => e.Timestamp)
                .ToList();
        }
    }
}
