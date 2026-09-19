using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Apps;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class WingetAppViewModel(WingetApp app, ILocalizationService loc) : ObservableObject
{
    public WingetApp App { get; } = app;
    public string Id => App.Id;
    public string Name => App.Name;
    public string Description => App.Description(loc.Language);

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private string? _foundPath;

    public bool IsPortable => FoundPath is not null;
    public string InstalledText => FoundPath is null ? loc["programs.alreadyInstalled"] : loc.Format("programs.portableAt", FoundPath);

    partial void OnFoundPathChanged(string? value)
    {
        OnPropertyChanged(nameof(IsPortable));
        OnPropertyChanged(nameof(InstalledText));
    }

    public bool Matches(string query) =>
        Name.Contains(query, StringComparison.CurrentCultureIgnoreCase)
        || Id.Contains(query, StringComparison.OrdinalIgnoreCase)
        || Description.Contains(query, StringComparison.CurrentCultureIgnoreCase);

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(InstalledText));
    }
}

public sealed partial class WingetUpgradeViewModel(WingetUpgrade upgrade, string name, ProgramsViewModel owner) : ObservableObject
{
    public WingetUpgrade Upgrade { get; } = upgrade;
    public string Id => Upgrade.Id;
    public string Name { get; } = name;
    public string VersionText => $"{Upgrade.Version} → {Upgrade.Available}";

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isDone;
    [ObservableProperty] private bool _failed;
    [ObservableProperty] private string? _status;

    [RelayCommand]
    private Task UpdateAsync() => owner.UpgradeAsync([this]);
}

public sealed partial class WingetGroupViewModel(WingetCategory category, IReadOnlyList<WingetAppViewModel> items, ILocalizationService loc)
    : ObservableObject
{
    public WingetCategory Category { get; } = category;
    public IReadOnlyList<WingetAppViewModel> Items { get; } = items;
    public string Title => loc[$"programs.cat.{Category}"];

    [ObservableProperty] private bool _isExpanded = true;
    [ObservableProperty] private bool _isVisible = true;

    public int VisibleCount => Items.Count(i => i.IsVisible);
    public int InstalledCount => Items.Count(i => i.IsInstalled);
    public string CountText => loc.Format("programs.cat.count", VisibleCount, InstalledCount);

    public void RefreshCounts()
    {
        OnPropertyChanged(nameof(VisibleCount));
        OnPropertyChanged(nameof(InstalledCount));
        OnPropertyChanged(nameof(CountText));
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(CountText));
        foreach (var item in Items) item.RefreshTexts();
    }
}

public sealed partial class ProgramsViewModel : ObservableObject
{
    private readonly IWingetService _winget;
    private readonly IInstalledAppsScanner _scanner;
    private readonly ILocalizationService _loc;
    private readonly IBusyService _busy;
    private readonly UnattendViewModel _unattend;

