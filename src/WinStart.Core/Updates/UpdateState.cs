using System.Diagnostics;
using System.Text.Json;

namespace WinStart.Core.Updates;

public sealed class UpdateState
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

public static class UpdateStateFile
{
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinStart", "update-state.json");

    public static UpdateState? Load(string? path = null)
    {
        try
        {
            var file = path ?? DefaultPath;
            return File.Exists(file)
                ? JsonSerializer.Deserialize<UpdateState>(File.ReadAllText(file), ReleaseFeed.JsonOptions)
                : null;
        }
        catch { return null; }
    }

    public static void Save(UpdateState state, string? path = null)
    {
        var file = path ?? DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, ReleaseFeed.JsonOptions));
        File.Move(temp, file, true);
    }

    public static void Delete(string? path = null)
    {
        try { File.Delete(path ?? DefaultPath); }
        catch { }
    }
}

public static class UpdateLifecycle
{
    public static void OnStarted(SemVersion current, string? statePath = null, Func<bool>? installerRunning = null)
    {
        var state = UpdateStateFile.Load(statePath);
        if (state is null) return;

        var matches = SemVersion.TryParse(state.ToVersion, out var target) && target.CompareTo(current) == 0;

        if (matches && !state.Confirmed)
        {
            state.Confirmed = true;
            try { UpdateStateFile.Save(state, statePath); }
            catch { }
            return;
        }

        if ((installerRunning ?? InstallerRunning)()) return;

        if (matches) TryDeleteDirectory(state.BackupDir);
        UpdateStateFile.Delete(statePath);
    }

    private static bool InstallerRunning()
    {
        var processes = Process.GetProcessesByName("WinStartSetup");
        foreach (var p in processes) p.Dispose();
        return processes.Length > 0;
    }

    private static void TryDeleteDirectory(string? dir)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        catch { }
    }
}
