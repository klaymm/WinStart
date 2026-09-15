using System.Diagnostics;
using System.Text.Json;
using Microsoft.Win32;
using WinStart.Core.Abstractions;

namespace WinStart.Core.Startup;

public enum StartupKind { Registry, Folder, Task, Service, Winlogon }

public sealed record StartupEntry(
    string Id,
    StartupKind Kind,
    string Name,
    string Command,
    string Location,
    string? Publisher,
    string? FilePath,
    bool Enabled,
    bool CanToggle,
    bool IsMicrosoft);

public interface IStartupService
{
    Task<IReadOnlyList<StartupEntry>> ScanAsync(CancellationToken ct);

    Task<string?> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct);
}

public sealed class StartupService(IProcessRunner process) : IStartupService
{
    private const string RunPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOncePath = @"Software\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string Run32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string RunOnce32Path = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ApprovedPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly string WindowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    public async Task<IReadOnlyList<StartupEntry>> ScanAsync(CancellationToken ct)
    {
        var list = new List<StartupEntry>();

        await Task.Run(() =>
        {
            ScanRegistry(list);
            ScanFolders(list);
            ScanWinlogon(list);
        }, ct).ConfigureAwait(false);

        list.AddRange(await ScanTasksAsync(ct).ConfigureAwait(false));
        list.AddRange(await ScanServicesAsync(ct).ConfigureAwait(false));
        return list;
    }

