using System.Globalization;
using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class ContextMenuTweaks
{
    public static IEnumerable<TweakDefinition> All()
    {
        yield return Unlocker();
        yield return Everything();
    }

    private static string Label(string ru, string en) =>
        CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? ru : en;

    private static readonly TweakOption[] Positions =
    [
        new() { Id = "Bottom", TitleKey = "option.position.bottom" },
        new() { Id = "Middle", TitleKey = "option.position.middle" },
        new() { Id = "Top", TitleKey = "option.position.top" }
    ];

    private static TweakDefinition Unlocker() => new()
    {
        Id = "context.unlocker",
        Category = TweakCategory.ContextMenu,
        Order = 1,
        RequiresNetwork = true,
        Options = Positions,
        SupportsShiftOption = true,
        CustomDetect = ctx => EntryWorks(ctx, @"HKCR\*\shell\Destroy") ? TweakState.Applied : TweakState.NotApplied,
        CustomApply = ApplyUnlockerAsync,
        CustomRevert = RevertUnlockerAsync
    };

    private static bool EntryWorks(ITweakContext ctx, string key)
    {
        var command = ctx.Registry.GetString($@"{key}\command", "")?.Trim();
        if (string.IsNullOrEmpty(command)) return false;

        var exe = command[0] == '"' ? command[1..].Split('"', 2)[0] : command.Split(' ', 2)[0];
        return File.Exists(exe);
    }

    private static async Task ApplyUnlockerAsync(ITweakContext ctx, CancellationToken ct)
    {
        var root = ctx.Paths.InstallRoot;
        var icons = Path.Combine(root, "Icons");
        var exe = Path.Combine(root, "Unlocker.exe");

        Directory.CreateDirectory(icons);

        ctx.Progress(ctx.Text("log.downloading", "Unlocker"), 0);
        var ok = await ctx.Download.DownloadAsync(
            ["https://i.getspace.eu/cloud/s/AK8zrMPcnP3dHZk/download"], exe,
            new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);

        if (!ok) throw new InvalidOperationException(ctx.Text("log.downloadFailed", "Unlocker"));

        foreach (var icon in new[] { "Unlock.ico", "Destroy.ico" })
            FileOps.TryCopyFile(Path.Combine(ctx.Paths.Tools, "Icons", icon), Path.Combine(icons, icon));

        foreach (var scope in new[] { @"HKCR\*\shell", @"HKCR\Directory\shell" })
        {
            Register(ctx, $@"{scope}\Destroy", Label("Уничтожить", "Destroy"),
                Path.Combine(icons, "Destroy.ico"), $"\"{exe}\" /contextMenu \"%1\"");

            Register(ctx, $@"{scope}\Unlock", Label("Разблокировать", "Unlock"),
                Path.Combine(icons, "Unlock.ico"), $"\"{exe}\" /unlock /contextMenu \"%1\"");
        }

        ctx.Progress(ctx.Text("log.menuAdded"), 100);
    }

    private static void Register(ITweakContext ctx, string key, string caption, string iconPath, string command)
    {
        var position = ctx.Option is "Top" or "Middle" ? ctx.Option : "Bottom";

        ctx.Registry.SetValue(key, "MUIVerb", caption, RegistryValueKind.String);
        ctx.Registry.SetValue(key, "Icon", iconPath + ",0", RegistryValueKind.String);
        ctx.Registry.SetValue(key, "Position", position, RegistryValueKind.String);
        ctx.Registry.SetValue($@"{key}\command", "", command, RegistryValueKind.String);

        if (ctx.Extended)
            ctx.Registry.SetValue(key, "Extended", "", RegistryValueKind.String);
        else
            ctx.Registry.DeleteValue(key, "Extended");
    }

    private static Task RevertUnlockerAsync(ITweakContext ctx, CancellationToken ct)
    {
        foreach (var key in new[]
                 {
                     @"HKCR\*\shell\Destroy", @"HKCR\Directory\shell\Destroy",
                     @"HKCR\*\shell\Unlock", @"HKCR\Directory\shell\Unlock"
                 })
            ctx.Registry.DeleteKey(key);

        var root = ctx.Paths.InstallRoot;
        FileOps.TryDeleteFile(Path.Combine(root, "Unlocker.exe"));
        FileOps.TryDeleteFile(Path.Combine(root, "Icons", "Unlock.ico"));
        FileOps.TryDeleteFile(Path.Combine(root, "Icons", "Destroy.ico"));
        FileOps.TryRemoveEmptyDirectory(Path.Combine(root, "Icons"));
        FileOps.TryRemoveEmptyDirectory(root);

        ctx.Log(ctx.Text("log.menuRemoved"));
        return Task.CompletedTask;
    }

    private static TweakDefinition Everything() => new()
    {
        Id = "context.everything",
        Category = TweakCategory.ContextMenu,
        Order = 2,
        RequiresNetwork = true,
        Options = Positions,
        SupportsShiftOption = true,
        CustomDetect = ctx => EntryWorks(ctx, EverythingKey) ? TweakState.Applied : TweakState.NotApplied,
        CustomApply = ApplyEverythingAsync,
        CustomRevert = RevertEverythingAsync
    };

    private const string EverythingKey = @"HKCR\Directory\Background\shell\Everything";

    private static async Task ApplyEverythingAsync(ITweakContext ctx, CancellationToken ct)
    {
        var target = Path.Combine(ctx.Paths.InstallRoot, "Everything");
        var archive = ctx.Paths.Temp("Everything.7z");

        ctx.Progress(ctx.Text("log.downloading", "Everything"), 0);
        var ok = await ctx.Download.DownloadAsync(
            ["https://github.com/MartyFiles/Everything/releases/download/Release/Everything.7z"], archive,
            new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);

        if (!ok) throw new InvalidOperationException(ctx.Text("log.downloadFailed", "Everything"));

        ctx.Progress(ctx.Text("log.extracting"), null);
        if (!await ctx.Archive.ExtractAsync(archive, target, "WC", ct).ConfigureAwait(false))
            throw new InvalidOperationException(ctx.Text("log.extractFailed", "Everything"));

        FileOps.TryDeleteFile(archive);

        Register(ctx, EverythingKey, Label("Поиск", "Search"),
            Path.Combine(target, "Everything.ico"), $"\"{Path.Combine(target, "Everything.exe")}\" -path \"%V\"");

        ctx.Progress(ctx.Text("log.menuAdded"), 100);
    }

    private static async Task RevertEverythingAsync(ITweakContext ctx, CancellationToken ct)
    {
        ctx.Registry.DeleteKey(EverythingKey);

        await ctx.Process.RunAsync("taskkill.exe", "/f /im Everything.exe", ct, timeoutMs: 15000)
            .ConfigureAwait(false);

        var target = Path.Combine(ctx.Paths.InstallRoot, "Everything");
        if (!FileOps.TryDeleteDirectory(target))
            await ctx.Process.TrustedInstallerAsync($"rd /s /q \"{target}\"", ct, 60000).ConfigureAwait(false);

        FileOps.TryRemoveEmptyDirectory(ctx.Paths.InstallRoot);
        ctx.Log(ctx.Text("log.menuRemoved"));
    }
}
