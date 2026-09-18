using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.Core.Tweaks;

internal static class MiscTweaks
{
    private const string StickyKeys = @"HKCU\Control Panel\Accessibility\StickyKeys";
    private const string ContentDelivery = @"HKCU\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager";
    private const string Advanced = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    private const string Explorer = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer";
    private const string StartMenu = @"HKCU\Software\Microsoft\Windows\CurrentVersion\Start";

    private const string DataCollectionPolicy = @"HKLM\Software\Policies\Microsoft\Windows\DataCollection";
    private const string DataCollection = @"HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\DataCollection";
    private const string AppCompat = @"HKLM\Software\Policies\Microsoft\Windows\AppCompat";
    private const string SystemPolicy = @"HKLM\Software\Policies\Microsoft\Windows\System";
    private const string ErrorReporting = @"HKLM\Software\Microsoft\Windows\Windows Error Reporting";
    private const string ErrorReportingPolicy = @"HKLM\Software\Policies\Microsoft\Windows\Windows Error Reporting";
    private const string InputPersonalization = @"HKCU\Software\Microsoft\InputPersonalization";
    private const string AutoLoggers = @"HKLM\System\CurrentControlSet\Control\WMI\Autologger";
    private const string SiufRules = @"HKCU\Software\Microsoft\Siuf\Rules";
    private const string MachineEnvironment = @"HKLM\System\CurrentControlSet\Control\Session Manager\Environment";
    private const string DefenderService = @"HKLM\System\CurrentControlSet\Services\WinDefend";

    private static readonly (string Name, string RevertStart)[] TelemetryServices =
    [
        ("DiagTrack", "auto"),
        ("dmwappushservice", "demand"),
        ("WerSvc", "demand")
    ];

    private static readonly string[] TelemetryTasks =
    [
        @"\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
        @"\Microsoft\Windows\Application Experience\ProgramDataUpdater",
        @"\Microsoft\Windows\Application Experience\PcaPatchDbTask",
        @"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
        @"\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
        @"\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
        @"\Microsoft\Windows\Autochk\Proxy",
        @"\Microsoft\Windows\Windows Error Reporting\QueueReporting",
        @"\Microsoft\Windows\Feedback\Siuf\DmClient",
        @"\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
    ];

    public static IEnumerable<TweakDefinition> All()
    {
        yield return DisableStickyKeys();
        yield return Recommendations();
        yield return Telemetry();
    }

    private static TweakDefinition DisableStickyKeys() => new()
    {
        Id = "misc.stickyKeys",
        Category = TweakCategory.Misc,
        Order = 1,
        RegistryActions = [RegistryAction.SetString(StickyKeys, "Flags", "506")],
        RevertActions = [RegistryAction.SetString(StickyKeys, "Flags", "510")]
    };

