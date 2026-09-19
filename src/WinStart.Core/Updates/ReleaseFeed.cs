using System.Runtime.InteropServices;
using System.Text.Json;

namespace WinStart.Core.Updates;

public sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);

public sealed record ReleaseEntry(
    SemVersion Version,
    DateTime Published,
    IReadOnlyList<string> Ru,
    IReadOnlyList<string> En,
    IReadOnlyList<ReleaseAsset> Assets);

public sealed record InstallerChoice(ReleaseAsset Asset, string? Sha256);

public sealed class UpdateManifest
{
    public string Version { get; set; } = "";
    public string? MinimumVersion { get; set; }
    public List<ManifestInstaller> Installers { get; set; } = [];
}

public sealed class ManifestInstaller
{
    public string Architecture { get; set; } = "x64";
    public string File { get; set; } = "";
    public string Sha256 { get; set; } = "";
}

public static class ReleaseFeed
{
    public const string InstallerName = "WinStartSetup.exe";
    public const string ManifestName = "update-manifest.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string CurrentArchitecture =>
        RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64";

    public static List<ReleaseEntry> Parse(string json, bool includePreReleases)
    {
        using var document = JsonDocument.Parse(json);
        var result = new List<ReleaseEntry>();

        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (Flag(release, "draft")) continue;
            if (Flag(release, "prerelease") && !includePreReleases) continue;

            var tag = release.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
            if (!SemVersion.TryParse(tag, out var version)) continue;
            if (version.IsPreRelease && !includePreReleases) continue;

            var published = release.TryGetProperty("published_at", out var p) && p.ValueKind == JsonValueKind.String
                            && DateTime.TryParse(p.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var d)
                ? d.ToLocalTime()
                : DateTime.Now;

            var (ru, en) = SplitNotes(release.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "");
            result.Add(new ReleaseEntry(version, published, ru, en, Assets(release)));
        }

        return result.OrderByDescending(r => r.Version).ToList();
    }

    public static bool AllowsUpgradeFrom(UpdateManifest? manifest, SemVersion current) =>
        manifest?.MinimumVersion is not { Length: > 0 } minimum
        || !SemVersion.TryParse(minimum, out var min)
        || current >= min;

    public static InstallerChoice? PickInstaller(ReleaseEntry release, UpdateManifest? manifest, string architecture)
    {
        if (manifest is { Installers.Count: > 0 })
        {
            foreach (var arch in new[] { architecture, "x64" }.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var entry = manifest.Installers.FirstOrDefault(i =>
                    i.Architecture.Equals(arch, StringComparison.OrdinalIgnoreCase));
                var asset = entry is null ? null : release.Assets.FirstOrDefault(a =>
                    a.Name.Equals(entry.File, StringComparison.OrdinalIgnoreCase));
                if (asset is not null)
                    return new InstallerChoice(asset, Normalize(entry!.Sha256) ?? asset.Sha256);
            }

            return null;
        }

        var legacy = release.Assets.FirstOrDefault(a => a.Name.Equals(InstallerName, StringComparison.OrdinalIgnoreCase));
        return legacy is null ? null : new InstallerChoice(legacy, legacy.Sha256);
    }

    public static ReleaseAsset? ManifestAsset(ReleaseEntry release) =>
        release.Assets.FirstOrDefault(a => a.Name.Equals(ManifestName, StringComparison.OrdinalIgnoreCase));

    public static (IReadOnlyList<string> Ru, IReadOnlyList<string> En) SplitNotes(string body)
    {
        var parts = body.Replace("\r\n", "\n").Split("\n---", 2);
        return (Bullets(parts[0]), parts.Length > 1 ? Bullets(parts[1]) : []);
    }

    private static List<string> Bullets(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => l[2..].Trim())
            .Where(l => l.Length > 0)
            .ToList();

    private static List<ReleaseAsset> Assets(JsonElement release)
    {
        var list = new List<ReleaseAsset>();
        if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) return list;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(url)) continue;

            var size = asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt64() : 0;
            var digest = asset.TryGetProperty("digest", out var dg) && dg.ValueKind == JsonValueKind.String
                ? dg.GetString()
                : null;

            list.Add(new ReleaseAsset(name, url, size,
                digest is not null && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                    ? Normalize(digest[7..])
                    : null));
        }

        return list;
    }

    private static string? Normalize(string? sha256)
    {
        var hex = sha256?.Trim().ToLowerInvariant();
        return hex is { Length: 64 } && hex.All(Uri.IsHexDigit) ? hex : null;
    }

    private static bool Flag(JsonElement release, string name) =>
        release.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
