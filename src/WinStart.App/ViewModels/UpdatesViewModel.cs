using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Infrastructure;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Updates;

namespace WinStart.App.ViewModels;

public sealed class ChangelogEntry
{
    public required string Title { get; init; }
    public required string Date { get; init; }
    public bool IsCurrent { get; init; }
    public bool IsNew { get; init; }
    public required IReadOnlyList<string> Changes { get; init; }
}

public enum UpdateState { Idle, Checking, UpToDate, Available, Downloading, Error }

public sealed partial class UpdatesViewModel : ObservableObject
{
    private static readonly (string Version, DateTime Date, int Count)[] Releases =
    [
        ("1.2.0", new DateTime(2026, 9, 19), 9),
        ("1.1.9", new DateTime(2026, 9, 19), 3),
        ("1.1.8", new DateTime(2026, 9, 18), 4),
        ("1.1.7", new DateTime(2026, 9, 17), 5),
        ("1.1.6", new DateTime(2026, 9, 16), 1),
        ("1.1.5", new DateTime(2026, 9, 15), 3),
        ("1.1.4", new DateTime(2026, 9, 15), 9),
        ("1.1.3", new DateTime(2026, 9, 15), 5),
        ("1.1.2", new DateTime(2026, 9, 14), 4),
        ("1.1.1", new DateTime(2026, 9, 14), 8),
        ("1.1", new DateTime(2026, 9, 14), 8),
        ("1.0", new DateTime(2026, 9, 13), 4)
    ];

    private readonly ILocalizationService _loc;
    private readonly IUpdateService _updates;
    private readonly IBusyService _busy;
    private readonly ISettingsService _settings;

    public UpdatesViewModel(ILocalizationService loc, IUpdateService updates, IBusyService busy,
        ISettingsService settings)
    {
        _loc = loc;
        _updates = updates;
        _busy = busy;
        _settings = settings;
        BuildEntries();
    }

    public string Title => _loc["nav.updates"];
    public string Subtitle => _loc["updates.subtitle"];
    public string CurrentVersion => _loc.Format("updates.current", AppInfo.Version);

    [ObservableProperty] private IReadOnlyList<ChangelogEntry> _entries = [];

    private IReadOnlyList<ReleaseNotes> _newer = [];

    [ObservableProperty] private UpdateState _state = UpdateState.Idle;
    [ObservableProperty] private UpdateInfo? _available;
    [ObservableProperty] private string? _statusText;
    [ObservableProperty] private double _progress;

    public bool IsAvailable => Available is not null;
    public bool IsBusyState => State is UpdateState.Checking or UpdateState.Downloading;
    public bool IsError => State == UpdateState.Error;
    public string AvailableTitle => Available is null ? "" : _loc.Format("updates.available", Available.Version.ToString());

    partial void OnStateChanged(UpdateState value)
    {
        OnPropertyChanged(nameof(IsBusyState));
        OnPropertyChanged(nameof(IsError));
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }

    partial void OnAvailableChanged(UpdateInfo? value)
    {
        OnPropertyChanged(nameof(IsAvailable));
        OnPropertyChanged(nameof(AvailableTitle));
        InstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanCheck() => !IsBusyState;

    [RelayCommand(CanExecute = nameof(CanCheck))]
    public async Task CheckAsync()
    {
        State = UpdateState.Checking;
        StatusText = _loc["updates.checking"];
        try
        {
            Apply(await _updates.CheckAsync(_settings.Current.ReceiveBetas, CancellationToken.None));
            State = Available is null ? UpdateState.UpToDate : UpdateState.Available;
            StatusText = Available is null ? _loc["updates.upToDate"] : null;
        }
        catch (Exception ex)
        {
            State = UpdateState.Error;
            StatusText = _loc.Format("updates.checkFailed", ex.Message);
        }
    }

    public async Task CheckSilentlyAsync()
    {
        if (State != UpdateState.Idle) return;
        try
        {
            Apply(await _updates.CheckAsync(_settings.Current.ReceiveBetas, CancellationToken.None));
            State = Available is null ? UpdateState.Idle : UpdateState.Available;
        }
        catch { State = UpdateState.Idle; }
    }

    private void Apply(UpdateCheck check)
    {
        Available = check.Update;
        _newer = check.Newer;
        BuildEntries();
    }

    private bool CanInstall() => IsAvailable && !IsBusyState;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task InstallAsync()
    {
        if (Available is null) return;

        using var _ = _busy.Begin();
        State = UpdateState.Downloading;
        Progress = 0;
        StatusText = _loc["updates.downloading"];
        try
        {
            var path = await _updates.DownloadAsync(Available,
                new Progress<double>(p => System.Windows.Application.Current?.Dispatcher.Invoke(() => Progress = p)),
                CancellationToken.None);

            StatusText = _loc["updates.installing"];
            _updates.InstallAndExit(path);
        }
        catch (UpdateIntegrityException)
        {
            State = UpdateState.Error;
            StatusText = _loc["updates.integrity"];
        }
        catch (Exception ex)
        {
            State = UpdateState.Error;
            StatusText = _loc.Format("updates.downloadFailed", ex.Message);
        }
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(CurrentVersion));
        OnPropertyChanged(nameof(AvailableTitle));
        BuildEntries();
    }

    private void BuildEntries()
    {
        var current = AppInfo.Semantic;

        var newer = _newer.Select(release => new ChangelogEntry
        {
            Title = _loc.Format(release.Version.IsPreRelease ? "updates.versionBeta" : "updates.version",
                release.Version.ToString()),
            Date = FormatDate(release.Date),
            IsNew = true,
            Changes = _loc.Language == "en" && release.En.Count > 0 ? release.En
                : release.Ru.Count > 0 ? release.Ru
                : release.En
        });

        var installed = Releases.Select(release => new ChangelogEntry
        {
            Title = _loc.Format("updates.version", release.Version),
            Date = FormatDate(release.Date),
            IsCurrent = SemVersion.TryParse(release.Version, out var v) && v == current,
            Changes = Enumerable.Range(1, release.Count)
                .Select(n => _loc[$"changelog.{release.Version}.{n}"])
                .ToList()
        });

        Entries = newer.Concat(installed).ToList();
    }

    private static string FormatDate(DateTime date) => date.ToString("d MMMM yyyy", CultureInfo.CurrentUICulture);
}
