using System.Globalization;
using System.Reflection;
using WinStart.Core.Abstractions;
using WinStart.Core.Updates;

namespace WinStart.App.Infrastructure;

public static class AppInfo
{
    public const string GitHubRepository = "klaymm/WinStart";

    public static string Version { get; } = ReadVersion();

    public static SemVersion Semantic { get; } =
        SemVersion.TryParse(Version, out var v) ? v : new SemVersion(1, 1, 0);

    public static string ShortVersion
    {
        get
        {
            var parts = Version.Split('.');
            return parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : Version;
        }
    }

    public static DateTime? BuildDate { get; } = ReadBuildDate();

    public static string Describe(ILocalizationService loc) =>
        BuildDate is { } date
            ? loc.Format("app.versionBuild", Version, date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture))
            : $"v{Version}";

    private static string InformationalVersion =>
        Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

    private static string ReadVersion()
    {
        if (SemVersion.TryParse(InformationalVersion, out var semantic)) return semantic.ToString();

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "1.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static DateTime? ReadBuildDate()
    {
        var info = InformationalVersion;
        var marker = info.IndexOf("build.", StringComparison.Ordinal);
        if (marker < 0) return null;

        var raw = info[(marker + 6)..];
        return raw.Length >= 8 &&
               DateTime.TryParseExact(raw[..8], "yyyyMMdd", CultureInfo.InvariantCulture,
                   DateTimeStyles.None, out var date)
            ? date
            : null;
    }
}
