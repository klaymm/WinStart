using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using WinStart.App.Infrastructure;
using WinStart.App.Services;
using WinStart.App.ViewModels;
using WinStart.App.Views;
using WinStart.Core.Abstractions;
using WinStart.Core.Services;
using WinStart.Core.Tweaks;

namespace WinStart.App;

public partial class App : Application
{
    private IServiceProvider _services = null!;
    private ILocalizationService _loc = null!;
    private ISettingsService _settings = null!;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogFatal(args.ExceptionObject as Exception);

        _services = BuildServices();

        _settings = _services.GetRequiredService<ISettingsService>();
        _loc = _services.GetRequiredService<ILocalizationService>();

        _loc.SetLanguage(_settings.Current.Language);
        LocalizationProxy.Instance.Attach(_loc);
        LocKeyConverter.Localization = _loc;

        _services.GetRequiredService<IThemeService>().Initialize();

        await _services.GetRequiredService<IJournalService>().LoadAsync();

        var shell = _services.GetRequiredService<ShellWindow>();
        MainWindow = shell;
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        shell.Show();

        _ = _services.GetRequiredService<UpdatesViewModel>().CheckSilentlyAsync();
    }

    private IServiceProvider BuildServices()
    {
        var services = new ServiceCollection();

        // --- Core: инфраструктура ---
        services.AddSingleton<IPathProvider>(_ => new PathProvider());
        services.AddSingleton<IProcessRunner, ProcessRunner>();
        services.AddSingleton<IRegistryService, RegistryService>();
        services.AddSingleton<IDownloadService, DownloadService>();
        services.AddSingleton<IArchiveService, ArchiveService>();
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IJournalService, JournalService>();
        services.AddSingleton<ITweakService, TweakService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IWingetService, WingetService>();
        services.AddSingleton<WinStart.Core.Unattend.IImageCheckService, WinStart.Core.Unattend.ImageCheckService>();
        services.AddSingleton<WinStart.Core.Startup.IStartupService, WinStart.Core.Startup.StartupService>();
        services.AddSingleton<TweakRegistry>();

        // --- App: сервисы UI ---
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IBusyService, BusyService>();
        services.AddSingleton<IUpdateService, UpdateService>();

        // --- ViewModels ---
        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<JournalViewModel>();
        services.AddSingleton<UpdatesViewModel>();
        services.AddSingleton<UnattendViewModel>();
        services.AddSingleton<ProgramsViewModel>();
        services.AddSingleton<StartupViewModel>();
        services.AddTransient<CategoryPageViewModel>();
        services.AddSingleton(sp => new SettingsViewModel(
            sp.GetRequiredService<IThemeService>(),
            sp.GetRequiredService<ISettingsService>(),
            sp.GetRequiredService<ILocalizationService>(),
            sp.GetRequiredService<IPathProvider>(),
            sp.GetRequiredService<IRestorePointService>(),
            sp.GetRequiredService<IBusyService>(),
            SetLanguage));

        services.AddSingleton<ShellWindow>();

        return services.BuildServiceProvider();
    }

    private void SetLanguage(string language)
    {
        if (language == _loc.Language) return;

        _loc.SetLanguage(language);
        _settings.Current.Language = language;
        _settings.Save();

        _services.GetRequiredService<ShellViewModel>().RefreshTexts();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatal(e.Exception);
        MessageBox.Show(e.Exception.Message, "WinStart", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }

    private void LogFatal(Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var paths = _services?.GetService<IPathProvider>();
            if (paths is null) return;
            var file = System.IO.Path.Combine(paths.Logs, "crash.log");
            System.IO.File.AppendAllText(file, $"{DateTime.Now:u}  {ex}\n\n");
        }
        catch { }
    }
}
