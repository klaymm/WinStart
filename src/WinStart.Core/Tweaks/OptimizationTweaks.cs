using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class OptimizationTweaks
{
    private const string PowerKey = @"HKLM\System\CurrentControlSet\Control\Power";
    private const string ReserveKey = @"HKLM\Software\Microsoft\Windows\CurrentVersion\ReserveManager";
    private const string RestoreKey = @"HKLM\Software\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    public static IEnumerable<TweakDefinition> All()
    {
        yield return Hibernation();
        yield return ReservedStorage();
        yield return SystemRestore();
        yield return WinSxS();
        yield return Sfc();
        yield return Dism();
        yield return Chkdsk();
        yield return SvcHost();
        yield return DelayedAutostart();
        yield return EventLogSize();
        yield return UltimatePerformance();
        yield return StartupDelay();
        yield return FastFolders();
        yield return ThumbnailCache();
        yield return GameDvr();
    }

    private static TweakDefinition Hibernation() => new()
    {
        Id = "optimization.hibernation",
        Category = TweakCategory.Optimization,
        Order = 1,
        ManualRevertOnly = true,
        RegistryActions = [RegistryAction.Set(PowerKey, "HibernateEnabledDefault", 0)],
        RevertActions = [RegistryAction.Set(PowerKey, "HibernateEnabledDefault", 1)],
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.hibernateOff"), null);
            var r = await ctx.Process.RunAsync("powercfg.exe", "-h off", ct, timeoutMs: 60000).ConfigureAwait(false);
            ctx.Log($"powercfg -h off → {r.ExitCode}");
        },
        CustomRevert = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.hibernateOn"), null);
            var r = await ctx.Process.RunAsync("powercfg.exe", "-h on", ct, timeoutMs: 60000).ConfigureAwait(false);
            ctx.Log($"powercfg -h on → {r.ExitCode}");
            ctx.Registry.DeleteValue(PowerKey, "HibernateEnabled");
        }
    };

    private static TweakDefinition ReservedStorage() => new()
    {
        Id = "optimization.reservedStorage",
        Category = TweakCategory.Optimization,
        Order = 2,
        MinBuild = 18362,
        CustomDetect = ctx => ctx.Registry.ValueExists(ReserveKey, "MiscPolicyInfo")
            ? TweakState.Applied
            : TweakState.NotApplied,
        CustomApply = async (ctx, ct) =>
        {
            ctx.Backup(ReserveKey, "MiscPolicyInfo");
            ctx.Progress(ctx.Text("log.reservedOff"), null);

            var r = await ctx.Process.RunAsync("dism.exe",
                "/Online /Set-ReservedStorageState /State:Disabled", ct, timeoutMs: 180000).ConfigureAwait(false);

            ctx.Log($"DISM → {r.ExitCode}");
            if (!r.Ok && r.StdOut.Length > 0) ctx.Log(r.StdOut);
        },
        CustomRevert = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.reservedOn"), null);

            var r = await ctx.Process.RunAsync("dism.exe",
                "/Online /Set-ReservedStorageState /State:Enabled", ct, timeoutMs: 180000).ConfigureAwait(false);

            ctx.Log($"DISM → {r.ExitCode}");
            ctx.Registry.DeleteValue(ReserveKey, "MiscPolicyInfo");
        }
    };

    private static TweakDefinition SystemRestore() => new()
    {
        Id = "optimization.systemRestore",
        Category = TweakCategory.Optimization,
        Order = 3,
        ConfirmKey = "confirm.systemRestore",
        CustomDetect = ctx => ctx.Registry.ValueExists(RestoreKey, "RPSessionInterval")
            ? TweakState.NotApplied
            : TweakState.Applied,
        CustomApply = async (ctx, ct) =>
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";
            ctx.Backup(RestoreKey, "RPSessionInterval");

            ctx.Progress(ctx.Text("log.restoreOff"), null);
            await ctx.Process.PowerShellAsync(
                $"Disable-ComputerRestore -Drive '{drive}'", ct, 120000).ConfigureAwait(false);

            await ctx.Process.RunAsync("vssadmin.exe",
                $"resize shadowstorage /on={drive} /for={drive} /maxsize=1%", ct, timeoutMs: 60000)
                .ConfigureAwait(false);

            ctx.Progress(ctx.Text("log.restoreDeleting"), null);
            await ctx.Process.RunAsync("vssadmin.exe", "delete shadows /all /quiet", ct, timeoutMs: 120000)
                .ConfigureAwait(false);

            ctx.Registry.DeleteValue(RestoreKey, "RPSessionInterval");
            ctx.Log(ctx.Text("log.restoreDone"));
        },
        CustomRevert = async (ctx, ct) =>
        {
            var drive = Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\";

            ctx.Progress(ctx.Text("log.restoreOn"), null);
            await ctx.Process.PowerShellAsync(
                $"Enable-ComputerRestore -Drive '{drive}'", ct, 120000).ConfigureAwait(false);

            await ctx.Process.RunAsync("vssadmin.exe",
                $"resize shadowstorage /on={drive} /for={drive} /maxsize=10%", ct, timeoutMs: 60000)
                .ConfigureAwait(false);

            ctx.Log(ctx.Text("log.restoreOnDone"));
        }
    };

    private static TweakDefinition WinSxS() => new()
    {
        Id = "optimization.winSxS",
        Category = TweakCategory.Optimization,
        Kind = TweakKind.Action,
        Order = 4,
        Destructive = true,
        Reversible = false,
        ConfirmKey = "confirm.winSxS",
        ActionVerbKey = "action.clean",
        CustomApply = async (ctx, ct) =>
        {
            var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
            long before = 0;
            try { before = drive.AvailableFreeSpace; } catch { }

            ctx.Progress(ctx.Text("log.winSxS"), null);
            var r = await ctx.Process.RunAsync("dism.exe",
                "/online /Cleanup-Image /StartComponentCleanup /ResetBase", ct, timeoutMs: 3_600_000)
                .ConfigureAwait(false);

            ctx.Log($"DISM → {r.ExitCode}");

            var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var root = Path.GetPathRoot(win) ?? @"C:\";

            ctx.Progress(ctx.Text("log.winSxSLeftovers"), null);
            foreach (var pattern in new[]
                     {
                         "amd64_microsoft-edge-webview*",
                         "amd64_microsoft-windows-onedrive-setup*"
                     })
            {
                foreach (var dir in FileOps.Directories(Path.Combine(win, "WinSxS"), pattern))
                    await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{dir}\"", ct, 120000).ConfigureAwait(false);
            }

            foreach (var folder in new[] { Path.Combine(root, "Inetpub"), Path.Combine(root, "PerfLogs") })
            {
                if (Directory.Exists(folder))
                    await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{folder}\"", ct, 120000)
                        .ConfigureAwait(false);
            }

            long after = before;
            try { after = drive.AvailableFreeSpace; } catch { }

            var freed = after - before;
            ctx.Progress(freed > 0
                ? ctx.Text("log.freedGb", $"{freed / 1024d / 1024 / 1024:0.##}")
                : ctx.Text("log.cleanDone"), 100);
        }
    };

    private static TweakDefinition Sfc() => new()
    {
        Id = "optimization.sfc",
        Category = TweakCategory.Optimization,
        Kind = TweakKind.Action,
        Order = 5,
        Reversible = false,
        ActionVerbKey = "action.run",
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.sfc"), null);
            var r = await ctx.Process.RunAsync("sfc.exe", "/scannow", ct, timeoutMs: 3_600_000).ConfigureAwait(false);
            var (verdict, details) = Verdict(LogTail(ctx, r));
            ctx.Progress(null, 100);
            ctx.Result(verdict ?? ctx.Text(r.Ok ? "log.checkDone" : "log.checkIssues"), !r.Ok || details > 0);
        }
    };

    private static TweakDefinition Dism() => new()
    {
        Id = "optimization.dism",
        Category = TweakCategory.Optimization,
        Kind = TweakKind.Action,
        Order = 6,
        Reversible = false,
        RequiresNetwork = true,
        ActionVerbKey = "action.run",
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.dismRestore"), null);
            var r = await ctx.Process.RunAsync("dism.exe", "/Online /Cleanup-Image /RestoreHealth", ct, timeoutMs: 3_600_000)
                .ConfigureAwait(false);
            var (verdict, _) = Verdict(LogTail(ctx, r));
            ctx.Progress(null, 100);
            ctx.Result(verdict ?? ctx.Text(r.Ok ? "log.checkDone" : "log.checkIssues"), !r.Ok);
        }
    };

    private static TweakDefinition Chkdsk() => new()
    {
        Id = "optimization.chkdsk",
        Category = TweakCategory.Optimization,
        Kind = TweakKind.Action,
        Order = 7,
        Reversible = false,
        ActionVerbKey = "action.run",
        CustomApply = async (ctx, ct) =>
        {
            var drive = (Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\").TrimEnd('\\');
            ctx.Progress(ctx.Text("log.chkdsk", drive), null);
            var r = await ctx.Process.RunAsync("chkdsk.exe", $"{drive} /scan", ct, timeoutMs: 3_600_000).ConfigureAwait(false);
            LogTail(ctx, r);
            ctx.Progress(null, 100);
            switch (r.ExitCode)
            {
                case 0 or 2: ctx.Result(ctx.Text("result.chkdsk.ok", drive), false); break;
                case 1: ctx.Result(ctx.Text("result.chkdsk.fixed", drive), false); break;
                default: ctx.Result(ctx.Text("result.chkdsk.issues", drive), true); break;
            }
        }
    };

    private const string ControlKey = @"HKLM\System\CurrentControlSet\Control";
    private const string SvcHostValue = "SvcHostSplitThresholdInKB";
    private const string SvcHostBackup = "SvcHostSplitThresholdInKB_orig";

    private static TweakDefinition SvcHost() => new()
    {
        Id = "optimization.svchost",
        Category = TweakCategory.Optimization,
        Order = 8,
        CustomDetect = ctx => ctx.Registry.ValueExists(ControlKey, SvcHostBackup) ? TweakState.Applied : TweakState.NotApplied,
        CustomApply = async (ctx, ct) =>
        {
            if (ctx.Registry.GetInt(ControlKey, SvcHostValue) is not { } threshold)
            {
                ctx.Log(ctx.Text("log.svchostMissing"));
                return;
            }

            ctx.Backup(ControlKey, SvcHostValue);
            ctx.Backup(ControlKey, SvcHostBackup);
            ctx.Progress(ctx.Text("log.svchost"), null);

            var memory = await TotalMemoryKbAsync(ctx, ct).ConfigureAwait(false);
            ctx.Registry.SetValue(ControlKey, SvcHostBackup, threshold, RegistryValueKind.DWord);
            ctx.Registry.SetValue(ControlKey, SvcHostValue, memory, RegistryValueKind.DWord);
        },
        CustomRevert = (ctx, ct) =>
        {
            if (ctx.Registry.GetInt(ControlKey, SvcHostBackup) is { } original)
            {
                ctx.Registry.SetValue(ControlKey, SvcHostValue, original, RegistryValueKind.DWord);
                ctx.Registry.DeleteValue(ControlKey, SvcHostBackup);
            }
            return Task.CompletedTask;
        }
    };

    /// <summary>Порог, выше которого службы не разделяются на отдельные процессы: объём памяти с запасом.</summary>
    private static async Task<int> TotalMemoryKbAsync(ITweakContext ctx, CancellationToken ct)
    {
        var r = await ctx.Process.PowerShellAsync(
            "(Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize", ct, 60000).ConfigureAwait(false);

        return int.TryParse(r.StdOut.Trim(), out var kb) && kb > 0
            ? (int)Math.Min(int.MaxValue, (long)kb + 1024000)
            : 16777216;
    }

    private static TweakDefinition DelayedAutostart()
    {
        string Service(string name) => $@"HKLM\System\CurrentControlSet\Services\{name}";

        return new TweakDefinition
        {
            Id = "optimization.delayedStart",
            Category = TweakCategory.Optimization,
            Order = 9,
            RegistryActions =
            [
                RegistryAction.Set(Service("EventSystem"), "DelayedAutostart", 1),
                RegistryAction.Set(Service("NlaSvc"), "DelayedAutostart", 1)
            ]
        };
    }

    private const string EventChannels = @"HKLM\Software\Microsoft\Windows\CurrentVersion\WINEVT\Channels";
    private const int EventLogMaxSize = 0x40000;

    private static TweakDefinition EventLogSize() => new()
    {
        Id = "optimization.eventLogSize",
        Category = TweakCategory.Optimization,
        Order = 10,
        ManualRevertOnly = true,
        CustomDetect = ctx =>
        {
            var channel = $@"{EventChannels}\Application";
            return ctx.Registry.ValueExists(channel, "MaxSize_old") ? TweakState.Applied : TweakState.NotApplied;
        },
        CustomApply = (ctx, ct) =>
        {
            var changed = 0;
            foreach (var name in ctx.Registry.SubKeys(EventChannels))
            {
                ct.ThrowIfCancellationRequested();
                var channel = $@"{EventChannels}\{name}";
                if (ctx.Registry.ValueExists(channel, "MaxSize_old")) continue;

                var size = ctx.Registry.GetInt(channel, "MaxSize");
                ctx.Registry.SetValue(channel, "MaxSize_old", size ?? 0, RegistryValueKind.DWord);
                ctx.Registry.SetValue(channel, "MaxSize", EventLogMaxSize, RegistryValueKind.DWord);
                changed++;
            }

            ctx.Progress(ctx.Text("log.eventLogs", changed), 100);
            return Task.CompletedTask;
        },
        CustomRevert = (ctx, ct) =>
        {
            foreach (var name in ctx.Registry.SubKeys(EventChannels))
            {
                ct.ThrowIfCancellationRequested();
                var channel = $@"{EventChannels}\{name}";
                if (ctx.Registry.GetInt(channel, "MaxSize_old") is not { } original) continue;

                if (original > 0) ctx.Registry.SetValue(channel, "MaxSize", original, RegistryValueKind.DWord);
                else ctx.Registry.DeleteValue(channel, "MaxSize");
                ctx.Registry.DeleteValue(channel, "MaxSize_old");
            }
            return Task.CompletedTask;
        }
    };

    private const string UltimateScheme = "e9a42b02-d5df-448d-aa00-03f14749eb61";
    private const string BalancedScheme = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private static TweakDefinition UltimatePerformance() => new()
    {
        Id = "optimization.ultimatePerformance",
        Category = TweakCategory.Optimization,
        Order = 11,
        ManualRevertOnly = true,
        CustomDetect = ctx =>
            ctx.Registry.GetString(@"HKLM\System\CurrentControlSet\Control\Power\User\PowerSchemes", "ActivePowerScheme")
                ?.Contains(UltimateScheme, StringComparison.OrdinalIgnoreCase) == true
                ? TweakState.Applied
                : TweakState.NotApplied,
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.powerScheme"), null);
            await ctx.Process.RunAsync("powercfg.exe", $"-duplicatescheme {UltimateScheme}", ct, timeoutMs: 60000)
                .ConfigureAwait(false);

            var r = await ctx.Process.RunAsync("powercfg.exe", $"/setactive {UltimateScheme}", ct, timeoutMs: 60000)
                .ConfigureAwait(false);
            ctx.Log($"powercfg /setactive → {r.ExitCode}");
        },
        CustomRevert = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.powerSchemeBack"), null);
            var r = await ctx.Process.RunAsync("powercfg.exe", $"/setactive {BalancedScheme}", ct, timeoutMs: 60000)
                .ConfigureAwait(false);
            ctx.Log($"powercfg /setactive → {r.ExitCode}");
        }
    };

    private static TweakDefinition StartupDelay()
    {
        const string user = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";
        const string machine = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize";

        return new TweakDefinition
        {
            Id = "optimization.startupDelay",
            Category = TweakCategory.Optimization,
            Order = 12,
            RegistryActions =
            [
                RegistryAction.Set(user, "StartupDelayInMSec", 0),
                RegistryAction.Set(user, "WaitforIdleState", 0),
                RegistryAction.Set(machine, "StartupDelayInMSec", 0),
                RegistryAction.Set(machine, "WaitforIdleState", 0)
            ]
        };
    }

    private static TweakDefinition FastFolders()
    {
        const string bags = @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell\Bags\AllFolders\Shell";
        string Media(string kind, string command) => $@"HKCR\SystemFileAssociations\{kind}\shell\{command}";

        return new TweakDefinition
        {
            Id = "optimization.fastFolders",
            Category = TweakCategory.Optimization,
            Order = 13,
            NeedsExplorerRestart = true,
            RegistryActions =
            [
                RegistryAction.SetString(bags, "FolderType", "NotSpecified"),
                .. new[] { "Directory.Audio", "Directory.Image", "Directory.Video" }
                    .SelectMany(kind => new[] { "Enqueue", "Play" }
                        .Select(command => RegistryAction.SetString(Media(kind, command), "LegacyDisable", "")))
            ]
        };
    }

    private static TweakDefinition ThumbnailCache() => new()
    {
        Id = "optimization.thumbnailCache",
        Category = TweakCategory.Optimization,
        Order = 14,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.SetString(@"HKLM\Software\Microsoft\Windows\CurrentVersion\Explorer", "MaxCachedIcons", "4096")
        ]
    };

    private static TweakDefinition GameDvr() => new()
    {
        Id = "optimization.gameDvr",
        Category = TweakCategory.Optimization,
        Order = 15,
        RegistryActions =
        [
            RegistryAction.Set(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 0),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 0),
            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0),
            RegistryAction.Set(@"HKLM\Software\Microsoft\PolicyManager\default\ApplicationManagement\AllowGameDVR", "Value", 0),
            RegistryAction.Set(@"HKLM\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AllowGameDVR", 0)
        ],
        RevertActions =
        [
            RegistryAction.Set(@"HKCU\System\GameConfigStore", "GameDVR_Enabled", 1),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", 1),
            RegistryAction.Set(@"HKLM\Software\Microsoft\PolicyManager\default\ApplicationManagement\AllowGameDVR", "Value", 1),
            RegistryAction.Set(@"HKLM\Software\Microsoft\Windows\CurrentVersion\GameDVR", "AllowGameDVR", 1)
        ]
    };

    private static List<string> LogTail(ITweakContext ctx, ProcessResult r)
    {
        var lines = (r.StdOut + "\n" + r.StdErr).Replace("\0", "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        foreach (var line in lines.Where(l => l.Any(char.IsLetter) && !l.Contains('%')).TakeLast(25)) ctx.Log(line);
        ctx.Log($"exit code {r.ExitCode}");
        return lines;
    }

    private static (string? Verdict, int Details) Verdict(List<string> lines)
    {
        var lastProgress = lines.FindLastIndex(l => l.Contains('%'));
        var text = lines.Where(l => l.Any(char.IsLetter) && !l.Contains('%')).ToList();
        if (lastProgress < 0) return (text.LastOrDefault(), 0);

        var tail = lines.Skip(lastProgress + 1).Where(l => l.Any(char.IsLetter)).ToList();
        return (tail.FirstOrDefault(), Math.Max(0, tail.Count - 1));
    }
}
