using System.Collections.Immutable;
using System.Drawing;
using Schneegans.Unattend;

namespace WinStart.Core.Unattend;

public static class UnattendBuilder
{
    public static byte[] Build(UnattendSettings s)
    {
        var g = UnattendCatalog.Generator;
        return UnattendGenerator.Serialize(g.GenerateXml(ToConfiguration(s, g)));
    }

    public static Configuration ToConfiguration(UnattendSettings s, UnattendGenerator g)
    {
        var scripts = new[]
            {
                (s.SystemScripts, ScriptPhase.System),
                (s.DefaultUserScripts, ScriptPhase.DefaultUser),
                (s.FirstLogonScripts, ScriptPhase.FirstLogon),
                (s.UserOnceScripts, ScriptPhase.UserOnce)
            }
            .SelectMany(pair => pair.Item1
                .Where(r => !string.IsNullOrWhiteSpace(r.Content))
                .Select(r => new Script(r.Content, pair.Item2, r.Type)))
            .ToList();

        var wingetApps = Apps.WingetCatalog.Apps.Where(a => s.WingetApps.Contains(a.Id)).Select(a => a.Id).ToList();
        if (wingetApps.Count > 0)
            scripts.Add(new Script(WingetScript(wingetApps), ScriptPhase.FirstLogon, ScriptType.Ps1));

        var components = UnattendCatalog.ComponentPasses
            .Select(cp => (Key: new ComponentAndPass(cp.Component.Id, cp.Pass),
                Xml: s.Components.GetValueOrDefault(UnattendCatalog.ComponentKey(cp.Component, cp.Pass))))
            .Where(x => !string.IsNullOrWhiteSpace(x.Xml))
            .ToImmutableDictionary(x => x.Key, x => x.Xml!);

        return Configuration.Default with
        {
            LanguageSettings = Language(s, g),
            AccountSettings = Accounts(s),
            LockoutSettings = s.LockoutMode switch
            {
                LockoutMode.Disable => new DisableLockoutSettings(),
                LockoutMode.Custom => new CustomLockoutSettings(s.LockoutThreshold, s.LockoutDuration, s.LockoutWindow),
                _ => new DefaultLockoutSettings()
            },
            PasswordExpirationSettings = s.PasswordExpiration switch
            {
                PasswordExpirationMode.Unlimited => new UnlimitedPasswordExpirationSettings(),
                PasswordExpirationMode.Custom => new CustomPasswordExpirationSettings(s.PasswordMaxAge),
                _ => new DefaultPasswordExpirationSettings()
            },
            ProcessAuditSettings = s.ProcessAudit
                ? new EnabledProcessAuditSettings(s.ProcessAuditCommandLine)
                : new DisabledProcessAuditSettings(),
            ComputerNameSettings = s.ComputerNameMode switch
            {
                ComputerNameMode.Custom => new CustomComputerNameSettings(s.ComputerName.Trim()),
                ComputerNameMode.Script => new ScriptComputerNameSettings(s.ComputerNameScript),
                _ => new RandomComputerNameSettings()
            },
            TimeZoneSettings = s.TimeZoneExplicit
                ? g.CreateExplicitTimeZoneSettings(s.TimeZone)
                : new ImplicitTimeZoneSettings(),
            WifiSettings = s.WifiMode switch
            {
                WifiMode.Skip => new SkipWifiSettings(),
                WifiMode.Parameters => new ParameterizedWifiSettings(s.WifiSsid, s.WifiPassword, ConnectAutomatically: true,
                    s.WifiAuthentication, s.WifiNonBroadcast),
                WifiMode.Xml => new XmlWifiSettings(s.WifiXml),
                _ => new InteractiveWifiSettings()
            },
            AppLockerSettings = s.AppLockerEnabled
                ? new ConfigureAppLockerSettings(s.AppLockerXml)
                : new SkipAppLockerSettings(),
            ProcessorArchitectures = new[]
                {
                    (s.ArchX86, ProcessorArchitecture.x86),
                    (s.ArchAmd64, ProcessorArchitecture.amd64),
                    (s.ArchArm64, ProcessorArchitecture.arm64)
                }
                .Where(a => a.Item1).Select(a => a.Item2).ToImmutableHashSet(),
            Components = components,
            Bloatwares = UnattendCatalog.Bloatwares.Where(b => s.Bloatware.Contains(b.Id)).ToImmutableList(),
            ExpressSettings = s.ExpressSettings,
            ScriptSettings = new ScriptSettings(scripts, s.RestartExplorer),
            LockKeySettings = s.LockKeysCustom
                ? new ConfigureLockKeySettings(
                    new LockKeySetting(s.CapsLockInitial, s.CapsLockBehavior),
                    new LockKeySetting(s.NumLockInitial, s.NumLockBehavior),
                    new LockKeySetting(s.ScrollLockInitial, s.ScrollLockBehavior))
                : new SkipLockKeySettings(),
            WallpaperSettings = s.WallpaperMode switch
            {
                WallpaperMode.Solid => new SolidWallpaperSettings(ParseColor(s.WallpaperColor)),
                WallpaperMode.Script => new ScriptWallpaperSettings(s.WallpaperScript),
                _ => new DefaultWallpaperSettings()
            },
            LockScreenSettings = s.LockScreenScriptEnabled
                ? new ScriptLockScreenSettings(s.LockScreenScript)
                : new DefaultLockScreenSettings(),
            ColorSettings = s.ColorsCustom
                ? new CustomColorSettings(s.SystemTheme, s.AppsTheme, s.EnableTransparency, s.AccentColorOnStart,
                    s.AccentColorOnBorders, ParseColor(s.AccentColor))
                : new DefaultColorSettings(),
            PESettings = Pe(s, g),
            ActivationKey = string.IsNullOrWhiteSpace(s.ActivationKey) ? null : new ProductKey(s.ActivationKey.Trim()),
            BypassNetworkCheck = s.BypassNetwork,
            EnableLongPaths = s.EnableLongPaths,
            EnableRemoteDesktop = s.EnableRemoteDesktop,
            HardenSystemDriveAcl = s.HardenSystemDriveAcl,
            DeleteJunctions = s.DeleteJunctions,
            AllowPowerShellScripts = s.AllowPowerShellScripts,
            DisableLastAccess = s.DisableLastAccess,
            PreventAutomaticReboot = s.PreventAutomaticReboot,
            DisableSac = s.DisableSac,
            DisableUac = s.DisableUac,
            DisableSmartScreen = s.DisableSmartScreen,
            DisableSystemRestore = s.DisableSystemRestore,
            DisableFastStartup = s.DisableFastStartup,
            TurnOffSystemSounds = s.TurnOffSystemSounds,
            DisableAppSuggestions = s.DisableAppSuggestions,
            DisableWidgets = s.DisableWidgets,
            VBoxGuestAdditions = s.VBoxGuestAdditions,
            VMwareTools = s.VMwareTools,
            VirtIoGuestTools = s.VirtIoGuestTools,
            ParallelsTools = s.ParallelsTools,
            PreventDeviceEncryption = s.PreventDeviceEncryption,
            ClassicContextMenu = s.ClassicContextMenu,
            LeftTaskbar = s.LeftTaskbar,
            HideTaskViewButton = s.HideTaskViewButton,
            ShowFileExtensions = s.ShowFileExtensions,
            ShowAllTrayIcons = s.ShowAllTrayIcons,
            HideFiles = s.HideFiles,
            HideEdgeFre = s.HideEdgeFre,
            DisableEdgeStartupBoost = s.DisableEdgeStartupBoost,
            MakeEdgeUninstallable = s.MakeEdgeUninstallable,
            DeleteEdgeDesktopIcon = s.DeleteEdgeDesktopIcon,
            LaunchToThisPC = s.LaunchToThisPC,
            DisableWindowsUpdate = s.DisableWindowsUpdate,
            DisablePointerPrecision = s.DisablePointerPrecision,
            DeleteWindowsOld = s.DeleteWindowsOld,
            DisableBingResults = s.DisableBingResults,
            UseConfigurationSet = s.UseConfigurationSet,
            HidePowerShellWindows = s.HidePowerShellWindows,
            ShowEndTask = s.ShowEndTask,
            KeepSensitiveFiles = s.KeepSensitiveFiles,
            UseNarrator = s.UseNarrator,
            DisableCoreIsolation = s.DisableCoreIsolation,
            DisableAutomaticRestartSignOn = s.DisableAutomaticRestartSignOn,
            HideInfoTip = s.HideInfoTip,
            DisableWpbt = s.DisableWpbt,
            PreventDeviceApps = s.PreventDeviceApps,
            TaskbarSearch = s.TaskbarSearch,
            StartPinsSettings = s.StartPins switch
            {
                LayoutMode.Empty => new EmptyStartPinsSettings(),
                LayoutMode.Custom => new CustomStartPinsSettings(s.StartPinsJson),
                _ => new DefaultStartPinsSettings()
            },
            StartTilesSettings = s.StartTiles switch
            {
                LayoutMode.Empty => new EmptyStartTilesSettings(),
                LayoutMode.Custom => new CustomStartTilesSettings(s.StartTilesXml),
                _ => new DefaultStartTilesSettings()
            },
            TaskbarIcons = s.TaskbarIcons switch
            {
                LayoutMode.Empty => new EmptyTaskbarIcons(),
                LayoutMode.Custom => new CustomTaskbarIcons(s.TaskbarIconsXml),
                _ => new DefaultTaskbarIcons()
            },
            Effects = s.EffectsMode switch
            {
                EffectsMode.BestAppearance => new BestAppearanceEffects(),
                EffectsMode.BestPerformance => new BestPerformanceEffects(),
                EffectsMode.Custom => new CustomEffects(Enum.GetValues<Effect>()
                    .ToImmutableDictionary(e => e, e => s.Effects.GetValueOrDefault(e.ToString()))),
                _ => new DefaultEffects()
            },
            DesktopIcons = s.DesktopIconsCustom
                ? new CustomDesktopIconSettings(UnattendCatalog.DesktopIcons
                    .ToDictionary(i => i, i => s.DesktopIcons.GetValueOrDefault(i.Id)))
                : new DefaultDesktopIconSettings(),
            StickyKeysSettings = s.StickyMode switch
            {
                StickyMode.Disabled => new DisabledStickyKeysSettings(),
                StickyMode.Custom => new CustomStickyKeysSettings(Enum.GetValues<StickyKeys>()
                    .Where(f => s.StickyFlags.GetValueOrDefault(f.ToString())).ToHashSet()),
                _ => new DefaultStickyKeysSettings()
            },
            StartFolderSettings = s.StartFoldersCustom
                ? new CustomStartFolderSettings(UnattendCatalog.StartFolders
                    .ToDictionary(f => f, f => s.StartFolders.GetValueOrDefault(f.Id)))
                : new DefaultStartFolderSettings()
        };
    }

