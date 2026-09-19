using System.Text.Json.Serialization;
using Schneegans.Unattend;

namespace WinStart.Core.Unattend;

public enum PeMode { Interactive, Generate, Script }
public enum EditionMode { Interactive, Generic, Custom, Firmware }
public enum ImageMode { Edition, Name, Index, Interactive }
public enum PartitionMode { Unattended, Custom, Interactive }
public enum TargetDiskMode { Criteria, Script, Interactive }
public enum AssertMode { Skip, Generated, Script }
public enum PagingMode { Automatic, Custom, None }
public enum ComputerNameMode { Random, Custom, Script }
public enum AccountMode { Unattended, InteractiveMicrosoft, InteractiveLocal }
public enum AutoLogonMode { Own, Builtin, None }
public enum PasswordExpirationMode { Unlimited, Default, Custom }
public enum LockoutMode { Default, Disable, Custom }
public enum LayoutMode { Default, Empty, Custom }
public enum EffectsMode { Default, BestAppearance, BestPerformance, Custom }
public enum WifiMode { Interactive, Skip, Parameters, Xml }
public enum StickyMode { Default, Disabled, Custom }
public enum WallpaperMode { Default, Solid, Script }

public sealed class AccountRow
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Password { get; set; } = "";
    public string Group { get; set; } = Constants.AdministratorsGroup;
}

public sealed class ScriptRow
{
    public ScriptType Type { get; set; } = ScriptType.Ps1;
    public string Content { get; set; } = "";
}

public sealed class UnattendSettings
{
    public const int CurrentVersion = 2;

    public static IReadOnlyDictionary<int, Action<System.Text.Json.Nodes.JsonObject>> Migrations { get; } =
        new Dictionary<int, Action<System.Text.Json.Nodes.JsonObject>>();

    public static bool TryMigrate(System.Text.Json.Nodes.JsonObject node)
    {
        var version = node["Version"] is { } v && v.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? v.GetValue<int>()
            : 1;

        while (version < CurrentVersion)
        {
            if (!Migrations.TryGetValue(version, out var step)) return false;
            step(node);
            version++;
            node["Version"] = version;
        }

        return version == CurrentVersion;
    }

    public int Version { get; set; } = CurrentVersion;

    // ---- Регион и язык ----
    public bool LanguageInteractive { get; set; }
    public string DisplayLanguage { get; set; } = "ru-RU";
    public string Locale1 { get; set; } = "ru-RU";
    public string Keyboard1 { get; set; } = "00000419";
    public bool UseLocale2 { get; set; } = true;
    public string Locale2 { get; set; } = "en-US";
    public string Keyboard2 { get; set; } = "00000409";
    public bool UseLocale3 { get; set; }
    public string Locale3 { get; set; } = "en-US";
    public string Keyboard3 { get; set; } = "00000409";
    public string GeoLocation { get; set; } = "203";

    // ---- Этап Windows PE ----
    public PeMode PeMode { get; set; } = PeMode.Interactive;
    public EditionMode EditionMode { get; set; } = EditionMode.Interactive;
    public string GenericEdition { get; set; } = "pro";
    public string CustomProductKey { get; set; } = "";

    public bool Disable8Dot3Names { get; set; }
    public bool DisableDefender { get; set; }
    public bool PauseBeforeFormatting { get; set; }
    public bool PauseBeforeReboot { get; set; }
    public bool CompactOs { get; set; }
    public bool SkipIntegrityCheck { get; set; }

    public ImageMode ImageMode { get; set; } = ImageMode.Edition;
    public string ImageEdition { get; set; } = "pro";
    public string ImageName { get; set; } = "";
    public int ImageIndex { get; set; } = 1;

