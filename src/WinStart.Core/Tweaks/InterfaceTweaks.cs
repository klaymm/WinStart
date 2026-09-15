using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class InterfaceTweaks
{
    public static IEnumerable<TweakDefinition> All()
    {
        yield return Home();
        yield return Gallery();
        yield return Network();
        yield return FolderIcons();
        yield return ShowSeconds();
        yield return DayOfWeek();
        yield return EndTask();
        yield return TaskbarIcons();
        yield return LanguageIcon();
        yield return Recommended();
        yield return SettingsFolder();
        yield return WallpaperQuality();
        yield return LockScreen();
        yield return IconShadow();
        yield return SettingsHome();
    }

    private static TweakDefinition Home() => new()
    {
        Id = "interface.home",
        Category = TweakCategory.Interface,
        Order = 1,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(Keys.ExplorerMachine, "HubMode", 1),
            RegistryAction.Set(Keys.Clsid(Keys.HomeClsid), Keys.PinnedValue, 0),
            new RegistryAction
            {
                Op = RegistryOp.SetValue,
                Path = Keys.WowClsid(Keys.HomeClsid),
                Name = Keys.PinnedValue,
                ValueKind = RegistryValueKind.DWord,
                Value = 0,
                SkipInDetect = true
            }
        ],
        RevertActions =
        [
            RegistryAction.DeleteValue(Keys.ExplorerMachine, "HubMode"),
            RegistryAction.DeleteKey(Keys.Clsid(Keys.HomeClsid)),
            RegistryAction.DeleteKey(Keys.WowClsid(Keys.HomeClsid))
        ]
    };

    private static TweakDefinition Gallery() => new()
    {
        Id = "interface.gallery",
        Category = TweakCategory.Interface,
        Order = 2,
        MinBuild = 22000,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(Keys.Clsid(Keys.GalleryClsid), Keys.PinnedValue, 0),
            new RegistryAction
            {
                Op = RegistryOp.SetValue,
                Path = Keys.WowClsid(Keys.GalleryClsid),
                Name = Keys.PinnedValue,
                ValueKind = RegistryValueKind.DWord,
                Value = 0,
                SkipInDetect = true
            }
        ],
        RevertActions =
        [
            RegistryAction.DeleteKey(Keys.Clsid(Keys.GalleryClsid)),
            RegistryAction.DeleteKey(Keys.WowClsid(Keys.GalleryClsid))
        ]
    };

    private static TweakDefinition Network() => new()
    {
        Id = "interface.network",
        Category = TweakCategory.Interface,
        Order = 3,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(Keys.Clsid(Keys.NetworkClsid), Keys.PinnedValue, 0)
        ],
        RevertActions =
        [
            RegistryAction.DeleteValue(Keys.Clsid(Keys.NetworkClsid), Keys.PinnedValue)
        ]
    };

    private static TweakDefinition FolderIcons() => new()
    {
        Id = "interface.folderIcons",
        Category = TweakCategory.Interface,
        Order = 4,
        MinBuild = 22000,
        RequiresNetwork = true,
        NeedsExplorerRestart = true,
        Options =
        [
            new TweakOption { Id = "BlueIcon", TitleKey = "option.folderIcons.blue", Swatch = "#2C7FD4" },
            new TweakOption { Id = "GrayIcon", TitleKey = "option.folderIcons.gray", Swatch = "#8A8A8A" }
        ],
        CustomDetect = _ => File.Exists(MunBackupPath) ? TweakState.Applied : TweakState.NotApplied,
        CustomApply = ApplyFolderIconsAsync,
        CustomRevert = RevertFolderIconsAsync
    };

    private static string WindowsDir => Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static string MunPath => Path.Combine(WindowsDir, "SystemResources", "imageres.dll.mun");
    private static string MunBackupPath => MunPath + "_bak";

    private static async Task ApplyFolderIconsAsync(ITweakContext ctx, CancellationToken ct)
    {
        var variant = ctx.Option is "GrayIcon" ? "GrayIcon" : "BlueIcon";
        var archive = ctx.Paths.Temp($"{variant}.7z");
        var extracted = ctx.Paths.Temp(variant);

        ctx.Progress(ctx.Text("log.iconsDownloading"), 0);
        var url = $"https://github.com/MartyFiles/Custom-folder-icons/releases/download/Release/{variant}.7z";

        var ok = await ctx.Download.DownloadAsync([url], archive,
            new Progress<double>(p => ctx.Progress(null, p)), ct).ConfigureAwait(false);
        if (!ok) throw new InvalidOperationException(ctx.Text("log.iconsDownloadFailed"));

        ctx.Progress(ctx.Text("log.extracting"), null);
        if (!await ctx.Archive.ExtractAsync(archive, extracted, "WC", ct).ConfigureAwait(false))
            throw new InvalidOperationException(ctx.Text("log.iconsExtractFailed"));

        ctx.Progress(ctx.Text("log.iconsReplacing"), null);

        if (!File.Exists(MunBackupPath))
            await ctx.Process.TrustedInstallerAsync(
                $"ren \"{MunPath}\" imageres.dll.mun_bak", ct, 60000).ConfigureAwait(false);

        await ctx.Process.TrustedInstallerAsync(
            $"copy /y \"{Path.Combine(extracted, "imageres.dll.mun")}\" \"{Path.Combine(WindowsDir, "SystemResources")}\"",
            ct, 60000).ConfigureAwait(false);

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var startMenu = Path.Combine(appData, @"Microsoft\Windows\Start Menu\Programs");
        var quickLaunch = Path.Combine(appData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");

        foreach (var name in new[] { "File Explorer.lnk", "Проводник.lnk" })
        {
            FileOps.TryDeleteFile(Path.Combine(startMenu, name));
            FileOps.TryDeleteFile(Path.Combine(quickLaunch, name));
        }

        FileOps.TryCopyFile(Path.Combine(extracted, "File Explorer.lnk"), Path.Combine(startMenu, "File Explorer.lnk"));
        FileOps.TryCopyFile(Path.Combine(extracted, "File Explorer.lnk"), Path.Combine(quickLaunch, "File Explorer.lnk"));
        FileOps.TryCopyFile(Path.Combine(extracted, "Blank.ico"), Path.Combine(WindowsDir, "Blank.ico"));

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var users = Path.Combine(Path.GetPathRoot(WindowsDir) ?? @"C:\", "Users");

        foreach (var (source, target) in new[]
                 {
                     ("windows", WindowsDir),
                     ("x64", programFiles),
                     ("x86", programFilesX86),
                     ("users", users)
                 })
        {
            var from = Path.Combine(extracted, source);
            if (!Directory.Exists(from)) continue;

            await ctx.Process.CmdAsync(
                $"xcopy \"{from}\" \"{target}\" /E /I /Y /H /K /C /R /F", ct, 120000).ConfigureAwait(false);
        }

        ctx.Backup($@"{Keys.ExplorerMachine}\Shell Icons", "179");
        ctx.Registry.SetValue($@"{Keys.ExplorerMachine}\Shell Icons", "179",
            Path.Combine(WindowsDir, "Blank.ico") + ",0", RegistryValueKind.ExpandString);

        foreach (var folderType in new[] { "CompressedFolder", "ArchiveFolder" })
        {
            var key = $@"HKCR\{folderType}\DefaultIcon";
            ctx.Backup(key, "");
            ctx.Registry.SetValue(key, "", @"%SystemRoot%\System32\imageres.dll,165",
                RegistryValueKind.ExpandString);
        }

        foreach (var folder in new[] { programFiles, programFilesX86, users, WindowsDir })
            await ctx.Process.CmdAsync($"attrib +R \"{folder}\"", ct, 30000).ConfigureAwait(false);

        FileOps.TryDeleteFile(archive);
        FileOps.TryDeleteDirectory(extracted);
    }

    private static async Task RevertFolderIconsAsync(ITweakContext ctx, CancellationToken ct)
    {
        if (!File.Exists(MunBackupPath))
        {
            ctx.Log(ctx.Text("log.iconsNoBackup"));
            return;
        }

        ctx.Progress(ctx.Text("log.iconsRestoring"), null);

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var users = Path.Combine(Path.GetPathRoot(WindowsDir) ?? @"C:\", "Users");

        foreach (var (folder, icon) in new[]
                 {
                     (WindowsDir, "windows.ico"),
                     (programFiles, "x64.ico"),
                     (programFilesX86, "x86.ico"),
                     (users, "users.ico")
                 })
        {
            await ctx.Process.CmdAsync(
                $"del /q /f /a:h \"{Path.Combine(folder, icon)}\" \"{Path.Combine(folder, "desktop.ini")}\"",
                ct, 30000).ConfigureAwait(false);
        }

        await ctx.Process.TrustedInstallerAsync($"del /q /f \"{MunPath}\"", ct, 60000).ConfigureAwait(false);
        await ctx.Process.TrustedInstallerAsync(
            $"ren \"{MunBackupPath}\" imageres.dll.mun", ct, 60000).ConfigureAwait(false);

        foreach (var folderType in new[] { "CompressedFolder", "ArchiveFolder" })
            ctx.Registry.SetValue($@"HKCR\{folderType}\DefaultIcon", "",
                @"%SystemRoot%\System32\zipfldr.dll", RegistryValueKind.ExpandString);

        ctx.Registry.DeleteValue($@"{Keys.ExplorerMachine}\Shell Icons", "179");
        FileOps.TryDeleteFile(Path.Combine(WindowsDir, "Blank.ico"));

        ctx.RequestExplorerRestart();
    }

    private static TweakDefinition ShowSeconds() => new()
    {
        Id = "interface.seconds",
        Category = TweakCategory.Interface,
        Order = 5,
        NeedsExplorerRestart = true,
        RegistryActions = [RegistryAction.Set(Keys.ExplorerAdvanced, "ShowSecondsInSystemClock", 1)],
        RevertActions = [RegistryAction.DeleteValue(Keys.ExplorerAdvanced, "ShowSecondsInSystemClock")]
    };

    private static TweakDefinition DayOfWeek() => new()
    {
        Id = "interface.dayOfWeek",
        Category = TweakCategory.Interface,
        Order = 6,
        RegistryActions = [RegistryAction.SetString(Keys.International, "sShortDate", "ddd, dd.MM.yy")],
        RevertActions = [RegistryAction.SetString(Keys.International, "sShortDate", "dd.MM.yyyy")],
        CustomApply = (_, _) => { NotifyIntl(); return Task.CompletedTask; },
        CustomRevert = (_, _) => { NotifyIntl(); return Task.CompletedTask; }
    };

    private static void NotifyIntl() => NativeMethods.BroadcastSettingChange("intl");

    private static TweakDefinition EndTask() => new()
    {
        Id = "interface.endTask",
        Category = TweakCategory.Interface,
        Order = 7,
        MinBuild = 22621,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set($@"{Keys.ExplorerAdvanced}\TaskbarDeveloperSettings", "TaskbarEndTask", 1)
        ],
        RevertActions =
        [
            RegistryAction.DeleteValue($@"{Keys.ExplorerAdvanced}\TaskbarDeveloperSettings", "TaskbarEndTask")
        ]
    };

    private static TweakDefinition TaskbarIcons() => new()
    {
        Id = "interface.taskbarIcons",
        Category = TweakCategory.Interface,
        Order = 8,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(Keys.Search, "SearchboxTaskbarMode", 0),
            RegistryAction.Set(Keys.ExplorerAdvanced, "ShowTaskViewButton", 0),
            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0)
        ],
        RevertActions =
        [
            RegistryAction.Set(Keys.Search, "SearchboxTaskbarMode", 2),
            RegistryAction.Set(Keys.ExplorerAdvanced, "ShowTaskViewButton", 1),
            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 1)
        ]
    };

    private static TweakDefinition LanguageIcon() => new()
    {
        Id = "interface.langIcon",
        Category = TweakCategory.Interface,
        Order = 9,
        RegistryActions = [RegistryAction.Set(@"HKCU\Software\Microsoft\CTF\LangBar", "ShowStatus", 3)],
        RevertActions = [RegistryAction.Set(@"HKCU\Software\Microsoft\CTF\LangBar", "ShowStatus", 0)],
        CustomApply = async (ctx, ct) =>
        {
            ctx.Progress(ctx.Text("log.langBar"), null);
            await ctx.Process.PowerShellAsync("Set-WinLanguageBarOption -UseLegacyLanguageBar", ct, 30000)
                .ConfigureAwait(false);
        },
        CustomRevert = async (ctx, ct) =>
        {
            await ctx.Process.PowerShellAsync("Set-WinLanguageBarOption", ct, 30000).ConfigureAwait(false);
        }
    };

    private static TweakDefinition Recommended() => new()
    {
        Id = "interface.recommended",
        Category = TweakCategory.Interface,
        Order = 10,
        MinBuild = 22000,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(@"HKLM\Software\Microsoft\PolicyManager\current\device\Start",
                "HideRecommendedSection", 1),
            RegistryAction.Set(@"HKLM\Software\Microsoft\PolicyManager\current\device\Education",
                "IsEducationEnvironment", 1),
            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\Windows\Explorer",
                "HideRecommendedSection", 1)
        ],
        RevertActions =
        [
            RegistryAction.DeleteValue(@"HKLM\Software\Microsoft\PolicyManager\current\device\Start",
                "HideRecommendedSection"),
            RegistryAction.DeleteValue(@"HKLM\Software\Microsoft\PolicyManager\current\device\Education",
                "IsEducationEnvironment"),
            RegistryAction.DeleteValue(@"HKLM\Software\Policies\Microsoft\Windows\Explorer",
                "HideRecommendedSection")
        ]
    };

    private static TweakDefinition SettingsFolder() => new()
    {
        Id = "interface.settingsFolder",
        Category = TweakCategory.Interface,
        Order = 11,
        MinBuild = 22000,
        RegistryActions =
        [
            RegistryAction.SetBinary(Keys.StartMenu, "VisiblePlaces",
                Convert.FromHexString("86087352AA5143429F7B2776584659D4"))
        ],
        RevertActions =
        [
            RegistryAction.SetBinary(Keys.StartMenu, "VisiblePlaces", [0])
        ],
        CustomApply = RestartStartMenuAsync,
        CustomRevert = RestartStartMenuAsync
    };

    private static async Task RestartStartMenuAsync(ITweakContext ctx, CancellationToken ct)
    {
        ctx.Progress(ctx.Text("log.startRestart"), null);
        await ctx.Process.RunAsync("taskkill.exe", "/f /im StartMenuExperienceHost.exe", ct, timeoutMs: 15000)
            .ConfigureAwait(false);
    }

    private static TweakDefinition WallpaperQuality() => new()
    {
        Id = "interface.wallpaperQuality",
        Category = TweakCategory.Interface,
        Order = 12,
        RegistryActions = [RegistryAction.Set(Keys.Desktop, "JPEGImportQuality", 100)],
        RevertActions = [RegistryAction.DeleteValue(Keys.Desktop, "JPEGImportQuality")]
    };

    private static TweakDefinition LockScreen() => new()
    {
        Id = "interface.lockScreen",
        Category = TweakCategory.Interface,
        Order = 13,
        RegistryActions =
        [
            RegistryAction.Set(Keys.Personalization, "NoLockScreen", 1),
            RegistryAction.Set(Keys.Personalization, "NoLockScreenCamera", 1)
        ],
        RevertActions =
        [
            RegistryAction.DeleteValue(Keys.Personalization, "NoLockScreen"),
            RegistryAction.DeleteValue(Keys.Personalization, "NoLockScreenCamera")
        ]
    };

    private static TweakDefinition IconShadow() => new()
    {
        Id = "interface.iconShadow",
        Category = TweakCategory.Interface,
        Order = 14,
        NeedsExplorerRestart = true,
        RegistryActions = [RegistryAction.Set(Keys.ExplorerAdvanced, "ListviewShadow", 0)],
        RevertActions = [RegistryAction.Set(Keys.ExplorerAdvanced, "ListviewShadow", 1)]
    };

    private static TweakDefinition SettingsHome() => new()
    {
        Id = "interface.settingsHome",
        Category = TweakCategory.Interface,
        Order = 15,
        MinBuild = 22000,
        CustomDetect = ctx =>
        {
            var value = ctx.Registry.GetString(Keys.PoliciesExplorer, "SettingsPageVisibility") ?? "";
            return value.Contains("home", StringComparison.OrdinalIgnoreCase)
                   && value.StartsWith("hide:", StringComparison.OrdinalIgnoreCase)
                ? TweakState.Applied
                : TweakState.NotApplied;
        },
        CustomApply = (ctx, _) =>
        {
            ctx.Backup(Keys.PoliciesExplorer, "SettingsPageVisibility");

            var pages = ReadHiddenPages(ctx);
            if (!pages.Contains("home", StringComparer.OrdinalIgnoreCase)) pages.Add("home");

            ctx.Registry.SetValue(Keys.PoliciesExplorer, "SettingsPageVisibility",
                "hide:" + string.Join(';', pages), RegistryValueKind.String);

            return Task.CompletedTask;
        },
        CustomRevert = (ctx, _) =>
        {
            var pages = ReadHiddenPages(ctx);
            pages.RemoveAll(p => p.Equals("home", StringComparison.OrdinalIgnoreCase));

            if (pages.Count == 0)
                ctx.Registry.DeleteValue(Keys.PoliciesExplorer, "SettingsPageVisibility");
            else
                ctx.Registry.SetValue(Keys.PoliciesExplorer, "SettingsPageVisibility",
                    "hide:" + string.Join(';', pages), RegistryValueKind.String);

            return Task.CompletedTask;
        }
    };

    private static List<string> ReadHiddenPages(ITweakContext ctx)
    {
        var raw = ctx.Registry.GetString(Keys.PoliciesExplorer, "SettingsPageVisibility") ?? "";
        var idx = raw.IndexOf(':');
        if (idx < 0) return [];

        return raw[(idx + 1)..]
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }
}
