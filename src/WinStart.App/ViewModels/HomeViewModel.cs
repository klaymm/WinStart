using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Wpf.Ui.Controls;
using WinStart.App.Infrastructure;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed class StatItem
{
    public required string Title { get; init; }
    public required string Value { get; init; }
    public string? Extra { get; init; }
    public SymbolRegular Icon { get; init; } = SymbolRegular.Info24;
}

public sealed partial class HomeViewModel : ObservableObject
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ILocalizationService _loc;

    public HomeViewModel(ISystemInfoService systemInfo, ILocalizationService loc)
    {
        _systemInfo = systemInfo;
        _loc = loc;
    }

    [ObservableProperty] private SystemSummary? _summary;
    [ObservableProperty] private bool _isLoaded;

    public ObservableCollection<StatItem> Stats { get; } = [];

    public string Welcome => _loc["home.welcome"];
    public string Subtitle => _loc["home.subtitle"];
    public string UserName => Summary?.UserName ?? Environment.UserName;

    public string WindowsTitle => _loc["home.windowsVersion"];
    public string WindowsName => Summary?.OsFamily ?? "Windows";
    public string WindowsEdition => Summary?.OsEdition ?? "";
    public string WindowsVersion => Summary is null || string.IsNullOrEmpty(Summary.OsDisplayVersion)
        ? "" : _loc.Format("home.osVersion", Summary.OsDisplayVersion);
    public string WindowsBuild => Summary is null ? "" : _loc.Format("home.osBuild", Summary.OsBuild);

    public string AppVersion => $"WinStart {AppInfo.Describe(_loc)}";

    public async Task LoadAsync()
    {
        Summary = await _systemInfo.GetSummaryAsync(CancellationToken.None);
        BuildStats();
        IsLoaded = true;
        RefreshTexts();
    }

    [ObservableProperty] private string? _reportStatus;

    [RelayCommand]
    private async Task SaveReportAsync()
    {
        if (Summary is null) return;

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = $"WinStart-{_loc["home.report.name"]}-{DateTime.Now:yyyy-MM-dd}.txt",
            DefaultExt = ".txt",
            Filter = "TXT|*.txt",
            Title = _loc["home.report"]
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var s = Summary;
            var lines = new List<string>
            {
                $"{_loc["home.report.title"]} — {DateTime.Now:dd.MM.yyyy HH:mm}",
                AppVersion,
                "",
                $"{_loc["profile.title"]}: {s.AccountName}",
                $"{_loc["home.windowsVersion"].TrimEnd('.')}: {s.OsFamily} {s.OsEdition}, {WindowsVersion}, {WindowsBuild}",
                $"{_loc["home.cpu"]}: {s.CpuName} ({s.CpuClock}, {_loc.Plural("home.cores", s.CpuCores)})",
                $"{_loc["home.ram"]}: {s.RamSize} {s.RamType} {s.RamSpeed}".TrimEnd(),
                $"{_loc["home.gpu"]}: {s.GpuName}",
                $"{_loc["home.resolution"]}: {s.Resolution}"
            };
            foreach (var d in s.Disks)
                lines.Add($"{_loc.Format("home.drive", d.Name)}: {FormatGb(d.TotalBytes)}, {_loc.Format("home.free", FormatGb(d.FreeBytes))}");
            lines.Add($"{_loc["home.uptime"]}: {FormatUptime(_loc, s.Uptime)}");

            ReportStatus = _loc["home.report.collecting"];
            var details = await _systemInfo.GetReportDetailsAsync(CancellationToken.None);

            lines.Add("");
            lines.Add($"{_loc["report.computer"]}: {details.ComputerName}");
            lines.Add($"{_loc["report.account"]}: {details.AccountName}");
            if (details.Motherboard.Length > 0) lines.Add($"{_loc["report.motherboard"]}: {details.Motherboard}");
            if (details.Bios.Length > 0) lines.Add($"{_loc["report.bios"]}: {details.Bios}");
            foreach (var drive in details.Drives) lines.Add($"{_loc["report.drive"]}: {drive}");
            foreach (var monitor in details.Monitors) lines.Add($"{_loc["report.monitor"]}: {monitor}");
            foreach (var ip in details.IpAddresses) lines.Add($"{_loc["report.ip"]}: {ip}");

            System.IO.File.WriteAllLines(dialog.FileName, lines);
            ReportStatus = _loc.Format("home.report.saved", dialog.FileName);
        }
        catch (Exception ex)
        {
            ReportStatus = ex.Message;
        }
    }

    private void BuildStats()
    {
        Stats.Clear();
        if (Summary is null) return;

        Stats.Add(new StatItem
        {
            Title = _loc["home.cpu"],
            Value = Summary.CpuName,
            Extra = $"{Summary.CpuClock}, {_loc.Plural("home.cores", Summary.CpuCores)}".Trim(' ', ','),
            Icon = SymbolRegular.DeveloperBoard24
        });

        Stats.Add(new StatItem
        {
            Title = _loc["home.ram"],
            Value = $"{Summary.RamSize} {Summary.RamType}".Trim(),
            Extra = Summary.RamSpeed,
            Icon = SymbolRegular.Server24
        });

        if (!string.IsNullOrWhiteSpace(Summary.GpuName))
            Stats.Add(new StatItem
            {
                Title = _loc["home.gpu"],
                Value = Summary.GpuName,
                Icon = SymbolRegular.Games24
            });

        foreach (var disk in Summary.Disks)
            Stats.Add(new StatItem
            {
                Title = _loc.Format("home.drive", disk.Name),
                Value = FormatGb(disk.TotalBytes),
                Extra = _loc.Format("home.free", FormatGb(disk.FreeBytes)),
                Icon = SymbolRegular.HardDrive24
            });

        if (!string.IsNullOrWhiteSpace(Summary.Resolution))
            Stats.Add(new StatItem
            {
                Title = _loc["home.resolution"],
                Value = Summary.Resolution,
                Icon = SymbolRegular.Desktop24
            });

        Stats.Add(new StatItem
        {
            Title = _loc["home.uptime"],
            Value = FormatUptime(_loc, Summary.Uptime),
            Icon = SymbolRegular.Clock24
        });
    }

    internal static string FormatUptime(ILocalizationService loc, TimeSpan t) =>
        loc.Format("home.uptimeValue", (int)t.TotalDays, t.Hours, t.Minutes);

    private static string FormatGb(long bytes) =>
        Math.Round(bytes / 1024d / 1024 / 1024).ToString("0", CultureInfo.InvariantCulture) + " GB";

    public void RefreshTexts()
    {
        foreach (var n in new[]
                 {
                     nameof(Welcome), nameof(Subtitle), nameof(UserName),
                     nameof(WindowsTitle), nameof(WindowsName), nameof(WindowsEdition),
                     nameof(WindowsVersion), nameof(WindowsBuild), nameof(AppVersion)
                 })
            OnPropertyChanged(n);

        if (IsLoaded) BuildStats();
    }
}
