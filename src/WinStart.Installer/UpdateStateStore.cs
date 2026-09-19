using System.IO;
using System.Text.Json;

namespace WinStart.Installer;

public sealed class UpdateStateDto
{
    public string FromVersion { get; set; } = "";
    public string ToVersion { get; set; } = "";
    public string InstallDir { get; set; } = "";
    public string BackupDir { get; set; } = "";
    public string? DataDir { get; set; }
    public string? DataBackupDir { get; set; }
    public bool Confirmed { get; set; }
    public DateTime StartedUtc { get; set; }
}

public static class UpdateStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinStart", "update-state.json");

    public static UpdateStateDto? Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file)
                ? JsonSerializer.Deserialize<UpdateStateDto>(File.ReadAllText(file), JsonOptions)
                : null;
        }
        catch { return null; }
    }

    public static void Save(UpdateStateDto state, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
        File.Move(temp, file, true);
    }

    public static void Delete(string? path = null)
    {
        try { File.Delete(path ?? DefaultPath); }
        catch { }
    }
}
