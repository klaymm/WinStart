using System.Windows;
using System.Windows.Controls;

namespace WinStart.App.Controls;

public partial class TextPreview : UserControl
{
    public TextPreview(string text)
    {
        InitializeComponent();
        Body.Text = text;
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(Body.Text);
            Copied.Visibility = Visibility.Visible;
        }
        catch { }
    }
}
