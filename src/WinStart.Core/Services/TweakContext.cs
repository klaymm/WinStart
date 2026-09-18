using WinStart.Core.Abstractions;
using WinStart.Core.Models;

namespace WinStart.Core.Services;

public sealed class TweakRunContext : ITweakContext
{
    private readonly string _backupFolder;
    private readonly ILocalizationService _loc;
    private readonly Action<string?, double?>? _progress;

    public TweakRunContext(
        IRegistryService registry,
        IProcessRunner process,
        IDownloadService download,
        IArchiveService archive,
        IPathProvider paths,
        ILocalizationService loc,
        string tweakId,
        string? option,
        bool extended,
        Action<string?, double?>? progress)
    {
        Registry = registry;
        Process = process;
        Download = download;
        Archive = archive;
        Paths = paths;
        Option = option;
        Extended = extended;
        _loc = loc;
        _progress = progress;

        _backupFolder = Path.Combine(paths.Backups, $"{DateTime.Now:yyyyMMdd_HHmmss}_{tweakId}");
    }

    public IRegistryService Registry { get; }
    public IProcessRunner Process { get; }
    public IDownloadService Download { get; }
    public IArchiveService Archive { get; }
    public IPathProvider Paths { get; }
    public string? Option { get; }
    public bool Extended { get; }
    public bool RestoredFromBackup { get; set; }

    public List<string> Lines { get; } = [];
    public List<RegistryValueSnapshot> Snapshots { get; } = [];
    public List<RegistryKeyBackup> KeyBackups { get; } = [];
    public bool ExplorerRestartRequested { get; private set; }
    public string? ResultMessage { get; private set; }
    public bool ResultHasIssues { get; private set; }

    public string Text(string key, params object[] args) => args.Length == 0 ? _loc[key] : _loc.Format(key, args);

    public void Log(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (Lines) Lines.Add(line);
    }

    public void Result(string message, bool hasIssues)
    {
        Log(message);
        ResultMessage = message;
        ResultHasIssues = hasIssues;
    }

    public void Progress(string? status, double? percent = null)
    {
        if (status is not null) Log(status);
        _progress?.Invoke(status, percent);
    }

    public void Backup(string path, string? name)
    {
        var snap = Registry.Snapshot(path, name);
        lock (Snapshots)
        {
            if (!Snapshots.Any(s => s.Path.Equals(path, StringComparison.OrdinalIgnoreCase)
                                    && string.Equals(s.Name ?? "", name ?? "", StringComparison.OrdinalIgnoreCase)))
                Snapshots.Add(snap);
        }
    }

    public async Task BackupKeyAsync(string path, CancellationToken ct)
    {
        var exists = Registry.KeyExists(path);
        var backup = new RegistryKeyBackup { Path = path, Existed = exists };

        if (exists)
        {
            Directory.CreateDirectory(_backupFolder);
            var file = Path.Combine(_backupFolder, Sanitize(path) + ".reg");
            if (await Registry.ExportKeyAsync(path, file, ct).ConfigureAwait(false))
                backup.File = file;
        }

        lock (KeyBackups) KeyBackups.Add(backup);
    }

    public void RequestExplorerRestart() => ExplorerRestartRequested = true;

    private static string Sanitize(string path)
    {
        var name = path;
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name.Length > 120 ? name[^120..] : name;
    }
}
