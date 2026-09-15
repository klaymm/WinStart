using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Startup;

namespace WinStart.App.ViewModels;

public sealed partial class StartupEntryViewModel(StartupEntry entry, StartupViewModel owner, ILocalizationService loc)
    : ObservableObject
{
    private bool _enabled = entry.Enabled;

    public StartupEntry Entry { get; } = entry;
    public string Name => Entry.Name;
    public string Command => Entry.Command;
    public string Location => Entry.Location;
    public string Publisher => Entry.Publisher ?? loc["startup.unknownPublisher"];
    public bool HasPublisher => Entry.Publisher is not null;
    public bool CanToggle => Entry.CanToggle;
    public bool IsMicrosoft => Entry.IsMicrosoft;
    public bool HasFile => Entry.FilePath is not null && File.Exists(Entry.FilePath);

    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string? _error;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value || IsBusy) return;
            _ = owner.SetEnabledAsync(this, value);
        }
    }

    public void SetEnabledSilently(bool value)
    {
        _enabled = value;
        OnPropertyChanged(nameof(Enabled));
    }

    [RelayCommand]
    private void OpenLocation()
    {
        if (!HasFile) return;
        try { Process.Start("explorer.exe", $"/select,\"{Entry.FilePath}\""); }
        catch { }
    }

    public void RefreshTexts() => OnPropertyChanged(nameof(Publisher));
}

public sealed class StartupGroupViewModel(StartupKind kind, IReadOnlyList<StartupEntryViewModel> items, ILocalizationService loc)
    : ObservableObject
{
    public StartupKind Kind { get; } = kind;
    public IReadOnlyList<StartupEntryViewModel> Items { get; } = items;
    public string Title => loc[$"startup.kind.{Kind}"];
    public string Description => loc[$"startup.kind.{Kind}.desc"];
    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Description));
    }
}

public sealed partial class StartupViewModel : ObservableObject
{
    private readonly IStartupService _startup;
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialogs;
    private List<StartupEntryViewModel> _all = [];

    public StartupViewModel(IStartupService startup, ILocalizationService loc, IDialogService dialogs)
    {
        _startup = startup;
        _loc = loc;
        _dialogs = dialogs;
    }

    public ObservableCollection<StartupGroupViewModel> Groups { get; } = [];

    public IReadOnlyList<StartupEntryViewModel> AllEntries => _all;

    public string Title => _loc["nav.startup"];
    public string Subtitle => _loc["startup.subtitle"];

    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _hideMicrosoft = true;
    [ObservableProperty] private string _filter = "";
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private bool _statusIsError;
    [ObservableProperty] private string _countText = "";

    partial void OnHideMicrosoftChanged(bool value) => ApplyFilter();
    partial void OnFilterChanged(string value) => ApplyFilter();

    public async Task LoadAsync()
    {
        if (IsLoading) return;
        IsLoading = true;
        StatusIsError = false;
        StatusText = _loc["startup.scanning"];
        try
        {
            var entries = await _startup.ScanAsync(CancellationToken.None);
            _all = entries.Select(e => new StartupEntryViewModel(e, this, _loc)).ToList();
            StatusText = null;
            ApplyFilter();
        }
        catch (Exception ex)
        {
            StatusIsError = true;
            StatusText = ex.Message;
        }
        finally
        {
            IsLoading = false;
            IsLoaded = true;
        }
    }

    [RelayCommand]
    private Task ReloadAsync() => LoadAsync();

    private bool Matches(StartupEntryViewModel e)
    {
        if (HideMicrosoft && e.IsMicrosoft) return false;
        var q = Filter.Trim();
        if (q.Length == 0) return true;
        return e.Name.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || e.Command.Contains(q, StringComparison.CurrentCultureIgnoreCase)
               || (e.Entry.Publisher?.Contains(q, StringComparison.CurrentCultureIgnoreCase) ?? false)
               || e.Location.Contains(q, StringComparison.CurrentCultureIgnoreCase);
    }

    private void ApplyFilter()
    {
        Groups.Clear();
        var shown = 0;
        foreach (var kind in Enum.GetValues<StartupKind>())
        {
            var items = _all.Where(e => e.Entry.Kind == kind && Matches(e))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            if (items.Count == 0) continue;
            shown += items.Count;
            Groups.Add(new StartupGroupViewModel(kind, items, _loc));
        }
        CountText = _loc.Format("startup.count", shown, _all.Count);
    }

    public async Task SetEnabledAsync(StartupEntryViewModel item, bool enabled)
    {
        if (item.Entry.Kind == StartupKind.Service && !enabled)
        {
            var ok = await _dialogs.ConfirmAsync(_loc["startup.kind.Service"], _loc.Format("startup.confirmService", item.Name));
            if (!ok) { item.SetEnabledSilently(true); return; }
        }

        item.IsBusy = true;
        item.Error = null;
        try
        {
            var error = await _startup.SetEnabledAsync(item.Entry, enabled, CancellationToken.None);
            if (error is null) item.SetEnabledSilently(enabled);
            else
            {
                item.Error = error;
                item.SetEnabledSilently(!enabled);
            }
        }
        finally
        {
            item.IsBusy = false;
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        foreach (var g in Groups) g.RefreshTexts();
        foreach (var e in _all) e.RefreshTexts();
        if (_all.Count > 0) CountText = _loc.Format("startup.count", Groups.Sum(g => g.Items.Count), _all.Count);
    }
}
