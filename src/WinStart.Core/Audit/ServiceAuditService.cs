using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;
using WinStart.Core.Tweaks;

namespace WinStart.Core.Audit;

public enum ServiceStart { Boot = 0, System = 1, Auto = 2, Manual = 3, Disabled = 4 }

public sealed record ServiceStartType(ServiceStart Start, bool Delayed)
{
    public string Key => Start == ServiceStart.Auto && Delayed ? "Delayed" : Start.ToString();

    public bool SameAs(ServiceStartType other) =>
        Start == other.Start && (Start != ServiceStart.Auto || Delayed == other.Delayed);
}

public sealed record ServiceDeviation(
    string Name,
    string DisplayName,
    ServiceStartType Current,
    ServiceStartType Default,
    DateTime? ChangedAt,
    string? WinStartTweakId);

public interface IServiceAuditService
{
    bool IsSupported { get; }

    Task<IReadOnlyList<ServiceDeviation>> ScanAsync(CancellationToken ct);

    Task<string?> RestoreAsync(ServiceDeviation deviation, CancellationToken ct);
}

public sealed partial class ServiceAuditService(
    IProcessRunner process,
    IRegistryService registry,
    IJournalService journal,
    ISystemInfoService system,
    ILocalizationService loc,
    ITweakService tweakService,
    TweakRegistry tweaks) : IServiceAuditService
{
    private const string ServicesPath = @"SYSTEM\CurrentControlSet\Services";
    private const int MinBuild = 26100;

    private static readonly HashSet<string> ManagedByWindows = new(StringComparer.OrdinalIgnoreCase)
    {
        "BITS", "TrustedInstaller", "WlanSvc", "DoSvc", "PcaSvc",
        "tzautoupdate", "ssh-agent", "WinRM", "W32Time", "Netlogon"
    };

    private static readonly Dictionary<string, string> WinStartChanges = new(StringComparer.OrdinalIgnoreCase)
    {
        ["DiagTrack"] = "misc.telemetry",
        ["dmwappushservice"] = "misc.telemetry",
        ["WerSvc"] = "misc.telemetry",
        ["AppXSvc"] = "apps.debloat",
        ["EventSystem"] = "optimization.delayedStart",
        ["NlaSvc"] = "optimization.delayedStart"
    };

    private static readonly Lazy<IReadOnlyDictionary<string, ServiceStartType>> Defaults = new(LoadDefaults);

    public bool IsSupported => system.Build >= MinBuild;

    public async Task<IReadOnlyList<ServiceDeviation>> ScanAsync(CancellationToken ct)
    {
        if (!IsSupported) return [];

        var deviations = await Task.Run(FindDeviations, ct).ConfigureAwait(false);
        if (deviations.Count == 0) return [];

        var (names, changed) = await QueryAsync(ct).ConfigureAwait(false);

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var id in deviations.Select(d => WinStartChanges.GetValueOrDefault(d.Name)).OfType<string>().Distinct())
            if (await IsAppliedAsync(id, ct).ConfigureAwait(false))
                applied.Add(id);

        return deviations
            .Select(d => new ServiceDeviation(d.Name, DisplayName(d.Name, names), d.Current, d.Default,
                changed.TryGetValue(d.Name, out var at) ? at : null,
                WinStartChanges.TryGetValue(d.Name, out var tweak) && applied.Contains(tweak) ? tweak : null))
            .OrderBy(d => d.WinStartTweakId is not null)
            .ThenBy(d => d.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<string?> RestoreAsync(ServiceDeviation deviation, CancellationToken ct)
    {
        var path = $@"HKLM\{ServicesPath}\{deviation.Name}";
        var snapshots = new List<RegistryValueSnapshot>
        {
            registry.Snapshot(path, "Start"),
            registry.Snapshot(path, "DelayedAutostart")
        };

        var mode = deviation.Default switch
        {
            { Start: ServiceStart.Auto, Delayed: true } => "delayed-auto",
            { Start: ServiceStart.Auto } => "auto",
            { Start: ServiceStart.Manual } => "demand",
            { Start: ServiceStart.Disabled } => "disabled",
            { Start: ServiceStart.System } => "system",
            _ => "boot"
        };

        var command = $"sc config \"{deviation.Name}\" start= {mode}";
        var result = await process.CmdAsync(command, ct, 30000).ConfigureAwait(false);
        if (!Matches(deviation.Name, deviation.Default))
            result = await process.TrustedInstallerAsync(command, ct, 30000).ConfigureAwait(false);
        if (!Matches(deviation.Name, deviation.Default))
            WriteDirectly(deviation.Name, deviation.Default);

        var ok = Matches(deviation.Name, deviation.Default);
        var error = ok ? null : FirstLine(result) ?? $"exit code {result.ExitCode}";

        await journal.AppendAsync(new JournalEntry
        {
            TweakId = $"services.audit.{deviation.Name}",
            TitleKey = "services.journal",
            Category = TweakCategory.Optimization,
            Direction = TweakDirection.Apply,
            Status = ok ? TweakStatus.Success : TweakStatus.Failed,
            Reversible = ok,
            Message = loc.Format("services.journal.message", deviation.DisplayName,
                loc[$"services.start.{deviation.Current.Key}"], loc[$"services.start.{deviation.Default.Key}"]),
            Log = [$"{DateTime.Now:HH:mm:ss}  {command} → {result.ExitCode}"],
            RegistrySnapshots = snapshots
        }).ConfigureAwait(false);

        return error;
    }

    private async Task<bool> IsAppliedAsync(string tweakId, CancellationToken ct)
    {
        if (journal.FindActiveApplies(tweakId).Count > 0) return true;
        if (tweaks.ById(tweakId) is not { } def) return false;

        try { return await tweakService.DetectAsync(def, ct).ConfigureAwait(false) == TweakState.Applied; }
        catch { return false; }
    }

    private static List<(string Name, ServiceStartType Current, ServiceStartType Default)> FindDeviations()
    {
        var list = new List<(string, ServiceStartType, ServiceStartType)>();
        using var root = Registry.LocalMachine.OpenSubKey(ServicesPath);
        if (root is null) return list;

        foreach (var (name, expected) in Defaults.Value)
        {
            if (ManagedByWindows.Contains(name)) continue;
            var current = Read(root, name);
            if (current is not null && !current.SameAs(expected)) list.Add((name, current, expected));
        }

        return list;
    }

    private static ServiceStartType? Read(RegistryKey root, string name)
    {
        try
        {
            using var key = root.OpenSubKey(name);
            if (key?.GetValue("Start") is not int start || start is < 0 or > 4) return null;
            var delayed = start == 2 && key.GetValue("DelayedAutostart") is int d && d == 1;
            return new ServiceStartType((ServiceStart)start, delayed);
        }
        catch { return null; }
    }

    private static bool Matches(string name, ServiceStartType expected)
    {
        using var root = Registry.LocalMachine.OpenSubKey(ServicesPath);
        return root is not null && Read(root, name) is { } current && current.SameAs(expected);
    }

    private static void WriteDirectly(string name, ServiceStartType target)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"{ServicesPath}\{name}", writable: true);
            if (key is null) return;
            key.SetValue("Start", (int)target.Start, RegistryValueKind.DWord);
            if (target.Start == ServiceStart.Auto) key.SetValue("DelayedAutostart", target.Delayed ? 1 : 0, RegistryValueKind.DWord);
        }
        catch { }
    }

    private static string? FirstLine(ProcessResult r)
    {
        var text = (string.IsNullOrWhiteSpace(r.StdErr) ? r.StdOut : r.StdErr).Trim();
        return text.Length == 0 ? null : text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0);
    }

    private static string DisplayName(string name, IReadOnlyDictionary<string, string> names)
    {
        if (names.TryGetValue(name, out var display) && display.Length > 0) return display;

        var instance = names.FirstOrDefault(n => n.Key.StartsWith(name + "_", StringComparison.OrdinalIgnoreCase));
        return instance.Value is { Length: > 0 } text ? UserServiceSuffix().Replace(text, "") : name;
    }

    [GeneratedRegex(@"_[0-9a-fA-F]{4,}$")]
    private static partial Regex UserServiceSuffix();

    private async Task<(IReadOnlyDictionary<string, string> Names, IReadOnlyDictionary<string, DateTime> Changed)> QueryAsync(
        CancellationToken ct)
    {
        const string script = """
            $ErrorActionPreference = 'SilentlyContinue'
            $names = @{}
            Get-CimInstance Win32_Service | ForEach-Object { $names[[string]$_.Name] = [string]$_.DisplayName }
            $changed = @{}
            Get-WinEvent -FilterHashtable @{ LogName = 'System'; ProviderName = 'Service Control Manager'; Id = 7040 } -MaxEvents 5000 |
              ForEach-Object {
                $n = [string]$_.Properties[3].Value
                if ($n -and -not $changed.ContainsKey($n)) { $changed[$n] = $_.TimeCreated.ToUniversalTime().ToString('o') }
              }
            $json = ConvertTo-Json -InputObject @{ names = $names; changed = $changed } -Compress -Depth 3
            'B64:' + [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($json))
            """;

        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var changed = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var r = await process.PowerShellAsync(script, ct, 120000).ConfigureAwait(false);
            var marker = r.StdOut.IndexOf("B64:", StringComparison.Ordinal);
            if (marker < 0) return (names, changed);

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(r.StdOut[(marker + 4)..].Trim()));
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("names", out var n) && n.ValueKind == JsonValueKind.Object)
                foreach (var p in n.EnumerateObject())
                    names[p.Name] = p.Value.GetString() ?? "";

            if (doc.RootElement.TryGetProperty("changed", out var c) && c.ValueKind == JsonValueKind.Object)
                foreach (var p in c.EnumerateObject())
                    if (DateTime.TryParse(p.Value.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var at))
                        changed[p.Name] = at.ToLocalTime();
        }
        catch { }

        return (names, changed);
    }

    private static IReadOnlyDictionary<string, ServiceStartType> LoadDefaults()
    {
        var result = new Dictionary<string, ServiceStartType>(StringComparer.OrdinalIgnoreCase);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("WinStart.Core.Audit.service-defaults.json");
        if (stream is null) return result;

        using var doc = JsonDocument.Parse(stream);
        foreach (var p in doc.RootElement.GetProperty("services").EnumerateObject())
        {
            var start = p.Value[0].GetInt32();
            var delayed = p.Value[1].GetBoolean();
            if (start is >= 0 and <= 4) result[p.Name] = new ServiceStartType((ServiceStart)start, delayed);
        }

        return result;
    }
}
