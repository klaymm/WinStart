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
            LogTail(ctx, r);
            ctx.Progress(ctx.Text(r.Ok ? "log.checkDone" : "log.checkIssues"), 100);
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
            LogTail(ctx, r);
            ctx.Progress(ctx.Text(r.Ok ? "log.checkDone" : "log.checkIssues"), 100);
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
            ctx.Progress(ctx.Text(r.Ok ? "log.checkDone" : "log.checkIssues"), 100);
        }
    };

    private static void LogTail(ITweakContext ctx, ProcessResult r)
    {
        var lines = (r.StdOut + "\n" + r.StdErr).Replace("\0", "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(l => l.Any(char.IsLetter))
            .ToList();
        foreach (var line in lines.TakeLast(25)) ctx.Log(line);
        ctx.Log($"exit code {r.ExitCode}");
    }
}