    public ProgramsViewModel(IWingetService winget, IInstalledAppsScanner scanner, ILocalizationService loc,
        IBusyService busy, UnattendViewModel unattend)
    {
        _winget = winget;
        _scanner = scanner;
        _loc = loc;
        _busy = busy;
        _unattend = unattend;

        Groups = Enum.GetValues<WingetCategory>()
            .Select(c => new WingetGroupViewModel(c,
                WingetCatalog.Apps.Where(a => a.Category == c).Select(a => new WingetAppViewModel(a, _loc)).ToList(), _loc))
            .Where(g => g.Items.Count > 0)
            .ToList();

        foreach (var item in Groups.SelectMany(g => g.Items))
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WingetAppViewModel.IsSelected)) RaiseSelection();
            };
    }

    public IReadOnlyList<WingetGroupViewModel> Groups { get; }
    public IEnumerable<WingetAppViewModel> All => Groups.SelectMany(g => g.Items);
    public System.Collections.ObjectModel.ObservableCollection<WingetUpgradeViewModel> Upgrades { get; } = [];

    public int PendingUpgrades => Upgrades.Count(u => !u.IsDone);
    public bool HasUpgrades => Upgrades.Count > 0;
    public string UpgradesTitle => _loc.Format("programs.upgrades.title", PendingUpgrades);
    public string UpgradeAllText => _loc.Format("programs.upgrades.all", PendingUpgrades);

    private void RaiseUpgrades()
    {
        OnPropertyChanged(nameof(PendingUpgrades));
        OnPropertyChanged(nameof(HasUpgrades));
        OnPropertyChanged(nameof(UpgradesTitle));
        OnPropertyChanged(nameof(UpgradeAllText));
        UpgradeAllCommand.NotifyCanExecuteChanged();
    }

    public string Title => _loc["nav.programs"];
    public string Subtitle => _loc.Format("programs.subtitle", WingetCatalog.Apps.Count);

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _wingetMissing;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private bool _onlyNotInstalled;

    public int SelectedCount => All.Count(a => a.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string InstallText => _loc.Format("programs.install", SelectedCount);
    public bool NothingFound => Groups.All(g => !g.IsVisible);
    public bool IsWorking => IsInstalling || IsRefreshing;

    partial void OnIsInstallingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        UpgradeAllCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRefreshingChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWorking));
        UpgradeAllCommand.NotifyCanExecuteChanged();
        ReloadCommand.NotifyCanExecuteChanged();
        InstallSelectedCommand.NotifyCanExecuteChanged();
    }

    partial void OnFilterChanged(string value) => ApplyFilter();
    partial void OnOnlyNotInstalledChanged(bool value) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = Filter.Trim();
        foreach (var group in Groups)
        {
            foreach (var item in group.Items)
                item.IsVisible = (query.Length == 0 || item.Matches(query)) && (!OnlyNotInstalled || !item.IsInstalled);

            group.IsVisible = group.Items.Any(i => i.IsVisible);
            if (query.Length > 0) group.IsExpanded = true;
            group.RefreshCounts();
        }
        OnPropertyChanged(nameof(NothingFound));
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(InstallText));
        InstallSelectedCommand.NotifyCanExecuteChanged();
        ToUnattendCommand.NotifyCanExecuteChanged();
    }

    public async Task RefreshAsync()
    {
        if (IsRefreshing || IsInstalling) return;
        IsRefreshing = true;
        StatusText = _loc["programs.checking"];
        try
        {
            var scan = _scanner.ScanAsync(CancellationToken.None);
            WingetMissing = !await _winget.IsAvailableAsync(CancellationToken.None);
            var installed = WingetMissing
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                : await _winget.ListInstalledAsync(CancellationToken.None);
            IReadOnlyDictionary<string, FoundApp> found;
            try { found = await scan; }
            catch { found = new Dictionary<string, FoundApp>(); }

            foreach (var a in All)
            {
                var byWinget = installed.Contains(a.Id);
                found.TryGetValue(a.Id, out var hit);
                a.FoundPath = !byWinget && hit is { Portable: true } ? System.IO.Path.GetDirectoryName(hit.ExePath) : null;
                a.IsInstalled = byWinget || hit is not null;
            }

            ApplyFilter();

            Upgrades.Clear();
            RaiseUpgrades();
            if (!WingetMissing)
            {
                StatusText = _loc["programs.upgrades.checking"];
                foreach (var u in await _winget.ListUpgradesAsync(CancellationToken.None))
                    Upgrades.Add(new WingetUpgradeViewModel(u, WingetCatalog.ById(u.Id)?.Name ?? u.Name, this));
            }
            RaiseUpgrades();
            StatusText = null;
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            IsRefreshing = false;
            IsLoaded = true;
        }
    }

    private bool CanReload() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadAsync() => RefreshAsync();

    [RelayCommand]
    private void ToggleGroup(WingetGroupViewModel? group)
    {
        if (group is not null) group.IsExpanded = !group.IsExpanded;
    }

    private bool CanUpgradeAll() => PendingUpgrades > 0 && !IsInstalling && !IsRefreshing;

    [RelayCommand(CanExecute = nameof(CanUpgradeAll))]
    private Task UpgradeAllAsync() => UpgradeAsync(Upgrades
        .Where(u => !u.IsDone)
        .OrderBy(u => u.Id.Equals("Microsoft.AppInstaller", StringComparison.OrdinalIgnoreCase))
        .ToList());

    public async Task UpgradeAsync(IReadOnlyList<WingetUpgradeViewModel> items)
    {
        if (items.Count == 0 || IsInstalling || IsRefreshing) return;

        using var _ = _busy.Begin();
        IsInstalling = true;
        InstallSelectedCommand.NotifyCanExecuteChanged();
        UpgradeAllCommand.NotifyCanExecuteChanged();
        var failed = 0;
        try
        {
            foreach (var item in items)
            {
                item.IsBusy = true;
                item.Failed = false;
                item.Status = _loc["programs.upgrades.updating"];
                StatusText = _loc.Format("programs.upgrades.updatingApp", item.Name);

                var error = await _winget.UpgradeAsync(item.Id, CancellationToken.None);

                item.IsBusy = false;
                item.Failed = error is not null;
                item.IsDone = error is null;
                item.Status = error is null ? _loc["programs.upgrades.updated"] : _loc.Format("programs.failed", error);
                if (error is not null) failed++;
                RaiseUpgrades();
            }

            StatusText = failed == 0
                ? _loc["programs.upgrades.done"]
                : _loc.Format("programs.doneWithErrors", failed);
        }
        finally
        {
            IsInstalling = false;
            InstallSelectedCommand.NotifyCanExecuteChanged();
            RaiseUpgrades();
        }
    }

    private bool CanInstall() => HasSelection && !IsInstalling && !IsRefreshing && !WingetMissing;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallSelectedAsync()
    {
        var selected = All.Where(a => a.IsSelected).ToList();
        if (selected.Count == 0) return;

        using var _ = _busy.Begin();
        IsInstalling = true;
        InstallSelectedCommand.NotifyCanExecuteChanged();
        var failed = 0;
        try
        {
            foreach (var app in selected)
            {
                app.IsBusy = true;
                app.Failed = false;
                app.Status = _loc["programs.installing"];
                StatusText = _loc.Format("programs.installingApp", app.Name);

                var error = await _winget.InstallAsync(app.Id, CancellationToken.None);

                app.IsBusy = false;
                app.Failed = error is not null;
                app.Status = error is null ? _loc["programs.installed"] : _loc.Format("programs.failed", error);
                if (error is null) { app.IsInstalled = true; app.FoundPath = null; app.IsSelected = false; }
                else failed++;
            }

            foreach (var group in Groups) group.RefreshCounts();
            StatusText = failed == 0
                ? _loc["programs.done"]
                : _loc.Format("programs.doneWithErrors", failed);
        }
        finally
        {
            IsInstalling = false;
            InstallSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var a in All) a.IsSelected = false;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void ToUnattend()
    {
        _unattend.SetWingetApps(All.Where(a => a.IsSelected).Select(a => a.Id));
        StatusText = _loc["programs.copiedToUnattend"];
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(InstallText));
        OnPropertyChanged(nameof(UpgradesTitle));
        OnPropertyChanged(nameof(UpgradeAllText));
        foreach (var g in Groups) g.RefreshTexts();
        ApplyFilter();
    }
}
