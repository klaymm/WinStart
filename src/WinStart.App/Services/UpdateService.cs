using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using WinStart.App.Infrastructure;
using WinStart.Core.Abstractions;

namespace WinStart.App.Services;

public sealed record UpdateInfo(Version Version, string Notes, string DownloadUrl, long Size);

public interface IUpdateService
{
    Task<UpdateInfo?> CheckAsync(CancellationToken ct);

    Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct);

    void InstallAndExit(string installerPath);
}

public sealed class UpdateService(IDownloadService download) : IUpdateService, IDisposable
{
    private const string InstallerName = "WinStartSetup.exe";

    private readonly HttpClient _http = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
        DefaultRequestHeaders = { { "User-Agent", $"WinStart/{AppInfo.Version}" }, { "Accept", "application/vnd.github+json" } }
    };

    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases/latest", ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
        var root = json.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version)) return null;
        if (Normalize(version) <= Normalize(Version.Parse(AppInfo.Version))) return null;

        if (!root.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), InstallerName, StringComparison.OrdinalIgnoreCase))
                continue;

            return new UpdateInfo(
                Normalize(version),
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0);
        }

        return null;
    }

    public async Task<string> DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
    {
        var path = Path.Combine(Path.GetTempPath(), "WinStart", InstallerName);
        if (!await download.DownloadAsync([info.DownloadUrl], path, progress, ct).ConfigureAwait(false))
            throw new IOException("download failed");
        return path;
    }

    public void InstallAndExit(string installerPath)
    {
        Process.Start(new ProcessStartInfo(installerPath, "/update") { UseShellExecute = true });
        System.Windows.Application.Current.Shutdown();
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), 0);

    public void Dispose() => _http.Dispose();
}
