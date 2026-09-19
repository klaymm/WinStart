using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinStart.Core.Apps;

public enum WingetCategory { Browsers, Messengers, Media, Office, Archivers, Tools, System, Development, Games, Server }

public sealed class WingetApp
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required WingetCategory Category { get; init; }
    public string DescRu { get; init; } = "";
    public string DescEn { get; init; } = "";
    public IReadOnlyList<string> Exes { get; init; } = [];
    public string Company { get; init; } = "";

    public string Description(string language) => language == "ru" ? DescRu : DescEn;
}

public static class WingetCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static IReadOnlyList<WingetApp> Apps { get; } = Load();

    public static WingetApp? ById(string id) => Apps.FirstOrDefault(a => a.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static string InstallArguments(string id) =>
        $"install --id {id} --exact --source winget --silent --accept-package-agreements --accept-source-agreements --disable-interactivity";

    private static List<WingetApp> Load()
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("WinStart.Core.Apps.catalog.json")
                           ?? throw new InvalidOperationException("Каталог программ не найден в ресурсах");
        return JsonSerializer.Deserialize<List<WingetApp>>(stream, JsonOptions) ?? [];
    }
}
