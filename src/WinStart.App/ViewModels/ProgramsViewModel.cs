using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Apps;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class WingetAppViewModel(WingetApp app) : ObservableObject
{
    public WingetApp App { get; } = app;
    public string Id => App.Id;
    public string Name => App.Name;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isInstalled;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _status;
    [ObservableProperty] private bool _failed;
}

public sealed class WingetGroupViewModel(WingetCategory category, IReadOnlyList<WingetAppViewModel> items, ILocalizationService loc)
    : ObservableObject
{
    public WingetCategory Category { get; } = category;
    public IReadOnlyList<WingetAppViewModel> Items { get; } = items;
    public string Title => loc[$"programs.cat.{Category}"];
    public void RefreshTexts() => OnPropertyChanged(nameof(Title));
}

public sealed partial class ProgramsViewModel : ObservableObject
{
    private readonly IWingetService _winget;
    private readonly ILocalizationService _loc;
    private readonly IBusyService _busy;
    private readonly UnattendViewModel _unattend;

    public ProgramsViewModel(IWingetService winget, ILocalizationService loc, IBusyService busy, UnattendViewModel unattend)
    {
        _winget = winget;
        _loc = loc;
        _busy = busy;
        _unattend = unattend;

        Groups = Enum.GetValues<WingetCategory>()
            .Select(c => new WingetGroupViewModel(c,
                WingetCatalog.Apps.Where(a => a.Category == c).Select(a => new WingetAppViewModel(a)).ToList(), _loc))
            .ToList();

        foreach (var item in Groups.SelectMany(g => g.Items))
            item.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(WingetAppViewModel.IsSelected)) RaiseSelection();
            };
    }

    public IReadOnlyList<WingetGroupViewModel> Groups { get; }
    public IEnumerable<WingetAppViewModel> All => Groups.SelectMany(g => g.Items);

    public string Title => _loc["nav.programs"];
    public string Subtitle => _loc["programs.subtitle"];

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isRefreshing;
    [ObservableProperty] private bool _isInstalling;
    [ObservableProperty] private bool _wingetMissing;
    [ObservableProperty] private string? _statusText;

    public int SelectedCount => All.Count(a => a.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string InstallText => _loc.Format("programs.install", SelectedCount);

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
        if (IsRefreshing) return;
        IsRefreshing = true;
        StatusText = _loc["programs.checking"];
        try
        {
            WingetMissing = !await _winget.IsAvailableAsync(CancellationToken.None);
            if (!WingetMissing)
            {
                var installed = await _winget.ListInstalledAsync(CancellationToken.None);
                foreach (var a in All) a.IsInstalled = installed.Contains(a.Id);
            }
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

    [RelayCommand]
    private Task ReloadAsync() => RefreshAsync();

    private bool CanInstall() => HasSelection && !IsInstalling && !WingetMissing;

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
                if (error is null) { app.IsInstalled = true; app.IsSelected = false; }
                else failed++;
            }

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
        foreach (var g in Groups) g.RefreshTexts();
    }
}
