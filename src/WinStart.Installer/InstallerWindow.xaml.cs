using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace WinStart.Installer;

public partial class InstallerWindow : FluentWindow
{
    private readonly Localization _loc = new();
    private readonly InstallService _install = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly IProgress<(string Stage, double Percent)> _progress;
    private bool _dark;
    private string _targetDir;
    private bool _installed;
    private bool _alreadyInstalled;

    private readonly bool _updateMode;

    public InstallerWindow()
    {
        InitializeComponent();

        _progress = new Progress<(string Stage, double Percent)>(p =>
        {
            Bar.Value = p.Percent;
            ProgressStage.Text = _loc[p.Stage];
        });

        var existing = InstallService.InstalledLocation;
        _alreadyInstalled = existing is not null;
        _targetDir = existing ?? _install.DefaultLocation;
        LocationBox.Text = _targetDir;

        UninstallButton.Visibility = _alreadyInstalled ? Visibility.Visible : Visibility.Collapsed;

        _updateMode = _alreadyInstalled && Environment.GetCommandLineArgs()
            .Any(a => a.Equals("/update", StringComparison.OrdinalIgnoreCase));
        if (_updateMode)
        {
            var (dark, lang) = InstallService.ReadAppSettings();
            _dark = dark;
            ApplicationThemeManager.Apply(_dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
            _loc.Toggle(lang);
            LangToggle.IsChecked = lang == "en";
        }

        _loc.Changed += (_, _) => ApplyTexts();
        ApplyTexts();
        UpdateThemeIcon();

        Loaded += (_, _) =>
        {
            if (_updateMode)
            {
                Intro.Visibility = Visibility.Collapsed;
                OnInstall(this, new RoutedEventArgs());
            }
            else
            {
                PlayIntro();
            }
        };
    }

    private void ApplyTexts()
    {
        TitleBar.Title = _loc["title"];

        WelcomeTitle.Text = _loc[_alreadyInstalled ? "alreadyInstalled" : "welcome"];
        WelcomeSubtitle.Text = _loc[_alreadyInstalled ? "alreadyInstalledText" : "subtitle"];
        LocationLabel.Text = _loc[_alreadyInstalled ? "installedTo" : "location"];
        InstallButton.Content = _loc[_alreadyInstalled ? "reinstall" : "install"];
        UninstallButton.Content = _loc["uninstall"];

        BrowseButton.Content = _loc["browse"];
        DesktopCheck.Content = _loc["desktopShortcut"];
        CancelButton.Content = _loc["cancel"];
        ProgressTitle.Text = _loc["installing"];
        FinishTitle.Text = _loc["done"];
        FinishText.Text = _loc["doneText"];
        RunCheck.Content = _loc["run"];
        FinishButton.Content = _loc["finish"];
    }

    // -------- Переключатели темы/языка --------

    private void OnToggleTheme(object sender, RoutedEventArgs e)
    {
        _dark = !_dark;
        ApplicationThemeManager.Apply(_dark ? ApplicationTheme.Dark : ApplicationTheme.Light);
        UpdateThemeIcon();
    }

    private void UpdateThemeIcon() =>
        ThemeButton.Icon = new SymbolIcon(_dark ? SymbolRegular.WeatherSunny24 : SymbolRegular.WeatherMoon24);

    private void OnToggleLang(object sender, RoutedEventArgs e) =>
        _loc.Toggle(LangToggle.IsChecked == true ? "en" : "ru");

    // -------- Кнопки --------

    private void OnBrowse(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = _loc["location"],
            InitialDirectory = Directory.Exists(_targetDir)
                ? _targetDir
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        };
        if (dialog.ShowDialog() == true)
        {
            _targetDir = InstallService.NormalizeTarget(dialog.FolderName);
            LocationBox.Text = _targetDir;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        _cts.Cancel();
        Close();
    }

    private async void OnInstall(object sender, RoutedEventArgs e)
    {
        var desktop = _updateMode ? InstallService.HasDesktopShortcut : DesktopCheck.IsChecked == true;

        try
        {
            _targetDir = InstallService.NormalizeTarget(
                string.IsNullOrWhiteSpace(LocationBox.Text) ? _install.DefaultLocation : LocationBox.Text);

            var fromVersion = InstallService.InstalledVersion;

            if (_alreadyInstalled)
            {
                var old = InstallService.InstalledLocation;
                if (old is not null && !InstallService.SamePath(old, _targetDir))
                {
                    ShowProgress(_loc[_updateMode ? "updating" : "removingOld"]);
                    await _install.UninstallAsync(old, _progress, _cts.Token, full: false);
                }
            }

            ShowProgress(_loc[_updateMode ? "updating" : "installing"]);
            var backup = await _install.InstallAsync(_targetDir, desktop, _dark, _loc.Lang, _progress, _cts.Token,
                writeSettings: !_updateMode, keepBackup: _updateMode);
            _installed = true;

            if (_updateMode)
            {
                await CompleteUpdateAsync(fromVersion, backup);
                return;
            }

            ShowFinish(ok: true, _loc["done"], _loc["doneText"], showRun: true);
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            ShowFinish(ok: false, _loc["failed"], ex.Message);
        }
    }

    private async Task CompleteUpdateAsync(string? fromVersion, string? backup)
    {
        if (backup is null)
        {
            InstallService.Launch(_targetDir);
            Close();
            return;
        }

        try
        {
            UpdateStateStore.Save(new UpdateStateDto
            {
                FromVersion = fromVersion ?? "",
                ToVersion = InstallService.PayloadVersion,
                InstallDir = _targetDir,
                BackupDir = backup,
                StartedUtc = DateTime.UtcNow
            });
        }
        catch
        {
            InstallService.DiscardBackup(backup);
            InstallService.Launch(_targetDir);
            Close();
            return;
        }

        Bar.IsIndeterminate = true;
        ProgressStage.Text = _loc["verifying"];

        var result = await InstallService.LaunchAndConfirmAsync(_targetDir, TimeSpan.FromMinutes(3), _cts.Token);
        Bar.IsIndeterminate = false;

        if (result == InstallService.StartResult.Confirmed)
        {
            InstallService.DiscardBackup(backup);
            UpdateStateStore.Delete();
            Close();
            return;
        }

        if (result == InstallService.StartResult.TimedOut)
        {
            Close();
            return;
        }

        ProgressStage.Text = _loc["rollingBack"];
        await InstallService.RollbackAsync(_targetDir, backup, fromVersion, UpdateStateStore.Load());
        UpdateStateStore.Delete();
        _installed = false;
        InstallService.Launch(_targetDir);
        ShowFinish(ok: false, _loc["rolledBack"], _loc["rolledBackText"]);
    }

    private async void OnUninstall(object sender, RoutedEventArgs e)
    {
        var dir = InstallService.InstalledLocation ?? _targetDir;
        ShowProgress(_loc["removing"]);

        try
        {
            await _install.UninstallAsync(dir, _progress, _cts.Token);
            ShowFinish(ok: true, _loc["removed"], _loc["removedText"]);
        }
        catch (OperationCanceledException)
        {
            Close();
        }
        catch (Exception ex)
        {
            ShowFinish(ok: false, _loc["failed"], ex.Message);
        }
    }

    private void ShowProgress(string title)
    {
        WelcomePage.Visibility = Visibility.Collapsed;
        ProgressPage.Visibility = Visibility.Visible;
        ProgressTitle.Text = title;
    }

    private void ShowFinish(bool ok, string title, string text, bool showRun = false)
    {
        ProgressPage.Visibility = Visibility.Collapsed;
        FinishPage.Visibility = Visibility.Visible;

        FinishIcon.Symbol = ok ? SymbolRegular.CheckmarkCircle48 : SymbolRegular.ErrorCircle48;
        FinishIcon.Foreground = new SolidColorBrush(ok ? Color.FromRgb(0x2F, 0xA6, 0x5A) : Color.FromRgb(0xE0, 0x48, 0x3E));
        FinishTitle.Text = title;
        FinishText.Text = text;
        RunCheck.Visibility = showRun ? Visibility.Visible : Visibility.Collapsed;
        FinishButton.Content = _loc[ok ? "finish" : "close"];
    }

    private void OnFinish(object sender, RoutedEventArgs e)
    {
        if (_installed && RunCheck.IsChecked == true)
            InstallService.Launch(_targetDir);
        Close();
    }

    // -------- Заставка --------

    private void PlayIntro()
    {
        Intro.Background = OverlayBackground();

        var contentW = Intro.ActualWidth > 0 ? Intro.ActualWidth : 612;
        var h = Intro.ActualHeight > 0 ? Intro.ActualHeight : 420;
        var centerY = h / 2;

        var lineWidth = contentW * 0.74;
        var lineLeft = (contentW - lineWidth) / 2;
        Canvas.SetLeft(TrailLine, lineLeft);
        Canvas.SetTop(TrailLine, centerY - 1);

        WordMark.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(WordMark, (contentW - WordMark.DesiredSize.Width) / 2);
        Canvas.SetTop(WordMark, centerY - WordMark.DesiredSize.Height - 18);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var backOut = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };

        var sb = new Storyboard();
        AddWidth(sb, TrailLine, 0, lineWidth, 80, 420, easeOut);
        Add(sb, WordMark, "Opacity", 0, 1, 200, 360, easeOut);
        Add(sb, WordScale, "ScaleX", 0.9, 1, 200, 460, backOut);
        Add(sb, WordScale, "ScaleY", 0.9, 1, 200, 460, backOut);
        Add(sb, TrailLine, "Opacity", 1, 0, 760, 360, easeOut);
        Add(sb, Intro, "Opacity", 1, 0, 880, 420, easeOut);

        sb.Completed += (_, _) => Intro.Visibility = Visibility.Collapsed;
        sb.Begin(this);
    }

    private static Brush OverlayBackground()
    {
        var dark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.5, 0.44),
            Center = new Point(0.5, 0.44)
        };

        if (dark)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x2A, 0x32, 0x42), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x1E, 0x24, 0x30), 1));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xF1, 0xF6, 0xFD), 1));
        }

        return brush;
    }

    private static void Add(Storyboard sb, DependencyObject target, string path,
        double from, double to, double beginMs, double durMs, IEasingFunction ease)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durMs))
        { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = ease };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, new PropertyPath(path));
        sb.Children.Add(a);
    }

    private static void AddWidth(Storyboard sb, FrameworkElement target,
        double from, double to, double beginMs, double durMs, IEasingFunction ease)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durMs))
        { BeginTime = TimeSpan.FromMilliseconds(beginMs), EasingFunction = ease };
        Storyboard.SetTarget(a, target);
        Storyboard.SetTargetProperty(a, new PropertyPath(WidthProperty));
        sb.Children.Add(a);
    }
}
