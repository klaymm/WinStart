using WinStart.Core.Models;

namespace WinStart.Core.Abstractions;

public interface ITweakContext
{
    IRegistryService Registry { get; }
    IProcessRunner Process { get; }
    IDownloadService Download { get; }
    IArchiveService Archive { get; }
    IPathProvider Paths { get; }

    string? Option { get; }

    bool Extended { get; }

    string Text(string key, params object[] args);

    void Log(string message);

    void Progress(string? status, double? percent = null);

    void Result(string message, bool hasIssues);

    void Backup(string path, string? name);

    Task BackupKeyAsync(string path, CancellationToken ct);

    void RequestExplorerRestart();
}
