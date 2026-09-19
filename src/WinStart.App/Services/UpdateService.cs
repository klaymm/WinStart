using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using WinStart.App.Infrastructure;
using WinStart.Core.Abstractions;
using WinStart.Core.Updates;

namespace WinStart.App.Services;

public sealed record UpdateInfo(SemVersion Version, string DownloadUrl, long Size, string? Sha256);

public sealed record ReleaseNotes(SemVersion Version, DateTime Date, IReadOnlyList<string> Ru, IReadOnlyList<string> En);

public sealed record UpdateCheck(UpdateInfo? Update, IReadOnlyList<ReleaseNotes> Newer)
{
    public static UpdateCheck None { get; } = new(null, []);
}

public sealed class UpdateIntegrityException() : Exception("SHA-256 mismatch");

public interface IUpdateService
{
    Task<UpdateCheck> CheckAsync(bool includePreReleases, CancellationToken ct);

    Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct);

    void InstallAndExit(string installerPath);
}

public sealed class UpdateService(IDownloadService download) : IUpdateService, IDisposable
{
    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", $"WinStart/{AppInfo.Version}" }, { "Accept", "application/vnd.github+json" } }
    };

    public async Task<UpdateCheck> CheckAsync(bool includePreReleases, CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases?per_page=30", ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return UpdateCheck.None;
        response.EnsureSuccessStatusCode();

        var current = AppInfo.Semantic;
        var newer = ReleaseFeed.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), includePreReleases)
            .Where(r => r.Version > current)
            .ToList();

        UpdateInfo? update = null;
        foreach (var release in newer)
        {
            var manifest = await ManifestAsync(release, ct).ConfigureAwait(false);
            if (!ReleaseFeed.AllowsUpgradeFrom(manifest, current)) continue;

            var choice = ReleaseFeed.PickInstaller(release, manifest, ReleaseFeed.CurrentArchitecture);
            if (choice is null) continue;

            update = new UpdateInfo(release.Version, choice.Asset.Url, choice.Asset.Size, choice.Sha256);
            break;
        }

        return new UpdateCheck(update,
            newer.Select(r => new ReleaseNotes(r.Version, r.Published, r.Ru, r.En)).ToList());
    }

    private async Task<UpdateManifest?> ManifestAsync(ReleaseEntry release, CancellationToken ct)
    {
        var asset = ReleaseFeed.ManifestAsset(release);
        if (asset is null) return null;

        using var response = await _http.GetAsync(asset.Url, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        try
        {
            return JsonSerializer.Deserialize<UpdateManifest>(
                await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), ReleaseFeed.JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "WinStart", ReleaseFeed.InstallerName);
        if (!await download.DownloadAsync([info.DownloadUrl], path, progress, ct).ConfigureAwait(false))
            throw new IOException("download failed");

        if (info.Sha256 is not null && !await MatchesAsync(path, info.Sha256, ct).ConfigureAwait(false))
        {
            try { File.Delete(path); }
            catch { }
            throw new UpdateIntegrityException();
        }

        return path;
    }

    private static async Task<bool> MatchesAsync(string path, string expected, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
        return hash.Equals(expected, StringComparison.OrdinalIgnoreCase);
    }

    public void InstallAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath, "/update") { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose() => _http.Dispose();
}
