namespace WinStart.Core.Abstractions;

public interface IDownloadService
{
    Task<bool> IsOnlineAsync(CancellationToken ct);

    Task<bool> DownloadAsync(IEnumerable<string> mirrors, string destination,
        IProgress<double>? progress, CancellationToken ct);

    Task DownloadFileAsync(string url, string destination, IProgress<double>? progress,
        int attempts, CancellationToken ct);
}

public interface IArchiveService
{
    Task<bool> ExtractAsync(string archive, string destination, string? password, CancellationToken ct);
}