    public async Task<string?> SetEnabledAsync(StartupEntry entry, bool enabled, CancellationToken ct)
    {
        try
        {
            switch (entry.Kind)
            {
                case StartupKind.Registry:
                case StartupKind.Folder:
                    SetApproved(entry, enabled);
                    return null;

                case StartupKind.Task:
                {
                    var (path, name) = SplitTask(entry.Location);
                    var verb = enabled ? "Enable-ScheduledTask" : "Disable-ScheduledTask";
                    var r = await process.PowerShellAsync(
                        $"{verb} -TaskPath '{Ps(path)}' -TaskName '{Ps(name)}' -ErrorAction Stop | Out-Null", ct, 60000)
                        .ConfigureAwait(false);
                    return r.Ok ? null : Error(r);
                }

                case StartupKind.Service:
                {
                    var type = enabled ? "Automatic" : "Disabled";
                    var r = await process.PowerShellAsync(
                        $"Set-Service -Name '{Ps(entry.Location)}' -StartupType {type} -ErrorAction Stop", ct, 60000)
                        .ConfigureAwait(false);
                    return r.Ok ? null : Error(r);
                }

                default:
                    return "unsupported";
            }
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    // ---------------------------------------------------------------- реестр Run / RunOnce

    private static void ScanRegistry(List<StartupEntry> list)
    {
        (RegistryKey Root, string Path, string ApprovedName, string Label)[] sources =
        [
            (Registry.CurrentUser, RunPath, "Run", @"HKCU\...\Run"),
            (Registry.CurrentUser, RunOncePath, "Run", @"HKCU\...\RunOnce"),
            (Registry.LocalMachine, RunPath, "Run", @"HKLM\...\Run"),
            (Registry.LocalMachine, RunOncePath, "Run", @"HKLM\...\RunOnce"),
            (Registry.LocalMachine, Run32Path, "Run32", @"HKLM\...\WOW6432Node\Run"),
            (Registry.LocalMachine, RunOnce32Path, "Run32", @"HKLM\...\WOW6432Node\RunOnce")
        ];

        foreach (var (root, path, approvedName, label) in sources)
        {
            using var key = root.OpenSubKey(path);
            if (key is null) continue;

            using var approved = root.OpenSubKey($@"{ApprovedPath}\{approvedName}");
            foreach (var name in key.GetValueNames())
            {
                if (name.Length == 0) continue;
                var command = key.GetValue(name)?.ToString() ?? "";
                var file = ResolveExecutable(command);
                var id = $"reg|{(root == Registry.CurrentUser ? "HKCU" : "HKLM")}\\{path}|{name}";
                list.Add(new StartupEntry(id, StartupKind.Registry, name, command, label,
                    Publisher(file), file, IsApproved(approved, name), CanToggle: true, IsMicrosoftFile(file, null)));
            }
        }
    }

    private static bool IsApproved(RegistryKey? approved, string name)
    {
        if (approved?.GetValue(name) is byte[] { Length: > 0 } bytes) return bytes[0] != 3;
        return true;
    }

    private static void SetApproved(StartupEntry entry, bool enabled)
    {
        var (root, approvedName) = entry.Kind == StartupKind.Folder
            ? (entry.Id.Contains("|common|") ? Registry.LocalMachine : Registry.CurrentUser, "StartupFolder")
            : (entry.Id.StartsWith("reg|HKCU", StringComparison.Ordinal) ? Registry.CurrentUser : Registry.LocalMachine,
               entry.Id.Contains("WOW6432Node", StringComparison.OrdinalIgnoreCase) ? "Run32" : "Run");

        using var key = root.CreateSubKey($@"{ApprovedPath}\{approvedName}", writable: true)
                        ?? throw new InvalidOperationException(ApprovedPath);

        var valueName = entry.Kind == StartupKind.Folder ? entry.Id.Split('|', 3)[2] : entry.Name;

        var bytes = new byte[12];
        bytes[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.Now.ToFileTime()).CopyTo(bytes, 4);
        key.SetValue(valueName, bytes, RegistryValueKind.Binary);
    }

    // ---------------------------------------------------------------- папки автозагрузки

    private static void ScanFolders(List<StartupEntry> list)
    {
        (string Folder, RegistryKey Root, string Tag, string Label)[] folders =
        [
            (Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser, "user", "Startup (user)"),
            (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine, "common", "Startup (all users)")
        ];

        foreach (var (folder, root, tag, label) in folders)
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
            using var approved = root.OpenSubKey($@"{ApprovedPath}\StartupFolder");

            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;

                var target = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) : file;
                var exe = ResolveExecutable(target ?? file);
                list.Add(new StartupEntry($"folder|{tag}|{name}", StartupKind.Folder, Path.GetFileNameWithoutExtension(name),
                    target ?? file, label, Publisher(exe), exe ?? file, IsApproved(approved, name), CanToggle: true,
                    IsMicrosoftFile(exe, null)));
            }
        }
    }

    private static string? ShortcutTarget(string lnk)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null) return null;
            dynamic shell = Activator.CreateInstance(type)!;
            dynamic shortcut = shell.CreateShortcut(lnk);
            string target = shortcut.TargetPath ?? "";
            string args = shortcut.Arguments ?? "";
            return target.Length == 0 ? null : args.Length == 0 ? target : $"\"{target}\" {args}";
        }
        catch
        {
            return null;
        }
    }

    // ---------------------------------------------------------------- Winlogon (только чтение)

    private static void ScanWinlogon(List<StartupEntry> list)
    {
        const string path = @"Software\Microsoft\Windows NT\CurrentVersion\Winlogon";
        using var key = Registry.LocalMachine.OpenSubKey(path);
        if (key is null) return;

        foreach (var name in new[] { "Shell", "Userinit" })
        {
            var value = key.GetValue(name)?.ToString();
            if (string.IsNullOrWhiteSpace(value)) continue;

            var exe = ResolveExecutable(value);
            list.Add(new StartupEntry($"winlogon|{name}", StartupKind.Winlogon, name, value, @"HKLM\...\Winlogon",
                Publisher(exe), exe, Enabled: true, CanToggle: false, IsMicrosoftFile(exe, null)));
        }
    }

    // ---------------------------------------------------------------- планировщик

    private async Task<List<StartupEntry>> ScanTasksAsync(CancellationToken ct)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $tasks = Get-ScheduledTask | Where-Object {
              $_.Triggers | Where-Object { $_.CimClass.CimClassName -in 'MSFT_TaskLogonTrigger','MSFT_TaskBootTrigger' }
            }
            $rows = foreach ($t in $tasks) {
              $trigger = ($t.Triggers | ForEach-Object { $_.CimClass.CimClassName -replace 'MSFT_Task','' -replace 'Trigger','' }) -join ', '
              [pscustomobject]@{
                Name = [string]$t.TaskName
                Path = [string]$t.TaskPath
                State = [string]$t.State
                Author = [string]$t.Author
                Trigger = $trigger
                Action = (($t.Actions | ForEach-Object { ("$($_.Execute) $($_.Arguments)").Trim() }) -join ' ; ')
              }
            }
            $json = ConvertTo-Json -InputObject @($rows) -Compress -Depth 3
            'B64:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
            """;

        var rows = await RunJsonAsync<TaskRow>(script, ct).ConfigureAwait(false);
        var list = new List<StartupEntry>();
        foreach (var t in rows)
        {
            if (string.IsNullOrEmpty(t.Name) || string.IsNullOrEmpty(t.Action)) continue;
            var exe = ResolveExecutable(t.Action);
            var location = t.Path + t.Name;
            var microsoft = t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)
                            || IsMicrosoftFile(exe, t.Author);
            list.Add(new StartupEntry($"task|{location}", StartupKind.Task, t.Name, t.Action, location,
                Publisher(exe) ?? CleanAuthor(t.Author), exe,
                !t.State.Equals("Disabled", StringComparison.OrdinalIgnoreCase), CanToggle: true, microsoft));
        }
        return list;
    }

    private static (string Path, string Name) SplitTask(string location)
    {
        var idx = location.LastIndexOf('\\');
        return idx < 0 ? ("\\", location) : (location[..(idx + 1)], location[(idx + 1)..]);
    }

    private static string? CleanAuthor(string? author) =>
        string.IsNullOrWhiteSpace(author) || author.StartsWith("$(") ? null : author;

    // ---------------------------------------------------------------- службы

    private async Task<List<StartupEntry>> ScanServicesAsync(CancellationToken ct)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $rows = Get-CimInstance Win32_Service | Where-Object { $_.StartMode -eq 'Auto' } | ForEach-Object {
              [pscustomobject]@{
                Name = [string]$_.Name
                DisplayName = [string]$_.DisplayName
                PathName = [string]$_.PathName
                State = [string]$_.State
              }
            }
            $json = ConvertTo-Json -InputObject @($rows) -Compress -Depth 3
            'B64:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
            """;

        var rows = await RunJsonAsync<ServiceRow>(script, ct).ConfigureAwait(false);
        var list = new List<StartupEntry>();
        foreach (var s in rows)
        {
            if (string.IsNullOrEmpty(s.Name)) continue;
            var exe = ResolveExecutable(s.PathName);
            list.Add(new StartupEntry($"service|{s.Name}", StartupKind.Service,
                string.IsNullOrEmpty(s.DisplayName) ? s.Name : s.DisplayName, s.PathName, s.Name,
                Publisher(exe), exe, Enabled: true, CanToggle: true, IsMicrosoftFile(exe, null)));
        }
        return list;
    }

    // ---------------------------------------------------------------- общее

    private async Task<List<T>> RunJsonAsync<T>(string script, CancellationToken ct)
    {
        var r = await process.PowerShellAsync(script, ct, 180000).ConfigureAwait(false);
        var marker = r.StdOut.IndexOf("B64:", StringComparison.Ordinal);
        if (marker < 0) return [];
        try
        {
            var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(r.StdOut[(marker + 4)..].Trim()));
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static string Ps(string s) => s.Replace("'", "''");

    private static string Error(ProcessResult r)
    {
        var text = (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr).Trim();
        return text.Length == 0 ? $"exit code {r.ExitCode}" : text.Split('\n')[0].Trim();
    }

    public static string? ResolveExecutable(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        var text = Environment.ExpandEnvironmentVariables(command.Trim());

        string candidate;
        if (text.StartsWith('"'))
        {
            var end = text.IndexOf('"', 1);
            candidate = end > 1 ? text[1..end] : text.Trim('"');
        }
        else
        {
            var exe = text.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            candidate = exe >= 0 ? text[..(exe + 4)] : UnquotedPath(text);
        }

        candidate = candidate.Trim();
        if (candidate.Length == 0) return null;

        try
        {
            if (Path.IsPathRooted(candidate)) return File.Exists(candidate) ? Path.GetFullPath(candidate) : candidate;

            var name = Path.HasExtension(candidate) ? candidate : candidate + ".exe";
            foreach (var dir in new[] { Environment.SystemDirectory, WindowsDir }
                         .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
            {
                if (dir.Length == 0) continue;
                var full = Path.Combine(dir, name);
                if (File.Exists(full)) return full;
            }
        }
        catch { }

        return candidate;
    }

    private static string UnquotedPath(string text)
    {
        var parts = text.Split(' ');
        for (var i = parts.Length; i >= 1; i--)
        {
            var prefix = string.Join(' ', parts[..i]);
            try
            {
                if (File.Exists(prefix)) return prefix;
                if (File.Exists(prefix + ".exe")) return prefix + ".exe";
            }
            catch { }
        }
        return Path.IsPathRooted(text) ? text : parts[0];
    }

    private static string? Publisher(string? file)
    {
        if (file is null || !File.Exists(file)) return null;
        try
        {
            var company = FileVersionInfo.GetVersionInfo(file).CompanyName?.Trim();
            return string.IsNullOrEmpty(company) ? null : company;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsMicrosoftFile(string? file, string? author)
    {
        if (author?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true) return true;
        if (file is null) return false;
        if (file.StartsWith(WindowsDir, StringComparison.OrdinalIgnoreCase)) return true;
        return Publisher(file)?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
    }

    private sealed class TaskRow
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string State { get; set; } = "";
        public string? Author { get; set; }
        public string? Trigger { get; set; }
        public string Action { get; set; } = "";
    }

    private sealed class ServiceRow
    {
        public string Name { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string PathName { get; set; } = "";
        public string State { get; set; } = "";
    }
}
