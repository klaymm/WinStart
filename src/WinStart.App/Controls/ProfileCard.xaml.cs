using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinStart.App.Controls;

public partial class ProfileCard : UserControl
{
    public ProfileCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Rise(TitleText, 60);
        Rise(Avatar, 100, fromScale: 0.7);
        Rise(NameText, 170);
        Rise(RoleText, 210);
    }

    private void Tile_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement tile) return;

        var index = SpecsList.ItemContainerGenerator.IndexFromContainer(VisualTreeHelper.GetParent(tile));
        Rise(tile, 250 + Math.Max(index, 0) * 45);
    }

    private static void Rise(FrameworkElement element, double delayMs, double fromScale = 1)
    {
        var begin = TimeSpan.FromMilliseconds(delayMs);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var move = new TranslateTransform(0, 10);
        var scale = new ScaleTransform(fromScale, fromScale);
        element.RenderTransformOrigin = new Point(0.5, 0.5);
        element.RenderTransform = new TransformGroup { Children = { scale, move } };
        element.Opacity = 0;

        element.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = begin, EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(380)) { BeginTime = begin, EasingFunction = ease });

        if (fromScale == 1) return;

        var grow = new DoubleAnimation(fromScale, 1, TimeSpan.FromMilliseconds(520))
        {
            BeginTime = begin,
            EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 }
        };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
    }
}
