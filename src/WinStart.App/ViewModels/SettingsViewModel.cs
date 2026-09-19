using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinStart.App.Services;
using WinStart.Core.Abstractions;
using WinStart.Core.Services;

namespace WinStart.App.ViewModels;

public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;
    private readonly IPathProvider _paths;
    private readonly IRestorePointService _restore;
    private readonly IBusyService _busy;
    private readonly Action<string> _setLanguage;

    public SettingsViewModel(IThemeService theme, ISettingsService settings,
        ILocalizationService loc, IPathProvider paths, IRestorePointService restore, IBusyService busy,
        Action<string> setLanguage)
    {
        _theme = theme;
        _settings = settings;
        _loc = loc;
        _paths = paths;
        _restore = restore;
        _busy = busy;
        _setLanguage = setLanguage;

        _selectedThemeIndex = (int)settings.Current.Theme;
        _isEnglish = settings.Current.Language == "en";
        _showSplash = settings.Current.ShowSplash;
        _receiveBetas = settings.Current.ReceiveBetas;
    }

    [ObservableProperty] private int _selectedThemeIndex;
    [ObservableProperty] private bool _isEnglish;
    [ObservableProperty] private bool _showSplash;
    [ObservableProperty] private bool _receiveBetas;

    [ObservableProperty] private bool _isCreatingRestorePoint;
    [ObservableProperty] private string? _restorePointStatus;
    [ObservableProperty] private bool _restorePointFailed;

    partial void OnSelectedThemeIndexChanged(int value)
    {
        _theme.Apply((AppTheme)value);
    }

    partial void OnIsEnglishChanged(bool value)
    {
        _setLanguage(value ? "en" : "ru");
    }

    partial void OnShowSplashChanged(bool value)
    {
        _settings.Current.ShowSplash = value;
        _settings.Save();
    }

    partial void OnReceiveBetasChanged(bool value)
    {
        _settings.Current.ReceiveBetas = value;
        _settings.Save();
    }

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (IsCreatingRestorePoint) return;

        using var _ = _busy.Begin();
        IsCreatingRestorePoint = true;
        RestorePointFailed = false;
        RestorePointStatus = _loc["restore.creating"];
        try
        {
            var error = await _restore.CreateAsync($"WinStart {DateTime.Now:dd.MM.yyyy HH:mm}", CancellationToken.None);
            RestorePointFailed = error is not null;
            RestorePointStatus = error is null ? _loc["restore.created"] : _loc.Format("restore.failed", error);
        }
        finally { IsCreatingRestorePoint = false; }
    }

    [RelayCommand]
    private void OpenLogs() => OpenFolder(_paths.Logs);

    [RelayCommand]
    private void OpenBackups() => OpenFolder(_paths.Backups);

    private static void OpenFolder(string path)
    {
        try
        {
            System.IO.Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch { }
    }
}
