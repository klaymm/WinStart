using System.IO;

namespace WinStart.Installer;

public static class FolderSwap
{
    public static bool Replace(string target, string source, string displaced)
    {
        TryDelete(displaced);
        if (Directory.Exists(displaced))
            throw new IOException($"Не удалось освободить папку {displaced}");

        if (!Directory.Exists(target))
        {
            Retry(() => Directory.Move(source, target));
            return false;
        }

        if (TryRetry(() => Directory.Move(target, displaced), 5))
        {
            try
            {
                Retry(() => Directory.Move(source, target));
            }
            catch
            {
                TryRetry(() => Directory.Move(displaced, target), 10);
                throw;
            }
            return true;
        }

        CopyTree(target, displaced);
        try
        {
            DeleteContents(target);
            CopyTree(source, target);
        }
        catch
        {
            try
            {
                DeleteContents(target);
                CopyTree(displaced, target);
            }
            catch { }
            throw;
        }

        TryDelete(source);
        return true;
    }

    public static void Retry(Action action, int attempts = 10)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when ((ex is IOException or UnauthorizedAccessException) && i < attempts)
            {
                Thread.Sleep(300);
            }
        }
    }

    private static bool TryRetry(Action action, int attempts)
    {
        try
        {
            Retry(action, attempts);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void CopyTree(string source, string destination)
    {
        var self = Environment.ProcessPath;
        Directory.CreateDirectory(destination);

        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(destination, Path.GetRelativePath(source, dir)));

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destination, Path.GetRelativePath(source, file));
            if (self is not null && string.Equals(dest, self, StringComparison.OrdinalIgnoreCase)) continue;
            File.Copy(file, dest, true);
        }
    }

    public static void DeleteContents(string dir)
    {
        if (!Directory.Exists(dir)) return;
        var self = Environment.ProcessPath;

        foreach (var file in SafeFiles(dir))
        {
            if (self is not null && string.Equals(file, self, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.SetAttributes(file, FileAttributes.Normal); File.Delete(file); }
            catch { }
        }

        foreach (var sub in SafeDirs(dir))
        {
            try { Directory.Delete(sub, true); }
            catch { }
        }
    }

    public static void TryDelete(string? dir)
    {
        if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return;
        try
        {
            Directory.Delete(dir, true);
        }
        catch
        {
            try
            {
                foreach (var file in SafeFiles(dir)) File.SetAttributes(file, FileAttributes.Normal);
                Directory.Delete(dir, true);
            }
            catch { }
        }
    }

    public static void RestoreFiles(string backupDir, string destination)
    {
        if (!Directory.Exists(backupDir)) return;
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(backupDir))
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), true);
    }

    private static IEnumerable<string> SafeFiles(string dir)
    {
        try { return Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList(); }
        catch { return []; }
    }

    private static IEnumerable<string> SafeDirs(string dir)
    {
        try { return Directory.EnumerateDirectories(dir).ToList(); }
        catch { return []; }
    }
}
