using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinStart.App.Controls;

public partial class TweakCard : UserControl
{
    private const int AnimatedCards = 8;
    private bool _shown;

    public TweakCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_shown) return;
        _shown = true;

        var index = IndexInList();
        if (index >= AnimatedCards) return;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        var begin = TimeSpan.FromMilliseconds(index * 35);
        var move = new TranslateTransform(0, 12);

        RenderTransform = move;
        CacheMode = new BitmapCache();
        Opacity = 0;

        var slide = new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(340)) { BeginTime = begin, EasingFunction = ease };
        slide.Completed += (_, _) => CacheMode = null;

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = begin, EasingFunction = ease });
        move.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private int IndexInList()
    {
        DependencyObject? node = this;
        while (node is not null && node is not ItemsControl)
            node = VisualTreeHelper.GetParent(node);

        if (node is ItemsControl itemsControl
            && itemsControl.ContainerFromElement(this) is { } container)
        {
            var index = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
            if (index >= 0) return index;
        }

        return 0;
    }
}
