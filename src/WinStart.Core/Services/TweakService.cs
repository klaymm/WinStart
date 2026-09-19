using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Tweaks;

namespace WinStart.Core.Services;

public interface ITweakService
{
    Task<TweakState> DetectAsync(TweakDefinition def, CancellationToken ct);

    Task<JournalEntry> ApplyAsync(TweakDefinition def, string? option, bool extended,
        Action<string?, double?>? progress, CancellationToken ct);

    Task<JournalEntry> RevertAsync(TweakDefinition def,
        Action<string?, double?>? progress, CancellationToken ct);

    Task<bool> RevertEntryAsync(JournalEntry entry, CancellationToken ct);

    Task RunBatchAsync(Func<Task> body, Action<string?>? status);

    void FlushPendingExplorerRestart();
}

public sealed class TweakService : ITweakService
{
    private readonly IRegistryService _registry;
    private readonly IProcessRunner _process;
    private readonly IDownloadService _download;
    private readonly IArchiveService _archive;
    private readonly IPathProvider _paths;
    private readonly ISystemInfoService _system;
    private readonly IJournalService _journal;
    private readonly ILocalizationService _loc;
    private readonly TweakRegistry _tweaks;
    private int _batchDepth;
    private int _explorerPending;

    public TweakService(
        IRegistryService registry,
        IProcessRunner process,
        IDownloadService download,
        IArchiveService archive,
        IPathProvider paths,
        ISystemInfoService system,
        IJournalService journal,
        ILocalizationService loc,
        TweakRegistry tweaks)
    {
        _registry = registry;
        _process = process;
        _download = download;
        _archive = archive;
        _paths = paths;
        _system = system;
        _journal = journal;
        _loc = loc;
        _tweaks = tweaks;
    }

    private TweakRunContext CreateContext(TweakDefinition def, string? option, bool extended,
        Action<string?, double?>? progress)
        => new(_registry, _process, _download, _archive, _paths, _loc, def.Id, option, extended, progress);

    // ---------------------------------------------------------------- Detect

    public Task<TweakState> DetectAsync(TweakDefinition def, CancellationToken ct) => Task.Run(() =>
    {
        if (def.MinBuild > 0 && _system.Build < def.MinBuild) return TweakState.Unavailable;
        if (def.Kind == TweakKind.Action) return TweakState.Unknown;

        if (def.CustomDetect is not null)
        {
            try { return def.CustomDetect(CreateContext(def, null, false, null)); }
            catch { return TweakState.Unknown; }
        }

        var checks = def.RegistryActions.Where(a => !a.SkipInDetect).ToList();
        if (checks.Count == 0) return TweakState.Unknown;

        return checks.All(IsApplied) ? TweakState.Applied : TweakState.NotApplied;
    }, ct);

    private bool IsApplied(RegistryAction action) => action.Op switch
    {
        RegistryOp.SetValue => ValueMatches(action),
        RegistryOp.DeleteValue => !_registry.ValueExists(action.Path, action.Name),
        RegistryOp.DeleteKey => !_registry.KeyExists(action.Path),
        _ => false
    };

