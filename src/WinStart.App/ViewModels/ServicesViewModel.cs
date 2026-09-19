using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Audit;

namespace WinStart.App.ViewModels;

public sealed partial class ServiceDeviationViewModel(ServiceDeviation deviation, ServicesViewModel owner, ILocalizationService loc)
    : ObservableObject
{
    public ServiceDeviation Deviation { get; } = deviation;
    public string Title => Deviation.DisplayName;
    public string Name => Deviation.Name;
    public string StateText => loc.Format("services.state",
        loc[$"services.start.{Deviation.Current.Key}"], loc[$"services.start.{Deviation.Default.Key}"]);

    public bool ByWinStart => Deviation.WinStartTweakId is not null;

    public string OriginText => Deviation.WinStartTweakId is { } tweak
        ? loc.Format("services.byWinStart", loc[$"tweak.{tweak}.title"])
        : Deviation.ChangedAt is { } at
            ? loc.Format("services.changedAt", at.ToString("dd.MM.yyyy HH:mm"))
            : loc["services.changedUnknown"];

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _isRestored;
    [ObservableProperty] private string? _error;

    [RelayCommand]
    private Task RestoreAsync() => owner.RestoreAsync(this);

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(OriginText));
    }
}

public sealed partial class ServicesViewModel : ObservableObject
{
    private readonly IServiceAuditService _audit;
    private readonly ILocalizationService _loc;
    private readonly IBusyService _busy;
    private readonly IDialogService _dialogs;

    public ServicesViewModel(IServiceAuditService audit, ILocalizationService loc, IBusyService busy, IDialogService dialogs)
    {
        _audit = audit;
        _loc = loc;
        _busy = busy;
        _dialogs = dialogs;
    }

    public ObservableCollection<ServiceDeviationViewModel> Items { get; } = [];

    public string Title => _loc["nav.services"];
    public string Subtitle => _loc["services.subtitle"];
    public bool IsSupported => _audit.IsSupported;

    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _statusIsError;

    public int PendingCount => Items.Count(i => !i.IsRestored && !i.ByWinStart);
    public bool HasPending => PendingCount > 0;
    public bool AllClean => IsLoaded && IsSupported && Items.All(i => i.IsRestored);
    public string RestoreAllText => _loc.Format("services.restoreAll", PendingCount);

    private void RaiseCounts()
    {
        OnPropertyChanged(nameof(PendingCount));
        OnPropertyChanged(nameof(HasPending));
        OnPropertyChanged(nameof(AllClean));
        OnPropertyChanged(nameof(RestoreAllText));
        RestoreAllCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ReloadCommand.NotifyCanExecuteChanged();
        RestoreAllCommand.NotifyCanExecuteChanged();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusIsError = false;
        StatusText = _loc["services.checking"];
        try
        {
            var found = await _audit.ScanAsync(CancellationToken.None);
            Items.Clear();
            foreach (var d in found) Items.Add(new ServiceDeviationViewModel(d, this, _loc));
            StatusText = null;
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            IsLoaded = true;
            RaiseCounts();
        }
    }

    private bool CanReload() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanReload))]
    private Task ReloadAsync() => LoadAsync();

    private bool CanRestoreAll() => !IsBusy && HasPending;

    [RelayCommand(CanExecute = nameof(CanRestoreAll))]
    private async Task RestoreAllAsync()
    {
        var pending = Items.Where(i => !i.IsRestored && !i.ByWinStart).ToList();
        if (pending.Count == 0) return;

        var ok = await _dialogs.ConfirmAsync(_loc["services.restoreAll.title"],
            _loc.Format("services.restoreAll.confirm", pending.Count));
        if (!ok) return;

        foreach (var item in pending) await RestoreAsync(item);
    }

    public async Task RestoreAsync(ServiceDeviationViewModel item)
    {
        if (item.IsBusy || item.IsRestored) return;

        using var _ = _busy.Begin();
        IsBusy = true;
        item.IsBusy = true;
        item.Error = null;
        StatusIsError = false;
        StatusText = _loc.Format("services.restoring", item.Title);
        try
        {
            var error = await _audit.RestoreAsync(item.Deviation, CancellationToken.None);
            item.IsRestored = error is null;
            item.Error = error is null ? null : _loc.Format("services.failed", error);
            StatusText = error is null ? _loc.Format("services.restored", item.Title) : item.Error;
            StatusIsError = error is not null;
        }
        catch (Exception ex)
        {
            item.Error = ex.Message;
            StatusIsError = true;
            StatusText = ex.Message;
        }
        finally
        {
            item.IsBusy = false;
            IsBusy = false;
            RaiseCounts();
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(RestoreAllText));
        foreach (var i in Items) i.RefreshTexts();
    }
}
