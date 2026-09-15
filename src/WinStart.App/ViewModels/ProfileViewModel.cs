using System.Collections.ObjectModel;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;

namespace WinStart.App.ViewModels;

public sealed class ProfileViewModel
{
    public ProfileViewModel(SystemSummary s, ILocalizationService loc)
    {
        Name = s.AccountName;
        Role = s.IsAdministrator ? loc["profile.admin"] : loc["profile.user"];
        Initial = string.IsNullOrEmpty(s.AccountName) ? "?" : s.AccountName[..1].ToUpperInvariant();
        Title = loc["profile.title"];

        Specs = new ObservableCollection<StatItem>
        {
            new() { Title = loc["home.cpu"], Value = s.CpuName },
            new() { Title = loc["home.ram"], Value = $"{s.RamSize} {s.RamType}".Trim() },
            new() { Title = loc["home.gpu"], Value = s.GpuName },
            new() { Title = loc["profile.system"], Value = $"{s.OsFamily} · {loc.Format("home.osBuild", s.Build)}" },
            new() { Title = loc["home.resolution"], Value = s.Resolution },
            new() { Title = loc["home.uptime"], Value = HomeViewModel.FormatUptime(loc, s.Uptime) }
        };
    }

    public string Name { get; }
    public string Role { get; }
    public string Initial { get; }
    public string Title { get; }
    public ObservableCollection<StatItem> Specs { get; }
}
