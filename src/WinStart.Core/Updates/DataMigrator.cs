using System.Text.Json;

namespace WinStart.Core.Updates;

public sealed record DataMigration(int To, Action<string> Apply);

public static class DataMigrator
{
    public const int CurrentVersion = 1;
    private const string VersionFileName = "data-version.json";

    public static IReadOnlyList<DataMigration> Steps { get; } = [];

    public static int Run(string userData, IReadOnlyList<DataMigration>? steps = null, int? target = null,
        string? statePath = null)
    {
        steps ??= Steps;
        var to = target ?? CurrentVersion;
        var file = Path.Combine(userData, VersionFileName);
        var from = ReadVersion(file);

        if (from is null)
        {
            WriteVersion(file, to);
            return to;
        }

        if (from >= to) return from.Value;

        var backup = Backup(userData, from.Value);
        var state = UpdateStateFile.Load(statePath);
        if (state is { Confirmed: false })
        {
            state.DataDir = userData;
            state.DataBackupDir = backup;
            try { UpdateStateFile.Save(state, statePath); }
            catch { }
        }

        try
        {
            foreach (var step in steps.Where(s => s.To > from && s.To <= to).OrderBy(s => s.To))
            {
                step.Apply(userData);
                WriteVersion(file, step.To);
            }
        }
        catch
        {
            Restore(backup, userData);
            throw;
        }

        WriteVersion(file, to);
        return to;
    }

    public static void Restore(string backupDir, string userData)
    {
        if (!Directory.Exists(backupDir)) return;
        foreach (var file in Directory.EnumerateFiles(backupDir))
            File.Copy(file, Path.Combine(userData, Path.GetFileName(file)), true);
    }

    private static string Backup(string userData, int from)
    {
        var dest = Path.Combine(userData, "migrations", $"v{from}-{DateTime.Now:yyyyMMdd-HHmmss}");
        Directory.CreateDirectory(dest);
        foreach (var file in Directory.EnumerateFiles(userData, "*.json"))
            File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
        return dest;
    }

    private static int? ReadVersion(string file)
    {
        try
        {
            if (!File.Exists(file)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(file));
            return doc.RootElement.TryGetProperty("version", out var v) && v.TryGetInt32(out var n) ? n : null;
        }
        catch { return null; }
    }

    private static void WriteVersion(string file, int version)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, $"{{ \"version\": {version} }}");
        }
        catch { }
    }
}
