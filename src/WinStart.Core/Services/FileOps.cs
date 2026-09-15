namespace WinStart.Core.Services;

internal static class FileOps
{
    public static bool TryDeleteFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return true;
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }

    public static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            return true;
        }
        catch { return false; }
    }

    public static void TryRemoveEmptyDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path) && !Directory.EnumerateFileSystemEntries(path).Any())
                Directory.Delete(path);
        }
        catch { }
    }

    public static void TryCopyFile(string source, string target)
    {
        try
        {
            if (!File.Exists(source)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }
        catch { }
    }

    public static IReadOnlyList<string> Files(string dir, string pattern = "*", bool recursive = false)
    {
        try
        {
            if (!Directory.Exists(dir)) return [];
            return Directory.EnumerateFiles(dir, pattern,
                recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly).ToList();
        }
        catch { return []; }
    }

    public static IReadOnlyList<string> Directories(string dir, string pattern = "*")
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateDirectories(dir, pattern).ToList() : [];
        }
        catch { return []; }
    }
}
