using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class ComponentsTweaks
{
    private static readonly string[] VisualCppMirrors =
    [
        "https://cf.comss.org/download/Visual-C-Runtimes-All-in-One-Jun-2026.zip"
    ];

    public static IEnumerable<TweakDefinition> All()
    {
        yield return VisualCpp();
        yield return DirectX();
        yield return WebView2();
    }

    private static TweakDefinition VisualCpp() => new()
    {
        Id = "components.visualCpp",
        Category = TweakCategory.Components,
        Kind = TweakKind.Action,
        Order = 1,
        Reversible = false,
        ActionVerbKey = "action.install",
        RequiresNetwork = true,
        CustomApply = async (ctx, ct) =>
        {
            var download = ctx.Paths.Temp("VisualC.zip");
            var extractDir = ctx.Paths.Temp("VisualC");

            ctx.Progress(ctx.Text("log.downloading", "Visual C++ Runtimes"), 0);
            var ok = await ctx.Download.DownloadAsync(VisualCppMirrors, download,
                new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);
            if (!ok) throw new InvalidOperationException(ctx.Text("log.downloadFailed", "Visual C++ Runtimes"));

            ctx.Progress(ctx.Text("log.extracting"), null);
            FileOps.TryDeleteDirectory(extractDir);
            if (!await ctx.Archive.ExtractAsync(download, extractDir, null, ct).ConfigureAwait(false))
                throw new InvalidOperationException(ctx.Text("log.extractFailed", "Visual C++ Runtimes"));

            var installer = FileOps.Files(extractDir, "install_all.bat", recursive: true).FirstOrDefault();

            ctx.Progress(ctx.Text("log.installingLong", "Visual C++ Runtimes"), null);
            if (installer is not null)
            {
                var dir = Path.GetDirectoryName(installer)!;
                var r = await ctx.Process.RunAsync("cmd.exe", "/c \"install_all.bat\"", ct,
                    workingDirectory: dir, timeoutMs: 900000).ConfigureAwait(false);
                ctx.Log($"install_all.bat → {r.ExitCode}");
            }
            else
            {
                await InstallVcredistsAsync(ctx, extractDir, ct).ConfigureAwait(false);
            }

            FileOps.TryDeleteFile(download);
            FileOps.TryDeleteDirectory(extractDir);
            ctx.Progress(ctx.Text("log.installed", "Visual C++ Runtimes"), 100);
        }
    };

    private static TweakDefinition DirectX() => new()
    {
        Id = "components.directX",
        Category = TweakCategory.Components,
        Kind = TweakKind.Action,
        Order = 2,
        Reversible = false,
        ActionVerbKey = "action.install",
        RequiresNetwork = true,
        CustomApply = async (ctx, ct) =>
        {
            var archive = ctx.Paths.Temp("DirectX.7z");
            var dir = ctx.Paths.Temp("DirectX");

            ctx.Progress(ctx.Text("log.downloading", "DirectX 9-11"), 0);
            var ok = await ctx.Download.DownloadAsync(
                ["https://github.com/MartyFiles/DirectX/releases/download/Release/DirectX.7z"], archive,
                new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);
            if (!ok) throw new InvalidOperationException(ctx.Text("log.downloadFailed", "DirectX"));

            ctx.Progress(ctx.Text("log.extracting"), null);
            if (!await ctx.Archive.ExtractAsync(archive, dir, "WC", ct).ConfigureAwait(false))
                throw new InvalidOperationException(ctx.Text("log.extractFailed", "DirectX"));

            var exe = Path.Combine(dir, "DirectX.exe");
            ctx.Progress(ctx.Text("log.installing", "DirectX"), null);
            var r = await ctx.Process.RunAsync(exe, "/ai1 /gm2", ct, timeoutMs: 600000).ConfigureAwait(false);
            ctx.Log($"установщик DirectX → {r.ExitCode}");

            ctx.Registry.DeleteKey(
                @"HKLM\Software\Microsoft\Windows\CurrentVersion\Uninstall\DirectX End-User Runtime");

            FileOps.TryDeleteFile(archive);
            FileOps.TryDeleteDirectory(dir);
            ctx.Progress(ctx.Text("log.installed", "DirectX 9-11"), 100);
        }
    };

    private static TweakDefinition WebView2() => new()
    {
        Id = "components.webView2",
        Category = TweakCategory.Components,
        Kind = TweakKind.Action,
        Order = 3,
        Reversible = false,
        RequiresNetwork = true,
        ActionVerbKey = "action.install",
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.downloading", "WebView2"), 0);
            var installer = ctx.Paths.Temp("WebView2.exe");

            var ok = await ctx.Download.DownloadAsync(
                ["https://go.microsoft.com/fwlink/?linkid=2124701"], installer,
                new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);
            if (!ok) throw new InvalidOperationException(ctx.Text("log.downloadFailed", "WebView2"));

            ctx.Progress(ctx.Text("log.installing", "WebView2"), null);
            var r = await ctx.Process.RunAsync(installer, "/silent /install", ct, timeoutMs: 300000)
                .ConfigureAwait(false);
            ctx.Log($"установщик WebView2 → {r.ExitCode}");

            FileOps.TryDeleteFile(installer);
            ctx.Progress(ctx.Text("log.installed", "WebView2"), 100);
        }
    };

    private static async Task InstallVcredistsAsync(ITweakContext ctx, string dir, CancellationToken ct)
    {
        foreach (var exe in FileOps.Files(dir, "vcredist*.exe", recursive: true).Order())
        {
            ct.ThrowIfCancellationRequested();
            var name = Path.GetFileNameWithoutExtension(exe).ToLowerInvariant();

            var args = name.Contains("2005") ? "/q"
                : name.Contains("2008") ? "/qb"
                : "/passive /norestart";

            var r = await ctx.Process.RunAsync(exe, args, ct, timeoutMs: 300000).ConfigureAwait(false);
            ctx.Log($"{Path.GetFileName(exe)} → {r.ExitCode}");
        }
    }
}