    private bool ValueMatches(RegistryAction action)
    {
        var current = _registry.GetValue(action.Path, action.Name);
        if (current is null || action.Value is null) return false;

        return (current, action.Value) switch
        {
            (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
            (string[] a, string[] b) => a.SequenceEqual(b, StringComparer.OrdinalIgnoreCase),
            (int a, int b) => a == b,
            (long a, long b) => a == b,
            (int a, long b) => a == b,
            (long a, int b) => a == b,
            _ => string.Equals(current.ToString(), action.Value.ToString(), StringComparison.OrdinalIgnoreCase)
        };
    }

    // ----------------------------------------------------------------- Apply

    public async Task<JournalEntry> ApplyAsync(TweakDefinition def, string? option, bool extended,
        Action<string?, double?>? progress, CancellationToken ct)
    {
        var ctx = CreateContext(def, option, extended, progress);
        var entry = NewEntry(def, TweakDirection.Apply, option, extended);

        try
        {
            if (def.MinBuild > 0 && _system.Build < def.MinBuild)
                return await SkipAsync(entry, ctx, _loc.Format("log.minBuild", def.MinBuild)).ConfigureAwait(false);

            if (def.RequiresNetwork && !await ctx.Download.IsOnlineAsync(ct).ConfigureAwait(false))
                return await SkipAsync(entry, ctx, _loc["log.offline"]).ConfigureAwait(false);

            if (!def.ManualRevertOnly)
            {
                foreach (var action in def.RegistryActions)
                {
                    ct.ThrowIfCancellationRequested();
                    await SnapshotAsync(ctx, action, ct).ConfigureAwait(false);
                }
            }

            foreach (var action in def.RegistryActions)
            {
                ct.ThrowIfCancellationRequested();
                Execute(ctx, action);
            }

            if (def.CustomApply is not null)
                await def.CustomApply(ctx, ct).ConfigureAwait(false);

            await FinishAsync(def, ctx, entry, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await SkipAsync(entry, ctx, _loc["log.cancelled"]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await FailAsync(entry, ctx, ex).ConfigureAwait(false);
        }

        return entry;
    }

    // ---------------------------------------------------------------- Revert

    public Task<JournalEntry> RevertAsync(TweakDefinition def,
        Action<string?, double?>? progress, CancellationToken ct)
        => RevertCoreAsync(def, _journal.FindActiveApplies(def.Id), progress, ct);

    public async Task<bool> RevertEntryAsync(JournalEntry entry, CancellationToken ct)
    {
        if (entry.Reverted || !entry.Reversible) return false;

        var def = _tweaks.ById(entry.TweakId)
                  ?? new TweakDefinition { Id = entry.TweakId, Category = entry.Category };

        List<JournalEntry> applied = def.Kind == TweakKind.Action
            ? [entry]
            : _journal.FindActiveApplies(entry.TweakId)
                .Where(e => e.Id != entry.Id)
                .Append(entry)
                .OrderByDescending(e => e.Timestamp)
                .ToList();

        var result = await RevertCoreAsync(def, applied, null, ct).ConfigureAwait(false);
        return result.Status == TweakStatus.Success;
    }

    private async Task<JournalEntry> RevertCoreAsync(TweakDefinition def, IReadOnlyList<JournalEntry> applied,
        Action<string?, double?>? progress, CancellationToken ct)
    {
        var latest = applied.Count > 0 ? applied[0] : null;
        var ctx = CreateContext(def, latest?.Option, applied.Any(e => e.Extended), progress);
        var entry = NewEntry(def, TweakDirection.Revert, latest?.Option, false);
        entry.Reversible = false;
        if (_tweaks.ById(def.Id) is null && latest is { TitleKey.Length: > 0 }) entry.TitleKey = latest.TitleKey;

        try
        {
            var backups = applied.Where(HasBackup).ToList();
            ctx.RestoredFromBackup = backups.Count > 0;
            if (backups.Count > 0)
            {
                ctx.Log(backups.Count == 1
                    ? _loc["log.restoreFromBackup"]
                    : _loc.Format("log.restoreFromBackups", backups.Count));

                foreach (var backup in backups)
                    await RestoreFromEntryAsync(backup, ctx, ct).ConfigureAwait(false);
            }
            else if (def.RevertActions.Count > 0)
            {
                foreach (var action in def.RevertActions)
                {
                    ct.ThrowIfCancellationRequested();
                    Execute(ctx, action);
                }
            }
            else if (def.RegistryActions.Count > 0)
            {
                ctx.Log(_loc["log.noBackupInverse"]);
                foreach (var action in def.RegistryActions.Reverse())
                {
                    ct.ThrowIfCancellationRequested();
                    var inverse = NaturalInverse(action);
                    if (inverse is not null) Execute(ctx, inverse);
                    else ctx.Log(_loc.Format("log.noInverse", action.Path));
                }
            }

            if (def.CustomRevert is not null)
                await def.CustomRevert(ctx, ct).ConfigureAwait(false);

            foreach (var item in applied)
            {
                item.Reverted = true;
                await _journal.UpdateAsync(item).ConfigureAwait(false);
            }

            await FinishAsync(def, ctx, entry, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return await SkipAsync(entry, ctx, _loc["log.cancelled"]).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return await FailAsync(entry, ctx, ex).ConfigureAwait(false);
        }

        return entry;
    }

    private async Task RestoreFromEntryAsync(JournalEntry entry, TweakRunContext ctx, CancellationToken ct)
    {
        foreach (var key in entry.KeyBackups)
        {
            ct.ThrowIfCancellationRequested();
            if (key is { Existed: true, File: not null })
            {
                await _registry.ImportKeyAsync(key.File, ct).ConfigureAwait(false);
                ctx.Log(_loc.Format("log.keyRestored", key.Path));
            }
            else if (!key.Existed)
            {
                _registry.DeleteKey(key.Path);
            }
        }

        foreach (var snap in entry.RegistrySnapshots)
        {
            ct.ThrowIfCancellationRequested();
            _registry.Restore(snap);
            ctx.Log(_loc.Format("log.valueRestored", $@"{snap.Path}\{snap.Name}"));
        }
    }

    private static RegistryAction? NaturalInverse(RegistryAction action) => action.Op switch
    {
        RegistryOp.SetValue when !string.IsNullOrEmpty(action.Name) =>
            new RegistryAction { Op = RegistryOp.DeleteValue, Path = action.Path, Name = action.Name },
        RegistryOp.SetValue => new RegistryAction { Op = RegistryOp.DeleteKey, Path = action.Path },
        _ => null
    };

    // ----------------------------------------------------------------- Batch

    public async Task RunBatchAsync(Func<Task> body, Action<string?>? status)
    {
        Interlocked.Increment(ref _batchDepth);
        try
        {
            await body().ConfigureAwait(false);
        }
        finally
        {
            if (Interlocked.Decrement(ref _batchDepth) == 0 && Interlocked.Exchange(ref _explorerPending, 0) == 1)
            {
                status?.Invoke(_loc["log.explorerRestart"]);
                try { await RestartExplorerAsync(CancellationToken.None).ConfigureAwait(false); }
                catch { }
                status?.Invoke(null);
            }
        }
    }

    public void FlushPendingExplorerRestart()
    {
        if (Interlocked.Exchange(ref _explorerPending, 0) != 1) return;
        try { RestartExplorerAsync(CancellationToken.None).GetAwaiter().GetResult(); }
        catch { }
    }

    private bool DeferExplorerRestart()
    {
        if (Volatile.Read(ref _batchDepth) == 0 || !ExplorerRunning())
        {
            Interlocked.Exchange(ref _explorerPending, 0);
            return false;
        }

        Interlocked.Exchange(ref _explorerPending, 1);
        return Volatile.Read(ref _batchDepth) > 0 || Interlocked.Exchange(ref _explorerPending, 0) == 0;
    }

    // -------------------------------------------------------------- Helpers

    private static async Task SnapshotAsync(TweakRunContext ctx, RegistryAction action, CancellationToken ct)
    {
        if (action.Op == RegistryOp.DeleteKey)
        {
            await ctx.BackupKeyAsync(action.Path, ct).ConfigureAwait(false);
            return;
        }

        ctx.Backup(action.Path, action.Name);
    }

    private void Execute(TweakRunContext ctx, RegistryAction action)
    {
        switch (action.Op)
        {
            case RegistryOp.SetValue:
                _registry.SetValue(action.Path, action.Name, action.Value!, action.ValueKind);
                ctx.Log($"set  {action.Path}\\{action.Name} = {Describe(action)}");
                break;

            case RegistryOp.DeleteValue:
                _registry.DeleteValue(action.Path, action.Name);
                ctx.Log($"del  {action.Path}\\{action.Name}");
                break;

            case RegistryOp.DeleteKey:
                _registry.DeleteKey(action.Path);
                ctx.Log($"del  {action.Path}");
                break;
        }
    }

    private static string Describe(RegistryAction action) => action.ValueKind == RegistryValueKind.Binary
        ? Convert.ToHexString((byte[])action.Value!)
        : action.Value?.ToString() ?? "";

    private static JournalEntry NewEntry(TweakDefinition def, TweakDirection direction, string? option,
        bool extended) => new()
    {
        TweakId = def.Id,
        TitleKey = def.TitleKey,
        Category = def.Category,
        Direction = direction,
        Option = option,
        Extended = extended,
        Reversible = def.Reversible
    };

    private async Task FinishAsync(TweakDefinition def, TweakRunContext ctx, JournalEntry entry, CancellationToken ct)
    {
        if (def.NeedsExplorerRestart || ctx.ExplorerRestartRequested)
        {
            if (DeferExplorerRestart())
            {
                ctx.Log(_loc["log.explorerRestartDeferred"]);
            }
            else
            {
                ctx.Progress(_loc["log.explorerRestart"]);
                await RestartExplorerAsync(ct).ConfigureAwait(false);
            }
        }

        entry.Status = TweakStatus.Success;
        entry.Message = ctx.ResultMessage;
        entry.HasIssues = ctx.ResultHasIssues;
        entry.Log = ctx.Lines;
        entry.RegistrySnapshots = ctx.Snapshots;
        entry.KeyBackups = ctx.KeyBackups;

        if (entry.Direction == TweakDirection.Apply)
            entry.Reversible = def.Reversible && HasBackup(entry);

        await _journal.AppendAsync(entry).ConfigureAwait(false);
    }

    private static bool HasBackup(JournalEntry entry) =>
        entry.RegistrySnapshots.Count > 0 || entry.KeyBackups.Count > 0;

    private async Task<JournalEntry> SkipAsync(JournalEntry entry, TweakRunContext ctx, string reason)
    {
        ctx.Log(reason);
        entry.Status = TweakStatus.Skipped;
        entry.Message = reason;
        entry.Reversible = false;
        entry.Log = ctx.Lines;
        await _journal.AppendAsync(entry).ConfigureAwait(false);
        return entry;
    }

    private async Task<JournalEntry> FailAsync(JournalEntry entry, TweakRunContext ctx, Exception ex)
    {
        ctx.Log(_loc.Format("log.error", ex.Message));
        entry.Status = TweakStatus.Failed;
        entry.Message = ex.Message;
        entry.Log = ctx.Lines;
        entry.RegistrySnapshots = ctx.Snapshots;
        entry.KeyBackups = ctx.KeyBackups;
        entry.Reversible = HasBackup(entry);
        await _journal.AppendAsync(entry).ConfigureAwait(false);
        return entry;
    }

    private async Task RestartExplorerAsync(CancellationToken ct)
    {
        await _process.RunAsync("taskkill.exe", "/f /im explorer.exe", ct, timeoutMs: 15000).ConfigureAwait(false);
        await Task.Delay(500, ct).ConfigureAwait(false);

        await ClearIconCacheAsync(ct).ConfigureAwait(false);

        if (!ExplorerRunning())
        {
            var explorer = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            await _process.LaunchAsync(explorer, "", false, ct).ConfigureAwait(false);
        }
    }

    private static bool ExplorerRunning()
    {
        var processes = System.Diagnostics.Process.GetProcessesByName("explorer");
        foreach (var process in processes) process.Dispose();
        return processes.Length > 0;
    }

    private async Task ClearIconCacheAsync(CancellationToken ct)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var explorerDir = Path.Combine(local, @"Microsoft\Windows\Explorer");

        foreach (var (dir, pattern) in new[]
                 {
                     (explorerDir, "iconcache*"),
                     (explorerDir, "thumbcache*"),
                     (local, "IconCache.db")
                 })
        {
            foreach (var file in FileOps.Files(dir, pattern))
            {
                ct.ThrowIfCancellationRequested();
                FileOps.TryDeleteFile(file);
            }
        }

        await _process.RunAsync("ie4uinit.exe", "-show", ct, timeoutMs: 15000).ConfigureAwait(false);
    }
}
