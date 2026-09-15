using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using WinStart.App.Infrastructure;
using WinStart.Core.Abstractions;

namespace WinStart.App.Services;

public sealed record UpdateInfo(Version Version, string DownloadUrl, long Size);

public sealed record ReleaseNotes(Version Version, DateTime Date, IReadOnlyList<string> Ru, IReadOnlyList<string> En);

public sealed record UpdateCheck(UpdateInfo? Update, IReadOnlyList<ReleaseNotes> Newer)
{
    public static UpdateCheck None { get; } = new(null, []);
}

public interface IUpdateService
{
    Task<UpdateCheck> CheckAsync(CancellationToken ct);

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

    public static Version CurrentVersion => Normalize(Version.Parse(AppInfo.Version));

    public async Task<UpdateCheck> CheckAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(
            $"https://api.github.com/repos/{AppInfo.GitHubRepository}/releases?per_page=30", ct).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return UpdateCheck.None;
        response.EnsureSuccessStatusCode();

        return Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false), CurrentVersion);
    }

    public static UpdateCheck Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var newer = new List<ReleaseNotes>();
        UpdateInfo? update = null;

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft") || Flag(release, "prerelease")) continue;

            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) continue;

            var version = Normalize(parsed);
            if (version <= current) continue;

            var date = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                       && DateTime.TryParse(p.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime()
                : DateTime.Now;

            var (ru, en) = SplitNotes(release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
            newer.Add(new ReleaseNotes(version, date, ru, en));

            var installer = Installer(release);
            if (installer is not null && (update is null || version > update.Version))
                update = new UpdateInfo(version, installer.Value.Url, installer.Value.Size);
        }

        return new UpdateCheck(update, newer.OrderByDescending(r => r.Version).ToList());
    }

    private static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static (string Url, long Size)? Installer(JsonElement release)
    {
        if (!release.TryGetProperty("assets", out var assets)) return null;
        foreach (var asset in assets.EnumerateArray())
        {
            if (!string.Equals(asset.GetProperty("name").GetString(), InstallerName, StringComparison.OrdinalIgnoreCase))
                continue;
            return (asset.GetProperty("browser_download_url").GetString() ?? "",
                asset.TryGetProperty("size", out var size) ? size.GetInt64() : 0);
        }
        return null;
    }

    private static (IReadOnlyList<string> Ru, IReadOnlyList<string> En) SplitNotes(string body)
    {
        var parts = body.Replace("\r\n", "\n").Split("\n---", 2);
        var ru = Bullets(parts[0]);
        var en = parts.Length > 1 ? Bullets(parts[1]) : [];
        return (ru, en);
    }

    private static List<string> Bullets(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => l[2..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

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
