using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinStart.App.Controls;

public partial class TweakCard : UserControl
{
    private bool _animated;

    public TweakCard()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_animated) return;
        _animated = true;

        var index = IndexInList();
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 14) * 38);

        var move = new TranslateTransform(0, 16);
        RenderTransform = move;
        Opacity = 0;

        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(280)))
            { BeginTime = delay, EasingFunction = ease });

        move.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(16, 0, new Duration(TimeSpan.FromMilliseconds(360)))
            { BeginTime = delay, EasingFunction = ease });
    }

    private int IndexInList()
    {
        DependencyObject? node = this;
        while (node is not null && node is not ItemsControl)
            node = VisualTreeHelper.GetParent(node);

        if (node is ItemsControl itemsControl)
        {
            var container = itemsControl.ContainerFromElement(this);
            if (container is not null)
            {
                var idx = itemsControl.ItemContainerGenerator.IndexFromContainer(container);
                if (idx >= 0) return idx;
            }
        }

        return 0;
    }
}