    public PartitionMode PartitionMode { get; set; } = PartitionMode.Unattended;
    public TargetDiskMode TargetDiskMode { get; set; } = TargetDiskMode.Criteria;
    public bool TargetNoPartitions { get; set; } = true;
    public bool TargetInterfaceType { get; set; }
    public bool TargetMediaType { get; set; }
    public bool TargetCapacity { get; set; }
    public int TargetMinGiB { get; set; } = Constants.TargetDiskMinSizeGiB;
    public int TargetMaxGiB { get; set; } = Constants.TargetDiskMaxSizeGiB;
    public bool TargetIndexEnabled { get; set; } = true;
    public int TargetIndex { get; set; }
    public string TargetDiskScript { get; set; } = "";
    public PartitionLayout PartitionLayout { get; set; } = PartitionLayout.Automatic;
    public int SystemPartitionSize { get; set; } = Constants.SystemPartitionSize;
    public RecoveryMode RecoveryMode { get; set; } = RecoveryMode.Partition;
    public int RecoveryPartitionSize { get; set; } = Constants.RecoveryPartitionSize;
    public string DiskpartScript { get; set; } = "";

    public AssertMode AssertMode { get; set; } = AssertMode.Skip;
    public bool AssertCapacity { get; set; }
    public int AssertMinGiB { get; set; } = Constants.DiskAssertionMinSizeGiB;
    public int AssertMaxGiB { get; set; } = Constants.DiskAssertionMaxSizeGiB;
    public bool AssertNoPartitions { get; set; } = true;
    public bool AssertInterfaceType { get; set; }
    public bool AssertMediaType { get; set; }
    public string AssertScript { get; set; } = "";

    public PagingMode PagingMode { get; set; } = PagingMode.Automatic;
    public int PagingInitialMiB { get; set; } = 1024;
    public int PagingMaxMiB { get; set; } = 4096;

    public string PeScript { get; set; } = "";

    // ---- Активация ----
    public string ActivationKey { get; set; } = "";

    // ---- Архитектуры ----
    public bool ArchX86 { get; set; }
    public bool ArchAmd64 { get; set; } = true;
    public bool ArchArm64 { get; set; }

    // ---- Параметры установки ----
    public bool BypassRequirements { get; set; } = true;
    public bool BypassNetwork { get; set; } = true;
    public bool UseConfigurationSet { get; set; }
    public bool HidePowerShellWindows { get; set; }
    public bool KeepSensitiveFiles { get; set; }
    public bool UseNarrator { get; set; }

    // ---- Имя компьютера, часовой пояс ----
    public ComputerNameMode ComputerNameMode { get; set; } = ComputerNameMode.Random;
    public string ComputerName { get; set; } = "";
    public string ComputerNameScript { get; set; } = "";
    public bool TimeZoneExplicit { get; set; } = true;
    public string TimeZone { get; set; } = "Russian Standard Time";

    // ---- Учётные записи ----
    public AccountMode AccountMode { get; set; } = AccountMode.Unattended;
    public List<AccountRow> Accounts { get; set; } = DefaultAccounts();
    public AutoLogonMode AutoLogon { get; set; } = AutoLogonMode.Own;
    public string AdministratorPassword { get; set; } = "";
    public bool ObscurePasswords { get; set; }

    public PasswordExpirationMode PasswordExpiration { get; set; } = PasswordExpirationMode.Unlimited;
    public int PasswordMaxAge { get; set; } = DefaultPasswordExpirationSettings.MaxAge;

    public LockoutMode LockoutMode { get; set; } = LockoutMode.Default;
    public int LockoutThreshold { get; set; } = 10;
    public int LockoutWindow { get; set; } = 10;
    public int LockoutDuration { get; set; } = 10;

    // ---- Проводник ----
    public HideModes HideFiles { get; set; } = HideModes.Hidden;
    public bool ShowFileExtensions { get; set; } = true;
    public bool ClassicContextMenu { get; set; }
    public bool HideInfoTip { get; set; }
    public bool LaunchToThisPC { get; set; } = true;
    public bool ShowEndTask { get; set; } = true;

    // ---- Пуск и панель задач ----
    public TaskbarSearchMode TaskbarSearch { get; set; } = TaskbarSearchMode.Hide;
    public LayoutMode TaskbarIcons { get; set; } = LayoutMode.Empty;
    public string TaskbarIconsXml { get; set; } = "";
    public bool DisableWidgets { get; set; } = true;
    public bool LeftTaskbar { get; set; } = true;
    public bool HideTaskViewButton { get; set; } = true;
    public bool ShowAllTrayIcons { get; set; }
    public bool DisableBingResults { get; set; } = true;
    public LayoutMode StartTiles { get; set; } = LayoutMode.Default;
    public string StartTilesXml { get; set; } = "";
    public LayoutMode StartPins { get; set; } = LayoutMode.Empty;
    public string StartPinsJson { get; set; } = "";

