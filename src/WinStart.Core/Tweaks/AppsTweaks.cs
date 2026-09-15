using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class AppsTweaks
{
    public static IEnumerable<TweakDefinition> All()
    {
        yield return RemovePreinstalled();
        yield return RemoveEdge();
        yield return RemoveDefender();
        yield return RestoreStore();
    }

    private static string ProgramFilesX86 => Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    private static string LocalAppData => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    private static string Win => Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static TweakDefinition RemovePreinstalled() => new()
    {
        Id = "apps.debloat",
        Category = TweakCategory.Apps,
        Kind = TweakKind.Action,
        Order = 1,
        Reversible = false,
        ActionVerbKey = "action.open",
        ConfirmKey = "confirm.debloat",
        CustomApply = async (ctx, ct) =>
        {
            var script = Path.Combine(ctx.Paths.Tools, "Apps.ps1");
            if (!File.Exists(script)) throw new FileNotFoundException(ctx.Text("log.fileMissing", "Apps.ps1"), script);

            ctx.Progress(ctx.Text("log.debloatStart"), null);
            var r = await ctx.Process.RunAsync("powershell.exe",
                $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\"", ct,
                workingDirectory: ctx.Paths.Tools).ConfigureAwait(false);

            ctx.Log($"Apps.ps1 → {r.ExitCode}");
            ctx.Progress(ctx.Text("log.debloatDone"), 100);
        }
    };

    private static TweakDefinition RemoveEdge() => new()
    {
        Id = "apps.removeEdge",
        Category = TweakCategory.Apps,
        Kind = TweakKind.Action,
        Order = 2,
        Destructive = true,
        Reversible = false,
        ConfirmKey = "confirm.removeEdge",
        ActionVerbKey = "action.run",
        Options =
        [
            new TweakOption { Id = "Edge", TitleKey = "option.edge.browser" },
            new TweakOption { Id = "WebView2", TitleKey = "option.edge.webview" },
            new TweakOption { Id = "Block", TitleKey = "option.edge.block" }
        ],
        CustomApply = async (ctx, ct) =>
        {
            switch (ctx.Option)
            {
                case "WebView2":
                    await RemoveWebView2Async(ctx, ct).ConfigureAwait(false);
                    break;
                case "Block":
                    BlockEdgeUpdate(ctx);
                    break;
                default:
                    await RemoveEdgeBrowserAsync(ctx, ct).ConfigureAwait(false);
                    break;
            }
        }
    };

    private static async Task RemoveEdgeBrowserAsync(ITweakContext ctx, CancellationToken ct)
    {
        ctx.Progress(ctx.Text("log.edgeRemoving"), null);

        var systemLevel = Directory.Exists(Path.Combine(ProgramFilesX86, @"Microsoft\Edge"))
            ? "--system-level" : "";

        var setup = new[] { ProgramFilesX86, LocalAppData }
            .Select(root => FileOps.Files(Path.Combine(root, @"Microsoft\Edge\Application"), "setup.exe", recursive: true)
                .FirstOrDefault())
            .FirstOrDefault(s => s is not null);

        if (setup is not null)
        {
            var stub = Path.Combine(Win, @"SystemApps\Microsoft.MicrosoftEdge_8wekyb3d8bbwe\MicrosoftEdge.exe");
            try { Directory.CreateDirectory(stub); } catch { }

            await ctx.Process.RunAsync(setup,
                $"--uninstall --force-uninstall --verbose-logging --msedge {systemLevel}", ct, timeoutMs: 180000)
                .ConfigureAwait(false);

            try { if (Directory.Exists(stub)) Directory.Delete(stub, true); } catch { }
        }
        else
        {
            var bundled = ctx.Paths.Tool("setup.exe");
            if (File.Exists(bundled))
                await ctx.Process.RunAsync(bundled,
                    $"--uninstall --force-uninstall --verbose-logging --msedge {systemLevel}", ct, timeoutMs: 180000)
                    .ConfigureAwait(false);
        }

        ctx.Progress(ctx.Text("log.edgeAppx"), null);
        await ctx.Process.PowerShellAsync(
            "Get-AppxProvisionedPackage -Online | Where-Object { $_.PackageName -match 'MicrosoftEdge' } | " +
            "Remove-AppxProvisionedPackage -Online -ErrorAction SilentlyContinue", ct, 120000).ConfigureAwait(false);
        await ctx.Process.PowerShellAsync(
            "Get-AppxPackage -AllUsers *MicrosoftEdge* | Remove-AppxPackage -AllUsers -ErrorAction SilentlyContinue",
            ct, 120000).ConfigureAwait(false);

        ctx.Progress(ctx.Text("log.edgeServices"), null);
        foreach (var svc in new[] { "edgeupdate", "edgeupdatem", "MicrosoftEdgeElevationService" })
        {
            await ctx.Process.TrustedInstallerAsync($"sc config {svc} start=disabled", ct, 30000).ConfigureAwait(false);
            await ctx.Process.TrustedInstallerAsync($"sc delete {svc}", ct, 30000).ConfigureAwait(false);
        }

        foreach (var folder in new[]
                 {
                     Path.Combine(ProgramFilesX86, @"Microsoft\Edge"),
                     Path.Combine(ProgramFilesX86, @"Microsoft\EdgeCore"),
                     Path.Combine(LocalAppData, @"Microsoft\Edge")
                 })
        {
            if (Directory.Exists(folder))
                await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{folder}\"", ct, 120000).ConfigureAwait(false);
        }

        RemoveEdgeShortcuts(ctx);

        await RemoveEdgeUpdateAsync(ctx, ct).ConfigureAwait(false);
        ctx.Progress(ctx.Text("log.edgeRemoved"), 100);
    }

    private static async Task RemoveWebView2Async(ITweakContext ctx, CancellationToken ct)
    {
        ctx.Progress(ctx.Text("log.webViewRemoving"), null);

        var bundled = ctx.Paths.Tool("setup.exe");
        if (File.Exists(bundled))
            await ctx.Process.RunAsync(bundled,
                "--uninstall --force-uninstall --verbose-logging --msedgewebview --system-level", ct, timeoutMs: 120000)
                .ConfigureAwait(false);

        foreach (var proc in new[] { "msedgewebview2", "msedgeupdate" })
            await ctx.Process.TrustedInstallerAsync($"taskkill /f /im {proc}.exe", ct, 20000).ConfigureAwait(false);

        foreach (var folder in new[]
                 {
                     Path.Combine(ProgramFilesX86, @"Microsoft\EdgeWebView"),
                     Path.Combine(LocalAppData, @"Microsoft\EdgeWebView"),
                     Path.Combine(Win, @"System32\Microsoft-Edge-WebView")
                 })
        {
            if (Directory.Exists(folder))
                await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{folder}\"", ct, 120000).ConfigureAwait(false);
        }

        await RemoveEdgeUpdateAsync(ctx, ct).ConfigureAwait(false);
        ctx.Progress(ctx.Text("log.webViewRemoved"), 100);
    }

    private static void RemoveEdgeShortcuts(ITweakContext ctx)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var publicDir = Environment.GetEnvironmentVariable("PUBLIC") ?? @"C:\Users\Public";

        var shortcuts = new[]
        {
            Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs\Microsoft Edge.lnk"),
            Path.Combine(programData, @"Microsoft\Windows\Start Menu\Programs\Microsoft Edge.lnk"),
            Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\Microsoft Edge.lnk"),
            Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar\Microsoft Edge.lnk"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Microsoft Edge.lnk"),
            Path.Combine(publicDir, @"Desktop\Microsoft Edge.lnk")
        };

        foreach (var lnk in shortcuts.Where(File.Exists))
            ctx.Log(ctx.Text(FileOps.TryDeleteFile(lnk) ? "log.shortcutDeleted" : "log.shortcutDeleteFailed", lnk));
    }

    private static async Task RemoveEdgeUpdateAsync(ITweakContext ctx, CancellationToken ct)
    {
        ctx.Progress(ctx.Text("log.edgeUpdateRemoving"), null);
        foreach (var root in new[] { ProgramFilesX86, LocalAppData })
        {
            var updater = Path.Combine(root, @"Microsoft\EdgeUpdate\MicrosoftEdgeUpdate.exe");
            if (File.Exists(updater))
                await ctx.Process.RunAsync(updater, "/uninstall", ct, timeoutMs: 60000).ConfigureAwait(false);
        }

        await ctx.Process.TrustedInstallerAsync("taskkill /f /im MicrosoftEdgeUpdate.exe", ct, 20000)
            .ConfigureAwait(false);

        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        await ctx.Process.TrustedInstallerAsync(
            $"rd /s /q \"{Path.Combine(programData, @"Microsoft\EdgeUpdate")}\"", ct, 60000).ConfigureAwait(false);
    }

    private static readonly string[] EdgeIfeoImages =
    [
        "MicrosoftEdgeStandaloneInstaller", "MicrosoftEdgeUpdate", "MicrosoftEdgeUpdateCore",
        "MicrosoftEdgeUpdateOnDemand", "MicrosoftEdgeUpdateSetup", "MicrosoftEdgeUpdateBroker",
        "MicrosoftEdgeUpdateComRegisterShell64", "MicrosoftEdgeUpdateComRegisterShell"
    ];

    private static void BlockEdgeUpdate(ITweakContext ctx)
    {
        ctx.Progress(ctx.Text("log.edgeBlocking"), null);

        ctx.Registry.SetValue(@"HKLM\Software\Policies\Microsoft\EdgeUpdate", "UpdateDefault",
            0, Microsoft.Win32.RegistryValueKind.DWord);
        ctx.Registry.SetValue(@"HKLM\Software\Microsoft\EdgeUpdate", "DoNotUpdateToEdgeWithChromium",
            1, Microsoft.Win32.RegistryValueKind.DWord);

        foreach (var image in EdgeIfeoImages)
            ctx.Registry.SetValue(
                $@"HKLM\Software\Microsoft\Windows NT\CurrentVersion\Image File Execution Options\{image}.exe",
                "Debugger", "dllhost.exe", Microsoft.Win32.RegistryValueKind.String);

        ctx.Progress(ctx.Text("log.edgeBlocked"), 100);
    }

    private static TweakDefinition RemoveDefender() => new()
    {
        Id = "apps.removeDefender",
        Category = TweakCategory.Apps,
        Kind = TweakKind.Action,
        Order = 3,
        Destructive = true,
        Reversible = false,
        ConfirmKey = "confirm.removeDefender",
        ActionVerbKey = "action.open",
        CustomApply = async (ctx, ct) =>
        {
            var bat = Path.Combine(ctx.Paths.Tools, "DefenderKiller.bat");
            if (!File.Exists(bat)) throw new FileNotFoundException(ctx.Text("log.fileMissing", "DefenderKiller.bat"), bat);

            ctx.Progress(ctx.Text("log.defenderStart"), null);
            await ctx.Process.LaunchAsync(bat, "", wait: false, ct).ConfigureAwait(false);
            ctx.Log(ctx.Text("log.defenderFollow"));
        }
    };

    private static TweakDefinition RestoreStore() => new()
    {
        Id = "apps.restoreStore",
        Category = TweakCategory.Apps,
        Kind = TweakKind.Action,
        Order = 4,
        Reversible = false,
        ConfirmKey = "confirm.restoreStore",
        ActionVerbKey = "action.run",
        RequiresNetwork = true,
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.storeRestoring"), null);
            await ctx.Process.RunAsync("wsreset.exe", "-i", ct, timeoutMs: 120000).ConfigureAwait(false);

            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            var appsDir = Path.Combine(programFiles, "WindowsApps");

            ctx.Progress(ctx.Text("log.storeWaiting"), null);
            for (var i = 0; i < 30; i++)
            {
                ct.ThrowIfCancellationRequested();
                if (FileOps.Directories(appsDir).Any(d => d.Contains("WindowsStore", StringComparison.OrdinalIgnoreCase)))
                    break;
                await Task.Delay(2000, ct).ConfigureAwait(false);
            }

            ctx.Registry.SetValue(@"HKLM\System\CurrentControlSet\Services\AppXSvc", "Start",
                2, Microsoft.Win32.RegistryValueKind.DWord);

            ctx.Progress(ctx.Text("log.storeRestored"), 100);
        }
    };
}
