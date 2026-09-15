using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class TweakCardViewModel : ObservableObject
{
    private readonly ITweakService _tweaks;
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly IBusyService _busy;
    private readonly IRestorePointService _restore;
    private CancellationTokenSource? _cts;

    public TweakDefinition Definition { get; }

    public TweakCardViewModel(TweakDefinition definition, ITweakService tweaks,
        ILocalizationService loc, IDialogService dialogs, IBusyService busy, IRestorePointService restore)
    {
        Definition = definition;
        _tweaks = tweaks;
        _loc = loc;
        _dialogs = dialogs;
        _busy = busy;
        _restore = restore;

        if (definition.Options.Count > 0)
            SelectedOption = definition.Options[0];

        _busy.Changed += (_, _) => Application.Current?.Dispatcher.Invoke(() =>
        {
            OnPropertyChanged(nameof(CanInteract));
            ApplyCommand.NotifyCanExecuteChanged();
            RevertCommand.NotifyCanExecuteChanged();
        });
    }

    public string Title => _loc[Definition.TitleKey];
    public string Description => _loc[Definition.DescriptionKey];

    public bool IsToggle => Definition.Kind == TweakKind.Toggle;
    public bool IsAction => Definition.Kind == TweakKind.Action;
    public bool HasOptions => Definition.Options.Count > 0;
    public IReadOnlyList<TweakOption> Options => Definition.Options;
    public bool SupportsShift => Definition.SupportsShiftOption;

    public string ActionVerb => _loc[Definition.ActionVerbKey ?? "card.apply"];

    public bool NeedsExplorer => Definition.NeedsExplorerRestart;
    public bool IsDestructive => Definition.Destructive;

    [ObservableProperty] private TweakState _state = TweakState.Unknown;
    [ObservableProperty] private TweakOption? _selectedOption;
    [ObservableProperty] private bool _shiftExtended;
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _isIndeterminate = true;

    public bool IsApplied => State == TweakState.Applied;
    public bool IsUnavailable => State == TweakState.Unavailable;
    public bool CanInteract => !IsBusy && !IsUnavailable && !_busy.IsBusy;

    public bool IsSelectable => !IsUnavailable;

    public string StateText => State switch
    {
        TweakState.Applied => _loc["card.applied"],
        TweakState.NotApplied => _loc["card.notApplied"],
        TweakState.Unavailable => _loc["card.unavailable"],
        _ => ""
    };

    public bool ShowRevert => IsToggle && Definition.Reversible && IsApplied;

    public bool ShowApply => !IsUnavailable && (IsAction || HasOptions || !IsApplied);

    partial void OnStateChanged(TweakState value)
    {
        OnPropertyChanged(nameof(IsApplied));
        OnPropertyChanged(nameof(IsUnavailable));
        OnPropertyChanged(nameof(StateText));
        OnPropertyChanged(nameof(ShowRevert));
        OnPropertyChanged(nameof(ShowApply));
        OnPropertyChanged(nameof(IsSelectable));
        OnPropertyChanged(nameof(CanInteract));
        ApplyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanInteract));
        ApplyCommand.NotifyCanExecuteChanged();
        RevertCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
        OnPropertyChanged(nameof(ActionVerb));
        OnPropertyChanged(nameof(StateText));
    }

    public async Task RefreshStateAsync()
    {
        try { State = await _tweaks.DetectAsync(Definition, CancellationToken.None); }
        catch { State = TweakState.Unknown; }
    }

    public Task ToggleForBatchAsync() => RunAsync(ShowRevert ? TweakDirection.Revert : TweakDirection.Apply);

    private bool CanApply() => CanInteract && ShowApply;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        if (Definition.ConfirmKey is not null)
        {
            var ok = await _dialogs.ConfirmAsync(_loc["dialog.confirm"], _loc[Definition.ConfirmKey]);
            if (!ok) return;
        }

        if (Definition.Destructive && await _dialogs.ConfirmAsync(_loc["restore.title"], _loc["restore.ask"]))
            await CreateRestorePointAsync();

        await RunAsync(TweakDirection.Apply);
    }

    private async Task CreateRestorePointAsync()
    {
        using var _ = _busy.Begin();
        IsBusy = true;
        IsIndeterminate = true;
        StatusText = _loc["restore.creating"];
        try
        {
            var error = await _restore.CreateAsync($"WinStart: {Title}", CancellationToken.None);
            StatusText = error is null ? null : _loc.Format("restore.failed", error);
        }
        finally { IsBusy = false; }
    }

    private bool CanRevert() => CanInteract && IsApplied;

    [RelayCommand(CanExecute = nameof(CanRevert))]
    private Task RevertAsync() => RunAsync(TweakDirection.Revert);

    private bool CanCancel() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private async Task RunAsync(TweakDirection direction)
    {
        using var _ = _busy.Begin();
        IsBusy = true;
        Progress = 0;
        IsIndeterminate = true;
        StatusText = _loc["card.working"];
        _cts = new CancellationTokenSource();

        void Report(string? status, double? percent)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (status is not null) StatusText = status;
                if (percent.HasValue)
                {
                    IsIndeterminate = false;
                    Progress = percent.Value;
                }
            });
        }

        try
        {
            var entry = direction == TweakDirection.Apply
                ? await _tweaks.ApplyAsync(Definition, SelectedOption?.Id, ShiftExtended, Report, _cts.Token)
                : await _tweaks.RevertAsync(Definition, Report, _cts.Token);

            StatusText = entry.Status switch
            {
                TweakStatus.Success => null,
                TweakStatus.Skipped => entry.Message,
                _ => entry.Message ?? _loc["journal.status.failed"]
            };
        }
        catch (Exception ex)
        {
            StatusText = ex.Message;
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            await RefreshStateAsync();
        }
    }
}
