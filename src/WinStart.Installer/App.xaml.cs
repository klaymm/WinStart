using System.Windows;
using Wpf.Ui.Appearance;

namespace WinStart.Installer;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplicationThemeManager.Apply(ApplicationTheme.Light);

        var window = new InstallerWindow();
        MainWindow = window;
        window.Show();
    }
}