    // ---- Системные настройки ----
    public bool DisableWindowsUpdate { get; set; }
    public bool DisableUac { get; set; } = true;
    public bool DisableSac { get; set; } = true;
    public bool DisableSmartScreen { get; set; } = true;
    public bool DisableFastStartup { get; set; } = true;
    public bool DisableSystemRestore { get; set; } = true;
    public bool EnableLongPaths { get; set; } = true;
    public bool EnableRemoteDesktop { get; set; }
    public bool HardenSystemDriveAcl { get; set; }
    public bool DeleteJunctions { get; set; } = true;
    public bool AllowPowerShellScripts { get; set; } = true;
    public bool DisableLastAccess { get; set; }
    public bool PreventAutomaticReboot { get; set; }
    public bool TurnOffSystemSounds { get; set; } = true;
    public bool DisableAppSuggestions { get; set; }
    public bool PreventDeviceEncryption { get; set; } = true;
    public bool HideEdgeFre { get; set; } = true;
    public bool DisableEdgeStartupBoost { get; set; } = true;
    public bool MakeEdgeUninstallable { get; set; } = true;
    public bool DisablePointerPrecision { get; set; } = true;
    public bool DeleteWindowsOld { get; set; } = true;
    public bool DisableAutomaticRestartSignOn { get; set; }
    public bool DisableWpbt { get; set; }
    public bool PreventDeviceApps { get; set; }
    public bool ProcessAudit { get; set; }
    public bool ProcessAuditCommandLine { get; set; }

    // ---- Визуальные эффекты ----
    public EffectsMode EffectsMode { get; set; } = EffectsMode.BestAppearance;
    public Dictionary<string, bool> Effects { get; set; } = AllOn(Enum.GetNames<Effect>());

    // ---- Значки рабочего стола, папки «Пуска» ----
    public bool DeleteEdgeDesktopIcon { get; set; } = true;
    public bool DesktopIconsCustom { get; set; } = true;
    public Dictionary<string, bool> DesktopIcons { get; set; } = new(StringComparer.Ordinal);
    public bool StartFoldersCustom { get; set; } = true;
    public Dictionary<string, bool> StartFolders { get; set; } = AllOn(["Settings", "FileExplorer", "Documents", "Downloads", "Network"]);

    // ---- Виртуализация ----
    public bool DisableCoreIsolation { get; set; } = true;
    public bool VBoxGuestAdditions { get; set; }
    public bool VMwareTools { get; set; }
    public bool VirtIoGuestTools { get; set; }
    public bool ParallelsTools { get; set; }

    // ---- Wi‑Fi, экспресс-параметры ----
    public WifiMode WifiMode { get; set; } = WifiMode.Skip;
    public string WifiSsid { get; set; } = "";
    public bool WifiNonBroadcast { get; set; }
    public WifiAuthentications WifiAuthentication { get; set; } = WifiAuthentications.WPA2PSK;
    public string WifiPassword { get; set; } = "";
    public string WifiXml { get; set; } = "";
    public ExpressSettingsMode ExpressSettings { get; set; } = ExpressSettingsMode.DisableAll;

    // ---- Клавиши-переключатели, залипание ----
    public bool LockKeysCustom { get; set; }
    public LockKeyInitial CapsLockInitial { get; set; } = LockKeyInitial.Off;
    public LockKeyBehavior CapsLockBehavior { get; set; } = LockKeyBehavior.Toggle;
    public LockKeyInitial NumLockInitial { get; set; } = LockKeyInitial.On;
    public LockKeyBehavior NumLockBehavior { get; set; } = LockKeyBehavior.Toggle;
    public LockKeyInitial ScrollLockInitial { get; set; } = LockKeyInitial.Off;
    public LockKeyBehavior ScrollLockBehavior { get; set; } = LockKeyBehavior.Toggle;
    public StickyMode StickyMode { get; set; } = StickyMode.Disabled;
    public Dictionary<string, bool> StickyFlags { get; set; } = AllOn(Enum.GetNames<StickyKeys>());