    private static ILanguageSettings Language(UnattendSettings s, UnattendGenerator g)
    {
        if (s.LanguageInteractive) return new InteractiveLanguageSettings();

        LocaleAndKeyboard Pair(string locale, string keyboard) =>
            new(g.Lookup<UserLocale>(locale), g.Lookup<KeyboardIdentifier>(keyboard));

        return new UnattendedLanguageSettings(
            ImageLanguage: g.Lookup<ImageLanguage>(s.DisplayLanguage),
            LocaleAndKeyboard: Pair(s.Locale1, s.Keyboard1),
            LocaleAndKeyboard2: s.UseLocale2 ? Pair(s.Locale2, s.Keyboard2) : null,
            LocaleAndKeyboard3: s.UseLocale3 ? Pair(s.Locale3, s.Keyboard3) : null,
            GeoLocation: g.Lookup<GeoLocation>(s.GeoLocation));
    }

    private static IAccountSettings Accounts(UnattendSettings s)
    {
        switch (s.AccountMode)
        {
            case AccountMode.InteractiveMicrosoft: return new InteractiveMicrosoftAccountSettings();
            case AccountMode.InteractiveLocal: return new InteractiveLocalAccountSettings();
        }

        var accounts = s.Accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.Name))
            .Select(a => new Account(a.Name.Trim(), a.DisplayName.Trim(), a.Password, a.Group))
            .ToImmutableList();

        IAutoLogonSettings autoLogon = s.AutoLogon switch
        {
            AutoLogonMode.Builtin => new BuiltinAutoLogonSettings(s.AdministratorPassword),
            AutoLogonMode.None => new NoneAutoLogonSettings(),
            _ => new OwnAutoLogonSettings()
        };

        return new UnattendedAccountSettings(accounts, autoLogon, s.ObscurePasswords);
    }

    private static IPESettings Pe(UnattendSettings s, UnattendGenerator g)
    {
        switch (s.PeMode)
        {
            case PeMode.Script:
                return new ScriptPESetttings(s.PeScript);

            case PeMode.Generate:
                return new GeneratePESettings(
                    PartitionSettings: Partition(s),
                    DiskAssertionSettings: s.AssertMode switch
                    {
                        AssertMode.Generated => new GeneratedDiskAssertionsSettings(
                            MinSizeGiB: s.AssertCapacity ? s.AssertMinGiB : null,
                            MaxSizeGiB: s.AssertCapacity ? s.AssertMaxGiB : null,
                            AssertNoPartitions: s.AssertNoPartitions,
                            AssertInterfaceType: s.AssertInterfaceType,
                            AssertMediaType: s.AssertMediaType),
                        AssertMode.Script => new ScriptDiskAssertionsSettings(s.AssertScript),
                        _ => new SkipDiskAssertionSettings()
                    },
                    InstallFromSettings: s.ImageMode switch
                    {
                        ImageMode.Name => new NameInstallFromSettings(s.ImageName),
                        ImageMode.Index => new IndexInstallFromSettings(s.ImageIndex),
                        ImageMode.Interactive => new InteractiveInstallFromSettings(),
                        _ => new EditionInstallFromSettings(g.Lookup<WindowsEdition>(s.ImageEdition))
                    },
                    PagingFileSettings: s.PagingMode switch
                    {
                        PagingMode.Custom => new CustomPagingFileSettings(s.PagingInitialMiB, s.PagingMaxMiB),
                        PagingMode.None => new NoPagingFileSettings(),
                        _ => new AutomaticPagingFileSettings()
                    },
                    DisableDefender: s.DisableDefender,
                    Disable8Dot3Names: s.Disable8Dot3Names,
                    PauseBeforeFormatting: s.PauseBeforeFormatting,
                    PauseBeforeReboot: s.PauseBeforeReboot,
                    CompactOs: s.CompactOs,
                    SkipIntegrityCheck: s.SkipIntegrityCheck);

            default:
                return new DefaultPESettings(
                    EditionSettings: s.EditionMode switch
                    {
                        EditionMode.Generic => new UnattendedEditionSettings(g.Lookup<WindowsEdition>(s.GenericEdition)),
                        EditionMode.Custom => new CustomEditionSettings(new ProductKey(s.CustomProductKey.Trim())),
                        EditionMode.Firmware => new FirmwareEditionSettings(),
                        _ => new InteractiveEditionSettings()
                    },
                    BypassRequirementsCheck: s.BypassRequirements);
        }
    }

    private static IPartitionSettings Partition(UnattendSettings s)
    {
        switch (s.PartitionMode)
        {
            case PartitionMode.Custom: return new CustomPartitionSettings(s.DiskpartScript);
            case PartitionMode.Interactive: return new InteractivePartitionSettings();
        }

        ITargetDiskSettings target = s.TargetDiskMode switch
        {
            TargetDiskMode.Script => new ScriptTargetDiskSettings(s.TargetDiskScript),
            TargetDiskMode.Interactive => new InteractiveTargetDiskSettings(),
            _ => new GeneratedTargetDiskSettings(
                MinSizeGiB: s.TargetCapacity ? s.TargetMinGiB : null,
                MaxSizeGiB: s.TargetCapacity ? s.TargetMaxGiB : null,
                Index: s.TargetIndexEnabled ? s.TargetIndex : null,
                AssertNoPartitions: s.TargetNoPartitions,
                AssertInterfaceType: s.TargetInterfaceType,
                AssertMediaType: s.TargetMediaType)
        };

        return new UnattendedPartitionSettings(target, s.PartitionLayout, s.RecoveryMode,
            s.SystemPartitionSize, s.RecoveryPartitionSize);
    }

    private static string WingetScript(IEnumerable<string> ids)
    {
        var list = string.Join("\n", ids.Select(id => $"\t'{id}';"));
        return $$"""
            $deadline = [datetime]::Now.AddMinutes( 15 );
            while( -not ( Get-Command -Name 'winget.exe' -ErrorAction 'SilentlyContinue' ) -and [datetime]::Now -lt $deadline ) {
            	Start-Sleep -Seconds 15;
            }
            foreach( $id in @(
            {{list}}
            ) ) {
            	winget.exe {{Apps.WingetCatalog.InstallArguments("$id")}};
            }
            """;
    }

    private static Color ParseColor(string html)
    {
        try { return ColorTranslator.FromHtml(html.Trim()); }
        catch (Exception ex) { throw new ConfigurationException($"Color '{html}' is invalid: {ex.Message}"); }
    }
}