    private static TweakDefinition Recommendations() => new()
    {
        Id = "misc.recommendations",
        Category = TweakCategory.Misc,
        Order = 2,
        NeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(ContentDelivery, "SubscribedContent-338389Enabled", 0),
            RegistryAction.Set(ContentDelivery, "SubscribedContent-310093Enabled", 0),
            RegistryAction.Set(ContentDelivery, "ContentDeliveryAllowed", 0),
            RegistryAction.Set(ContentDelivery, "SystemPaneSuggestionsEnabled", 0),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\UserProfileEngagement",
                "ScoobeSystemSettingEnabled", 0),
            RegistryAction.Set(StartMenu, "ShowRecentList", 0),
            RegistryAction.Set(StartMenu, "ShowFrequentList", 0),
            RegistryAction.Set(Advanced, "Start_TrackDocs", 0),
            RegistryAction.Set(Advanced, "Start_IrisRecommendations", 0),
            RegistryAction.Set(Advanced, "Start_AccountNotifications", 0),
            RegistryAction.Set(Explorer, "ShowRecent", 0),
            RegistryAction.Set(Explorer, "ShowFrequent", 0),
            RegistryAction.Set(Explorer, "ShowCloudFilesInQuickAccess", 0),
            RegistryAction.Set(Explorer, "ShowRecommendations", 0),
            RegistryAction.Set(@"HKCU\Control Panel\International\User Profile", "HttpAcceptLanguageOptOut", 1),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\SearchSettings",
                "IsDeviceSearchHistoryEnabled", 0)
        ]
    };

    private static TweakDefinition Telemetry() => new()
    {
        Id = "misc.telemetry",
        Category = TweakCategory.Misc,
        Order = 3,
        ExtendedOptionKey = "option.telemetry.appLaunches",
        ExtendedNeedsExplorerRestart = true,
        RegistryActions =
        [
            RegistryAction.Set(DataCollectionPolicy, "AllowTelemetry", 0),
            RegistryAction.Set(DataCollectionPolicy, "MaxTelemetryAllowed", 0),
            RegistryAction.Set(DataCollectionPolicy, "AllowDeviceNameInTelemetry", 0),
            RegistryAction.Set(DataCollectionPolicy, "AllowCommercialDataPipeline", 0),
            RegistryAction.Set(DataCollectionPolicy, "DisableOneSettingsDownloads", 1),
            RegistryAction.Set(DataCollectionPolicy, "LimitDumpCollection", 1),
            RegistryAction.Set(DataCollectionPolicy, "LimitDiagnosticLogCollection", 1),
            RegistryAction.Set(DataCollectionPolicy, "DoNotShowFeedbackNotifications", 1),
            RegistryAction.Set(DataCollection, "AllowTelemetry", 0),
            RegistryAction.Set(DataCollection, "DoNotShowFeedbackNotifications", 1),

            RegistryAction.Set(AppCompat, "AITEnable", 0),
            RegistryAction.Set(AppCompat, "DisableInventory", 1),
            RegistryAction.Set(AppCompat, "DisableUAR", 1),

            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\SQMClient\Windows", "CEIPEnable", 0),
            RegistryAction.Set(@"HKLM\Software\Microsoft\SQMClient\Windows", "CEIPEnable", 0),

            RegistryAction.Set(ErrorReporting, "Disabled", 1),
            RegistryAction.Set(ErrorReportingPolicy, "Disabled", 1),

            RegistryAction.Set(SystemPolicy, "EnableActivityFeed", 0),
            RegistryAction.Set(SystemPolicy, "PublishUserActivities", 0),
            RegistryAction.Set(SystemPolicy, "UploadUserActivities", 0),

            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled", 0),
            RegistryAction.Set(@"HKLM\Software\Policies\Microsoft\Windows\AdvertisingInfo", "DisabledByGroupPolicy", 1),
            RegistryAction.Set(@"HKCU\Software\Policies\Microsoft\Windows\CloudContent",
                "DisableTailoredExperiencesWithDiagnosticData", 1),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Windows\CurrentVersion\Privacy",
                "TailoredExperiencesWithDiagnosticDataEnabled", 0),

            RegistryAction.Set(InputPersonalization, "RestrictImplicitTextCollection", 1),
            RegistryAction.Set(InputPersonalization, "RestrictImplicitInkCollection", 1),
            RegistryAction.Set($@"{InputPersonalization}\TrainedDataStore", "HarvestContacts", 0),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Input\TIPC", "Enabled", 0),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Speech_OneCore\Settings\OnlineSpeechPrivacy", "HasAccepted", 0),
            RegistryAction.Set(@"HKCU\Software\Microsoft\Personalization\Settings", "AcceptedPrivacyPolicy", 0),
            RegistryAction.Set(SiufRules, "NumberOfSIUFInPeriod", 0),
            RegistryAction.DeleteValue(SiufRules, "PeriodInNanoSeconds"),

            RegistryAction.Set($@"{AutoLoggers}\AutoLogger-Diagtrack-Listener", "Start", 0),
            RegistryAction.Set($@"{AutoLoggers}\SQMLogger", "Start", 0),

            RegistryAction.SetString(MachineEnvironment, "POWERSHELL_TELEMETRY_OPTOUT", "1")
        ],
        CustomApply = async (ctx, ct) =>
        {
            NativeMethods.BroadcastSettingChange("Environment");

            if (ctx.Extended)
            {
                ctx.Backup(Advanced, "Start_TrackProgs");
                ctx.Registry.SetValue(Advanced, "Start_TrackProgs", 0, RegistryValueKind.DWord);
                ctx.Log($@"set  {Advanced}\Start_TrackProgs = 0");
                ctx.RequestExplorerRestart();
            }

            foreach (var (service, _) in TelemetryServices)
            {
                ctx.Progress(ctx.Text("log.serviceOff", service), null);
                await ctx.Process.CmdAsync($"sc stop {service}", ct, 60000).ConfigureAwait(false);
                var r = await ctx.Process.CmdAsync($"sc config {service} start= disabled", ct, 60000).ConfigureAwait(false);
                ctx.Log($"sc config {service} → {r.ExitCode}");
            }

            await ForTasksAsync(ctx, "/Disable", ct).ConfigureAwait(false);
            await SetSampleSubmissionAsync(ctx, 2, ct).ConfigureAwait(false);
        },
        CustomRevert = async (ctx, ct) =>
        {
            if (ctx.Extended)
            {
                ctx.RequestExplorerRestart();
            }
            else if (!ctx.RestoredFromBackup && ctx.Registry.GetInt(Advanced, "Start_TrackProgs") == 0)
            {
                ctx.Registry.DeleteValue(Advanced, "Start_TrackProgs");
                ctx.Log($@"del  {Advanced}\Start_TrackProgs");
                ctx.RequestExplorerRestart();
            }

            NativeMethods.BroadcastSettingChange("Environment");

            foreach (var (service, start) in TelemetryServices)
            {
                ctx.Progress(ctx.Text("log.serviceOn", service), null);
                var r = await ctx.Process.CmdAsync($"sc config {service} start= {start}", ct, 60000).ConfigureAwait(false);
                ctx.Log($"sc config {service} → {r.ExitCode}");
            }

            await ForTasksAsync(ctx, "/Enable", ct).ConfigureAwait(false);
            await SetSampleSubmissionAsync(ctx, 1, ct).ConfigureAwait(false);
        }
    };

    private static async Task SetSampleSubmissionAsync(ITweakContext ctx, int consent, CancellationToken ct)
    {
        if (!ctx.Registry.KeyExists(DefenderService))
        {
            ctx.Log(ctx.Text("log.defenderMissing"));
            return;
        }

        var command = $"Set-MpPreference -SubmitSamplesConsent {consent}";
        var r = await ctx.Process.PowerShellAsync(command, ct, 60000).ConfigureAwait(false);
        ctx.Log($"{command} → {r.ExitCode}");
    }

    private static async Task ForTasksAsync(ITweakContext ctx, string switchName, CancellationToken ct)
    {
        foreach (var task in TelemetryTasks)
        {
            ct.ThrowIfCancellationRequested();
            var r = await ctx.Process.RunAsync("schtasks.exe", $"/Change /TN \"{task}\" {switchName}", ct, timeoutMs: 60000)
                .ConfigureAwait(false);
            ctx.Log($"{task} → {r.ExitCode}");
        }
    }
}
