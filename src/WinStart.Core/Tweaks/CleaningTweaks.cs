using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class CleaningTweaks
{
    public static IEnumerable<TweakDefinition> All()
    {
        yield return EdgeUpdate();
        yield return WindowsUpdate();
        yield return NetworkCache();
        yield return StoreCache();
        yield return ShellBags();
        yield return IconCache();
        yield return DiskCleanup();
    }

    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string UserProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static TweakDefinition EdgeUpdate() => new()
    {
        Id = "cleaning.edgeUpdate",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 1,
        Reversible = false,
        ActionVerbKey = "action.clean",
        CustomApply = async (ctx, ct) =>
        {
            await RunWithSizeReportAsync(ctx, ct, async () =>
            {
                await ClearAsync(ctx, Path.Combine(ProgramFilesX86, @"Microsoft\EdgeUpdate\Download"), ct)
                    .ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    };

    private static TweakDefinition WindowsUpdate() => new()
    {
        Id = "cleaning.windowsUpdate",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 2,
        Reversible = false,
        ActionVerbKey = "action.clean",
        CustomApply = async (ctx, ct) =>
        {
            await RunWithSizeReportAsync(ctx, ct, async () =>
            {
                ctx.Progress(ctx.Text("log.wuStop"), null);
                await ctx.Process.RunAsync("net.exe", "stop wuauserv", ct, timeoutMs: 60000).ConfigureAwait(false);

                ctx.Progress(ctx.Text("log.cleanSoftwareDistribution"), null);
                await ClearAsync(ctx, Path.Combine(Win, "SoftwareDistribution"), ct).ConfigureAwait(false);

                ctx.Progress(ctx.Text("log.cleanDeliveryCache"), null);
                await ClearAsync(ctx, Path.Combine(Win,
                    @"ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
                    ct).ConfigureAwait(false);

                await RemoveAsync(ctx, Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Windows Defender Advanced Threat Protection"), ct).ConfigureAwait(false);

                await ctx.Process.RunAsync("net.exe", "start wuauserv", ct, timeoutMs: 60000).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    };

    private static TweakDefinition NetworkCache() => new()
    {
        Id = "cleaning.networkCache",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 3,
        Reversible = false,
        ActionVerbKey = "action.clean",
        CustomApply = async (ctx, ct) =>
        {
            await RunWithSizeReportAsync(ctx, ct, async () =>
            {
                await ClearAsync(ctx, Path.Combine(Win, @"System32\sru"), ct).ConfigureAwait(false);
                await ClearAsync(ctx, Path.Combine(UserProfile,
                    @"AppData\LocalLow\Microsoft\CryptnetUrlCache"), ct).ConfigureAwait(false);

                ctx.Progress(ctx.Text("log.flushDns"), null);
                await ctx.Process.RunAsync("ipconfig.exe", "/flushdns", ct, timeoutMs: 30000).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    };

    private static TweakDefinition StoreCache() => new()
    {
        Id = "cleaning.storeCache",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 4,
        Reversible = false,
        ActionVerbKey = "action.clean",
        CustomApply = async (ctx, ct) =>
        {
            await RunWithSizeReportAsync(ctx, ct, async () =>
            {
                await ctx.Process.RunAsync("taskkill.exe", "/f /t /im WinStore.App.exe", ct, timeoutMs: 20000)
                    .ConfigureAwait(false);

                ctx.Progress(ctx.Text("log.storeCacheReset"), null);
                await ctx.Process.RunAsync("wsreset.exe", "-i", ct, timeoutMs: 60000).ConfigureAwait(false);

                await ClearAsync(ctx, Path.Combine(LocalAppData,
                    @"Packages\Microsoft.WindowsStore_8wekyb3d8bbwe\LocalCache"), ct).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
    };

    private static TweakDefinition ShellBags() => new()
    {
        Id = "cleaning.shellBags",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 5,
        ConfirmKey = "confirm.shellBags",
        ActionVerbKey = "action.clean",
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            .. new[]
                {
                    @"HKCU\Software\Microsoft\Windows\Shell",
                    @"HKCU\Software\Microsoft\Windows\ShellNoRoam",
                    @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\Shell"
                }
                .SelectMany(root => new[] { "Bags", "BagMRU", "BagsMRU" }
                    .Select(name => RegistryAction.DeleteKey($@"{root}\{name}")))
        ]
    };

    private static TweakDefinition IconCache() => new()
    {
        Id = "cleaning.iconCache",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 6,
        ConfirmKey = "confirm.iconCache",
        ActionVerbKey = "action.clean",
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.DeleteKey(@"HKCU\Control Panel\NotifyIconSettings"),
            .. new[] { "IconStreams", "PastIconsStream", "PromotedIconCache" }
                .SelectMany(name => new[]
                {
                    RegistryAction.DeleteValue(
                        @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify",
                        name),
                    RegistryAction.DeleteValue(
                        @"HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotSIB",
                        name)
                })
        ],
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.iconCacheReset"), null);
            await ctx.Process.RunAsync("ie4uinit.exe", "-show", ct, timeoutMs: 30000).ConfigureAwait(false);
            await ctx.Process.RunAsync("ie4uinit.exe", "-ClearIconCache", ct, timeoutMs: 30000).ConfigureAwait(false);

            await ctx.Process.RunAsync("taskkill.exe", "/f /im explorer.exe", ct, timeoutMs: 20000)
                .ConfigureAwait(false);
            await Task.Delay(1000, ct).ConfigureAwait(false);

            foreach (var pattern in new[] { "IconCache*", "thumbcache*" })
            {
                DeleteByPattern(ctx, Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer"), pattern);
                DeleteByPattern(ctx, LocalAppData, pattern);
            }

            DeleteByPattern(ctx, Path.Combine(LocalAppData, @"Microsoft\Windows\Explorer\IconCacheToDelete"), "*.tmp");
        }
    };

    private static TweakDefinition DiskCleanup() => new()
    {
        Id = "cleaning.diskCleanup",
        Category = TweakCategory.Cleaning,
        Kind = TweakKind.Action,
        Order = 7,
        Reversible = false,
        ActionVerbKey = "action.open",
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.diskCleanupStart"), null);
            await ctx.Process.LaunchAsync(
                Path.Combine(Environment.SystemDirectory, "cleanmgr.exe"), "", false, ct).ConfigureAwait(false);
            ctx.Log(ctx.Text("log.diskCleanupOpened"));
        }
    };

    // ------------------------------------------------------------- Вспомогательное

    private static async Task RunWithSizeReportAsync(ITweakContext ctx, CancellationToken ct, Func<Task> work)
    {
        var drive = new DriveInfo(Path.GetPathRoot(Environment.SystemDirectory) ?? @"C:\");
        var before = SafeFreeSpace(drive);

        await work().ConfigureAwait(false);

        var freed = SafeFreeSpace(drive) - before;
        ctx.Progress(freed > 0
            ? ctx.Text("log.freedMb", $"{freed / 1024d / 1024:0.#}")
            : ctx.Text("log.cleanDone"), 100);
    }

    private static long SafeFreeSpace(DriveInfo drive)
    {
        try { return drive.AvailableFreeSpace; }
        catch { return 0; }
    }

    private static async Task ClearAsync(ITweakContext ctx, string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder))
        {
            ctx.Log(ctx.Text("log.skipNoFolder", folder));
            return;
        }

        var stubborn = false;

        foreach (var file in FileOps.Files(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (!FileOps.TryDeleteFile(file)) stubborn = true;
        }

        foreach (var dir in FileOps.Directories(folder))
        {
            ct.ThrowIfCancellationRequested();
            if (!FileOps.TryDeleteDirectory(dir)) stubborn = true;
        }

        if (stubborn)
        {
            ctx.Log(ctx.Text("log.retryTrustedInstaller", folder));
            await ctx.Process.TrustedInstallerAsync(
                $"del /f /s /q \"{folder}\\*\"", ct, 120000).ConfigureAwait(false);
        }

        ctx.Log(ctx.Text("log.cleared", folder));
    }

    private static async Task RemoveAsync(ITweakContext ctx, string folder, CancellationToken ct)
    {
        if (!Directory.Exists(folder)) return;

        if (!FileOps.TryDeleteDirectory(folder))
            await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{folder}\"", ct, 120000).ConfigureAwait(false);

        ctx.Log(ctx.Text("log.deleted", folder));
    }

    private static void DeleteByPattern(ITweakContext ctx, string folder, string pattern)
    {
        foreach (var file in FileOps.Files(folder, pattern))
            if (!FileOps.TryDeleteFile(file)) ctx.Log(ctx.Text("log.deleteFailed", file));
    }
}
