using System.Windows.Media;
using Wpf.Ui.Appearance;

namespace WinStart.App.Services;

public interface IThemeService
{
    void Initialize();

    void Apply(AppTheme theme);
}

public sealed class ThemeService : IThemeService
{
    private static readonly Color AccentLight = Color.FromRgb(0x0A, 0x9E, 0xF0);
    private static readonly Color AccentDark = Color.FromRgb(0x36, 0xB8, 0xFF);

    private readonly ISettingsService _settings;

    public ThemeService(ISettingsService settings) => _settings = settings;

    public void Initialize() => ApplyTheme(_settings.Current.Theme);

    public void Apply(AppTheme theme)
    {
        _settings.Current.Theme = theme;
        _settings.Save();
        ApplyTheme(theme);
    }

    private static void ApplyTheme(AppTheme theme)
    {
        ApplicationTheme applied;
        if (theme == AppTheme.System)
        {
            ApplicationThemeManager.ApplySystemTheme();
            applied = ApplicationThemeManager.GetAppTheme();
        }
        else
        {
            applied = theme == AppTheme.Dark ? ApplicationTheme.Dark : ApplicationTheme.Light;
            ApplicationThemeManager.Apply(applied, updateAccent: false);
        }

        ApplicationAccentColorManager.Apply(applied == ApplicationTheme.Dark ? AccentDark : AccentLight, applied);
    }
}
