using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using Microsoft.Win32;

namespace WinStart.Installer;

public sealed class InstallService
{
    public const string AppName = "WinStart";
    public const string ExeName = "WinStart.exe";
    private const string UninstallKey =
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\WinStart";

    public string DefaultLocation =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), AppName);

    public const string UninstallerName = "WinStartSetup.exe";

    public static string? InstalledLocation
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(UninstallKey);
                var path = key?.GetValue("InstallLocation") as string;
                return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path) ? path : null;
            }
            catch { return null; }
        }
    }

    public static string NormalizeTarget(string path)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path.Trim()));
        return IsOwnFolder(full) ? full : Path.Combine(full, AppName);
    }

    private static bool IsOwnFolder(string dir) =>
        string.Equals(Path.GetFileName(Path.TrimEndingDirectorySeparator(dir)), AppName,
            StringComparison.OrdinalIgnoreCase);

    private static string ToolsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), $"{AppName} Tools");

    private static readonly string[] ContextMenuKeys =
    [
        @"*\shell\Destroy", @"*\shell\Unlock",
        @"Directory\shell\Destroy", @"Directory\shell\Unlock",
        @"Directory\Background\shell\Everything"
    ];

    public async Task UninstallAsync(string targetDir,
        IProgress<(string Stage, double Percent)> progress, CancellationToken ct,
        bool full = true)
    {
        progress.Report(("removing", 10));
        StopApp(Path.GetFileNameWithoutExtension(ExeName));

        progress.Report(("removing", 30));
        RemoveShortcuts();

        progress.Report(("removing", 45));
        try { Registry.LocalMachine.DeleteSubKeyTree(UninstallKey, false); }
        catch { }

        progress.Report(("removing", 60));

        if (IsOwnFolder(targetDir))
            await Task.Run(() => DeleteContents(targetDir), ct).ConfigureAwait(false);

        progress.Report(("removing", 80));
        if (full)
        {
            StopApp("Everything");
            await Task.Run(() => TryDeleteFolder(ToolsFolder), ct).ConfigureAwait(false);
        }
        RemoveContextMenu(all: full);

        if (full && IsOwnFolder(targetDir)) ScheduleFolderRemoval(targetDir);

        progress.Report(("done", 100));
    }

    private static void StopApp(string processName)
    {
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(true); p.WaitForExit(5000); }
            catch { }
        }
    }

    private static void RemoveContextMenu(bool all)
    {
        foreach (var key in ContextMenuKeys)
        {
            try
            {
                if (!all)
                {
                    using var command = Registry.ClassesRoot.OpenSubKey($@"{key}\command");
                    if (command?.GetValue("") is not string cmd) continue;

                    var exe = CommandTarget(cmd);
                    if (exe is not null && File.Exists(exe)) continue;
                }

                Registry.ClassesRoot.DeleteSubKeyTree(key, false);
            }
            catch { }
        }
    }

    private static string? CommandTarget(string command)
    {
        command = command.Trim();
        if (command.Length == 0) return null;

        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            return end > 1 ? command[1..end] : null;
        }

        var space = command.IndexOf(' ');
        return space < 0 ? command : command[..space];
    }

    private static void TryDeleteFolder(string dir)
    {
        try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        catch { }
    }

    private static void RemoveShortcuts()
    {
        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs", $"{AppName}.lnk");
        var desktop = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk");

        foreach (var lnk in new[] { startMenu, desktop })
        {
            try { if (File.Exists(lnk)) File.Delete(lnk); }
            catch { }
        }
    }

    private static void DeleteContents(string dir)
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

    private static void ScheduleFolderRemoval(string dir)
    {
        try
        {
            Process.Start(new ProcessStartInfo("cmd.exe",
                $"/c timeout /t 2 /nobreak >nul & rd /s /q \"{dir}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false
            });
        }
        catch { }
    }

    public async Task InstallAsync(string targetDir, bool desktopShortcut,
        bool darkTheme, string language,
        IProgress<(string Stage, double Percent)> progress, CancellationToken ct,
        bool writeSettings = true)
    {
        Directory.CreateDirectory(targetDir);

        progress.Report(("extracting", 5));
        await Task.Run(() => ExtractPayload(targetDir, progress), ct).ConfigureAwait(false);

        progress.Report(("shortcuts", 88));
        CreateShortcuts(targetDir, desktopShortcut);

        progress.Report(("registering", 95));
        WriteUninstallInfo(targetDir);
        CopySelfAsUninstaller(targetDir);

        if (writeSettings) WriteAppSettings(darkTheme, language);

        progress.Report(("done", 100));
    }

    public static bool HasDesktopShortcut => File.Exists(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory), $"{AppName}.lnk"));

    private static string AppSettingsFile => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppName, "settings.json");

    public static (bool Dark, string Language) ReadAppSettings()
    {
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(AppSettingsFile));
            var root = json.RootElement;
            var theme = root.TryGetProperty("Theme", out var t) ? t.GetInt32() : 0;
            var lang = root.TryGetProperty("Language", out var l) ? l.GetString() : null;

            var dark = theme == 2 || (theme == 0 && SystemUsesDarkTheme());
            return (dark, lang == "en" ? "en" : "ru");
        }
        catch { return (false, "ru"); }
    }

    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int light && light == 0;
        }
        catch { return false; }
    }

    private static void ExtractPayload(string targetDir, IProgress<(string, double)> progress)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream("WinStart.Installer.payload.zip")
            ?? throw new InvalidOperationException("Полезная нагрузка не встроена в установщик (payload.zip).");

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var total = archive.Entries.Count;
        var i = 0;

        foreach (var entry in archive.Entries)
        {
            i++;
            if (entry.FullName.EndsWith('/')) continue;

            var dest = Path.Combine(targetDir, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);

            progress.Report(("extracting", 5 + 80.0 * i / Math.Max(total, 1)));
        }
    }

    private static void CreateShortcuts(string targetDir, bool desktop)
    {
        var exe = Path.Combine(targetDir, ExeName);

        var startMenu = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs");
        Directory.CreateDirectory(startMenu);
        CreateShortcut(Path.Combine(startMenu, $"{AppName}.lnk"), exe, targetDir);

        if (desktop)
        {
            var desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            CreateShortcut(Path.Combine(desktopDir, $"{AppName}.lnk"), exe, targetDir);
        }
    }

    private static void CreateShortcut(string linkPath, string targetExe, string workingDir)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType is null) return;

            dynamic shell = Activator.CreateInstance(shellType)!;
            var link = shell.CreateShortcut(linkPath);
            link.TargetPath = targetExe;
            link.WorkingDirectory = workingDir;
            link.IconLocation = targetExe + ",0";
            link.Description = AppName;
            link.Save();
        }
        catch { }
    }

    private static void WriteAppSettings(bool darkTheme, string language)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppSettingsFile)!);

            var theme = darkTheme ? 2 : 1;
            var lang = language == "en" ? "en" : "ru";
            var json = $"{{\r\n  \"Theme\": {theme},\r\n  \"Language\": \"{lang}\",\r\n  \"ShowSplash\": true\r\n}}";

            File.WriteAllText(AppSettingsFile, json);
        }
        catch { }
    }

    private static void WriteUninstallInfo(string targetDir)
    {
        var version = typeof(InstallService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        using var key = Registry.LocalMachine.CreateSubKey(UninstallKey, true);
        key.SetValue("DisplayName", AppName);
        key.SetValue("DisplayVersion", version);
        key.SetValue("Publisher", AppName);
        key.SetValue("InstallLocation", targetDir);
        key.SetValue("DisplayIcon", Path.Combine(targetDir, ExeName));
        key.SetValue("UninstallString", $"\"{Path.Combine(targetDir, UninstallerName)}\"");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
        key.SetValue("EstimatedSize", DirSizeKb(targetDir), RegistryValueKind.DWord);
    }

    private static void CopySelfAsUninstaller(string targetDir)
    {
        try
        {
            var self = Environment.ProcessPath;
            if (self is null) return;

            var dest = Path.Combine(targetDir, UninstallerName);
            if (!string.Equals(self, dest, StringComparison.OrdinalIgnoreCase))
                File.Copy(self, dest, true);
        }
        catch { }
    }

    private static int DirSizeKb(string dir)
    {
        try
        {
            long bytes = new DirectoryInfo(dir)
                .EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            return (int)(bytes / 1024);
        }
        catch { return 0; }
    }

    public static void Launch(string targetDir)
    {
        try
        {
            Process.Start(new ProcessStartInfo(Path.Combine(targetDir, ExeName)) { UseShellExecute = true });
        }
        catch { }
    }
}
