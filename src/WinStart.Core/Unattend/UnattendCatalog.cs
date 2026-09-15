using Schneegans.Unattend;

namespace WinStart.Core.Unattend;

public sealed record UnattendChoice(string Value, string Title);

public static class UnattendCatalog
{
    public static UnattendGenerator Generator { get; } = new();

    public static IReadOnlyList<UnattendChoice> ImageLanguages { get; } =
        Pinned(Sorted(Generator.ImageLanguages.Values.Select(l => new UnattendChoice(l.Id, l.DisplayName))), "ru-RU", "en-US");

    public static IReadOnlyList<UnattendChoice> UserLocales { get; } =
        Pinned(Sorted(Generator.UserLocales.Values.Select(l => new UnattendChoice(l.Id, l.DisplayName))), "ru-RU", "en-US");

    public static IReadOnlyList<UnattendChoice> Keyboards { get; } =
        Pinned(Sorted(Generator.KeyboardIdentifiers.Values.Select(k => new UnattendChoice(k.Id, k.DisplayName))), "00000419", "00000409");

    public static IReadOnlyList<UnattendChoice> GeoLocations { get; } =
        Sorted(Generator.GeoLocations.Values.Select(g => new UnattendChoice(g.Id, g.DisplayName)));

    public static IReadOnlyList<UnattendChoice> TimeZones { get; } =
        Generator.TimeOffsets.Values
            .Select(t => new UnattendChoice(t.Id, t.DisplayName))
            .OrderBy(t => UtcOffset(t.Title))
            .ThenBy(t => t.Title, StringComparer.Ordinal)
            .ToList();

    public static IReadOnlyList<UnattendChoice> Editions { get; } =
        Generator.WindowsEditions.Values.Where(e => e.Visible).Select(e => new UnattendChoice(e.Id, e.DisplayName)).ToList();

    public static IReadOnlyList<Bloatware> Bloatwares { get; } =
        Generator.Bloatwares.Values.OrderBy(b => b.DisplayName, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<DesktopIcon> DesktopIcons { get; } =
        Generator.DesktopIcons.Values.OrderBy(i => i.DisplayName, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<StartFolder> StartFolders { get; } =
        Generator.StartFolders.Values.OrderBy(f => f.DisplayName, StringComparer.Ordinal).ToList();

    public static IReadOnlyList<(Component Component, Pass Pass)> ComponentPasses { get; } =
        Generator.Components.Values
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .SelectMany(c => c.Passes.Select(p => (c, p)))
            .ToList();

    public static string ComponentKey(Component component, Pass pass) => $"{component.Id}|{pass}";

    public static string? DefaultKeyboardFor(string localeId) =>
        Generator.UserLocales.TryGetValue(localeId, out var locale) ? locale.KeyboardLayout?.Id : null;

    public static string? DefaultGeoLocationFor(string localeId) =>
        Generator.UserLocales.TryGetValue(localeId, out var locale) ? locale.GeoLocation?.Id : null;

    private static List<UnattendChoice> Sorted(IEnumerable<UnattendChoice> items) =>
        items.OrderBy(i => i.Title, StringComparer.Ordinal).ToList();

    private static List<UnattendChoice> Pinned(List<UnattendChoice> items, params string[] ids)
    {
        var top = ids.Select(id => items.First(i => i.Value == id)).ToList();
        return top.Concat(items.Except(top)).ToList();
    }

    private static int UtcOffset(string title)
    {
        var m = System.Text.RegularExpressions.Regex.Match(title, @"^\(UTC([+-])(\d{2}):(\d{2})\)");
        if (!m.Success) return 0;
        var minutes = int.Parse(m.Groups[2].Value) * 60 + int.Parse(m.Groups[3].Value);
        return m.Groups[1].Value == "-" ? -minutes : minutes;
    }
}
