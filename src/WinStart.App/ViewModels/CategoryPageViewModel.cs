using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Models;
using WinStart.Core.Services;
using WinStart.Core.Tweaks;

namespace WinStart.App.ViewModels;

public sealed partial class CategoryPageViewModel : ObservableObject
{
    private readonly TweakRegistry _registry;
    private readonly ITweakService _tweaks;
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private readonly IBusyService _busy;
    private readonly IRestorePointService _restore;
    private string _titleKey = "";

    public CategoryPageViewModel(TweakRegistry registry, ITweakService tweaks,
        ILocalizationService loc, IDialogService dialogs, IBusyService busy, IRestorePointService restore)
    {
        _registry = registry;
        _tweaks = tweaks;
        _loc = loc;
        _dialogs = dialogs;
        _busy = busy;
        _restore = restore;
    }

    public ObservableCollection<TweakCardViewModel> Cards { get; } = [];

    public string Title => _loc[_titleKey];
    public string Count => _loc.Plural("section.count", Cards.Count);

    public int SelectedCount => Cards.Count(c => c.IsSelected);
    public bool HasSelection => SelectedCount > 0;
    public string ApplySelectedText => _loc.Format("section.applySelected", SelectedCount);

    public void Load(TweakCategory category, string titleKey)
    {
        _titleKey = titleKey;

        Cards.Clear();
        foreach (var def in _registry.ByCategory(category))
        {
            var card = new TweakCardViewModel(def, _tweaks, _loc, _dialogs, _busy, _restore);
            card.PropertyChanged += OnCardPropertyChanged;
            Cards.Add(card);
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Count));
        RaiseSelection();
    }

    private void OnCardPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TweakCardViewModel.IsSelected)) RaiseSelection();
    }

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(ApplySelectedText));
        ApplySelectedCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ApplySelectedAsync()
    {
        var selected = Cards.Where(c => c.IsSelected).ToList();
        if (selected.Count == 0) return;

        var ok = await _dialogs.ConfirmAsync(
            _loc["dialog.applySelected.title"],
            _loc.Format("dialog.applySelected.text", selected.Count));
        if (!ok) return;

        foreach (var card in selected)
        {
            card.IsSelected = false;
            await card.ToggleForBatchAsync();
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var card in Cards) card.IsSelected = false;
    }

    public Task RefreshAllAsync() => Task.WhenAll(Cards.Select(c => c.RefreshStateAsync()));

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Count));
        OnPropertyChanged(nameof(ApplySelectedText));
        foreach (var card in Cards) card.RefreshTexts();
    }
}
