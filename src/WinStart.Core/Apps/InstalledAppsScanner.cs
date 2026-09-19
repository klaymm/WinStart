using System.Diagnostics;
using Microsoft.Win32;
using WinStart.Core.Services;

namespace WinStart.Core.Apps;

public sealed record FoundApp(string Id, string ExePath, bool Portable);

public interface IInstalledAppsScanner
{
    Task<IReadOnlyDictionary<string, FoundApp>> ScanAsync(CancellationToken ct);
}

public sealed class InstalledAppsScanner : IInstalledAppsScanner
{
    private const int MaxFolders = 20000;
    private static readonly TimeSpan MaxWalkTime = TimeSpan.FromSeconds(6);

    private static readonly string[] InstallRoots =
    [
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft"),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
    ];

    private static readonly string[] FolderHints =
        ["portable", "programs", "programm", "apps", "soft", "tools", "utils", "games", "программы", "софт"];

    public Task<IReadOnlyDictionary<string, FoundApp>> ScanAsync(CancellationToken ct) => Task.Run(() =>
    {
        var signatures = WingetCatalog.Apps
            .Where(a => a.Exes.Count > 0)
            .SelectMany(a => a.Exes.Select(e => (Exe: e, App: a)))
            .ToLookup(x => x.Exe, x => x.App, StringComparer.OrdinalIgnoreCase);

        var drives = LocalDrives();
        var found = new Dictionary<string, FoundApp>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Candidates(drives, ct))
        {
            ct.ThrowIfCancellationRequested();
            if (!seen.Add(path)) continue;

            var name = Path.GetFileName(path);
            if (!signatures.Contains(name) || IsJunkLocation(path)) continue;

            string? company = null;
            var companyRead = false;

            foreach (var app in signatures[name])
            {
                if (found.ContainsKey(app.Id)) continue;

                if (app.Company.Length > 0)
                {
                    if (!companyRead) { company = Company(path); companyRead = true; }
                    if (company is null || !company.Contains(app.Company, StringComparison.OrdinalIgnoreCase)) continue;
                }

                found[app.Id] = new FoundApp(app.Id, path, IsPortableLocation(path));
            }
        }

        return (IReadOnlyDictionary<string, FoundApp>)found;
    }, ct);

    private static IEnumerable<string> Candidates(HashSet<char> drives, CancellationToken ct)
    {
        Func<IEnumerable<string>>[] sources =
        [
            () => MuiCache(drives),
            () => Bam(drives),
            () => CompatibilityStore(drives),
            () => Shortcuts(drives),
            () => TypicalFolders(drives, ct)
        ];

        foreach (var source in sources)
            foreach (var path in Safe(source))
                yield return path;
    }

    private static List<string> Safe(Func<IEnumerable<string>> source)
    {
        var result = new List<string>();
        try
        {
            foreach (var path in source()) result.Add(path);
        }
        catch (OperationCanceledException) { throw; }
        catch { }
        return result;
    }

    private static IEnumerable<string> MuiCache(HashSet<char> drives)
    {
        const string path = @"Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\MuiCache";
        using var key = TryOpen(Registry.CurrentUser, path);
        if (key is null) yield break;

        foreach (var value in key.GetValueNames())
        {
            var idx = value.IndexOf(".exe.", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) continue;
            var file = value[..(idx + 4)];
            if (Exists(file, drives)) yield return Path.GetFullPath(file);
        }
    }

    private static IEnumerable<string> Bam(HashSet<char> drives)
    {
        var sid = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value;
        if (sid is null) yield break;

        using var key = TryOpen(Registry.LocalMachine, $@"SYSTEM\CurrentControlSet\Services\bam\State\UserSettings\{sid}");
        if (key is null) yield break;

        var volumes = VolumeMap(drives);
        foreach (var value in key.GetValueNames())
        {
            if (!value.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase)) continue;
            var file = DeviceToDrive(value, volumes);
            if (file is not null && Exists(file, drives)) yield return file;
        }
    }

    private static IEnumerable<string> CompatibilityStore(HashSet<char> drives)
    {
        const string path = @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Compatibility Assistant\Store";
        using var key = TryOpen(Registry.CurrentUser, path);
        if (key is null) yield break;

        foreach (var value in key.GetValueNames())
            if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && Exists(value, drives))
                yield return Path.GetFullPath(value);
    }

    private static IEnumerable<string> Shortcuts(HashSet<char> drives)
    {
        var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string[] folders =
        [
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.Combine(roaming, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar")
        ];

        var type = Type.GetTypeFromProgID("WScript.Shell");
        if (type is null) yield break;
        var shell = Activator.CreateInstance(type);
        if (shell is null) yield break;

        foreach (var folder in folders)
        {
            foreach (var lnk in FileOps.Files(folder, "*.lnk", recursive: true))
            {
                var target = ShortcutTarget(shell, lnk);
                if (target is not null && Exists(target, drives)) yield return Path.GetFullPath(target);
            }
        }
    }

    private static string? ShortcutTarget(object shell, string lnk)
    {
        try
        {
            dynamic s = shell;
            dynamic shortcut = s.CreateShortcut(lnk);
            string target = shortcut.TargetPath ?? "";
            return target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? target : null;
        }
        catch { return null; }
    }

    private static IEnumerable<string> TypicalFolders(HashSet<char> drives, CancellationToken ct)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var roots = new List<string>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Path.Combine(profile, "Downloads"),
            Path.Combine(profile, "Documents")
        };

        foreach (var letter in drives)
        {
            var root = $@"{letter}:\";
            roots.Add(root);
            foreach (var dir in FileOps.Directories(root))
                if (FolderHints.Any(h => Path.GetFileName(dir).Contains(h, StringComparison.OrdinalIgnoreCase)))
                    roots.Add(dir);
        }

        var budget = new WalkBudget();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsLocal(root, drives)) continue;
            foreach (var file in Walk(root, 3, budget, ct)) yield return file;
            if (budget.Exhausted) yield break;
        }
    }

    private static IEnumerable<string> Walk(string dir, int depth, WalkBudget budget, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth < 0 || !budget.Take() || IsSystemFolder(dir)) yield break;

        foreach (var file in FileOps.Files(dir, "*.exe")) yield return file;

        if (depth == 0) yield break;
        foreach (var sub in FileOps.Directories(dir))
        {
            if (IsReparsePoint(sub)) continue;
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || name.StartsWith('$') || name.Equals("Windows", StringComparison.OrdinalIgnoreCase)) continue;
            foreach (var file in Walk(sub, depth - 1, budget, ct)) yield return file;
            if (budget.Exhausted) yield break;
        }
    }

    private sealed class WalkBudget
    {
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private int _folders;

        public bool Exhausted => _folders >= MaxFolders || _clock.Elapsed > MaxWalkTime;

        public bool Take()
        {
            if (Exhausted) return false;
            _folders++;
            return true;
        }
    }

    private static bool IsReparsePoint(string dir)
    {
        try { return new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint); }
        catch { return true; }
    }

    private static bool IsSystemFolder(string dir) =>
        InstallRoots.Any(r => r.Length > 0 && dir.StartsWith(r, StringComparison.OrdinalIgnoreCase))
        || dir.Contains(@"\AppData\Local\Temp", StringComparison.OrdinalIgnoreCase);

    private static bool IsPortableLocation(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        return !InstallRoots.Any(r => r.Length > 0 && dir.StartsWith(r, StringComparison.OrdinalIgnoreCase))
               && !dir.Contains(@"\AppData\Local\", StringComparison.OrdinalIgnoreCase)
               && !dir.Contains(@"\AppData\Roaming\", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsJunkLocation(string path)
    {
        var dir = Path.GetDirectoryName(path) ?? "";
        return dir.Contains(@"\Temp\", StringComparison.OrdinalIgnoreCase)
               || dir.EndsWith(@"\Temp", StringComparison.OrdinalIgnoreCase)
               || dir.Contains(@"\$Recycle.Bin\", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Company(string file)
    {
        try { return FileVersionInfo.GetVersionInfo(file).CompanyName?.Trim(); }
        catch { return null; }
    }

    private static HashSet<char> LocalDrives()
    {
        var set = new HashSet<char>();
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType == DriveType.Fixed && drive.IsReady)
                        set.Add(char.ToUpperInvariant(drive.Name[0]));
                }
                catch { }
            }
        }
        catch { }
        return set;
    }

    private static bool IsLocal(string path, HashSet<char> drives) =>
        path.Length >= 3 && path[1] == ':' && path[2] == '\\' && drives.Contains(char.ToUpperInvariant(path[0]));

    private static bool Exists(string path, HashSet<char> drives)
    {
        try { return IsLocal(path, drives) && File.Exists(path); }
        catch { return false; }
    }

    private static RegistryKey? TryOpen(RegistryKey root, string path)
    {
        try { return root.OpenSubKey(path); }
        catch { return null; }
    }

    private static Dictionary<string, string> VolumeMap(HashSet<char> drives)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var letter in drives)
        {
            var target = NativeMethods.QueryDosDevice($"{letter}:");
            if (target is not null) map[target] = $"{letter}:";
        }
        return map;
    }

    private static string? DeviceToDrive(string devicePath, Dictionary<string, string> volumes)
    {
        foreach (var (device, letter) in volumes)
            if (devicePath.StartsWith(device + "\\", StringComparison.OrdinalIgnoreCase))
                return letter + devicePath[device.Length..];
        return null;
    }
}