    // ---- Оформление ----
    public bool ColorsCustom { get; set; }
    public ColorTheme SystemTheme { get; set; } = ColorTheme.Dark;
    public ColorTheme AppsTheme { get; set; } = ColorTheme.Dark;
    public string AccentColor { get; set; } = "#0078D4";
    public bool AccentColorOnStart { get; set; }
    public bool AccentColorOnBorders { get; set; }
    public bool EnableTransparency { get; set; } = true;
    public WallpaperMode WallpaperMode { get; set; } = WallpaperMode.Default;
    public string WallpaperColor { get; set; } = "#000000";
    public string WallpaperScript { get; set; } = "";
    public bool LockScreenScriptEnabled { get; set; }
    public string LockScreenScript { get; set; } = "";

    // ---- Удаление приложений ----
    public HashSet<string> Bloatware { get; set; } = new(DefaultBloatware, StringComparer.Ordinal);

    // ---- Установка программ после установки Windows ----
    public HashSet<string> WingetApps { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    // ---- Свои скрипты ----
    public List<ScriptRow> SystemScripts { get; set; } = Rows(4);
    public List<ScriptRow> DefaultUserScripts { get; set; } = Rows(3, ScriptType.Reg);
    public List<ScriptRow> FirstLogonScripts { get; set; } = Rows(4);
    public List<ScriptRow> UserOnceScripts { get; set; } = Rows(4);
    public bool RestartExplorer { get; set; }

    // ---- AppLocker, дополнительные компоненты ----
    public bool AppLockerEnabled { get; set; }
    public string AppLockerXml { get; set; } = "";

    public Dictionary<string, string> Components { get; set; } = new(StringComparer.Ordinal);

    // ---------------------------------------------------------------- значения по умолчанию

    private static List<AccountRow> DefaultAccounts()
    {
        var rows = Rows(5, () => new AccountRow());
        rows[0].Name = "User";
        return rows;
    }

    private static List<ScriptRow> Rows(int count, ScriptType type = ScriptType.Ps1) =>
        Rows(count, () => new ScriptRow { Type = type });

    private static List<T> Rows<T>(int count, Func<T> create) => Enumerable.Range(0, count).Select(_ => create()).ToList();

    private static Dictionary<string, bool> AllOn(IEnumerable<string> keys) =>
        keys.ToDictionary(k => k, _ => true, StringComparer.Ordinal);

    private static readonly string[] DefaultBloatware =
    [
        "Remove3DViewer", "RemoveBingSearch", "RemoveClipchamp", "RemoveClock", "RemoveCopilot", "RemoveCortana",
        "RemoveDevHome", "RemoveFamily", "RemoveFeedbackHub", "RemoveGameAssist", "RemoveGetHelp", "RemoveGetStarted",
        "RemoveHandwriting", "RemoveInternetExplorer", "RemoveMailCalendar", "RemoveMaps", "RemoveMathInputPanel",
        "RemoveMixedReality", "RemoveNews", "RemoveOffice365", "RemoveOneDrive", "RemoveOneNote", "RemoveOneSync",
        "RemoveOpenSSHClient", "RemoveOutlook", "RemovePaint3D", "RemovePeople", "RemovePowerAutomate", "RemovePowerShell2",
        "RemovePowerShellISE", "RemoveQuickAssist", "RemoveRdpClient", "RemoveRecall", "RemoveSkype", "RemoveSolitaire",
        "RemoveSpeech", "RemoveStepsRecorder", "RemoveStickyNotes", "RemoveTeams", "RemoveToDo", "RemoveVoiceRecorder",
        "RemoveWallet", "RemoveWeather", "RemoveWindowsHello", "RemoveWindowsMediaPlayer", "RemoveWindowsTerminal",
        "RemoveWordPad", "RemoveYourPhone", "RemoveZuneVideo"
    ];

    [JsonIgnore]
    public bool IsCurrentVersion => Version == CurrentVersion;
}
