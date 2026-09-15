using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using WinStart.App.ViewModels;

namespace WinStart.App.Controls;

public partial class UnattendPage : UserControl
{
    public static readonly DependencyProperty ScrollOffsetProperty = DependencyProperty.Register(
        nameof(ScrollOffset), typeof(double), typeof(UnattendPage),
        new PropertyMetadata(0d, (d, e) => ((UnattendPage)d).FormScroll.ScrollToVerticalOffset((double)e.NewValue)));

    public double ScrollOffset
    {
        get => (double)GetValue(ScrollOffsetProperty);
        set => SetValue(ScrollOffsetProperty, value);
    }

    public UnattendPage()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                e.Handled = true;
            }
        };
    }

    private void OnTocTopClick(object sender, RoutedEventArgs e) => ScrollTo(0);

    private void OnTocClick(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: FormSection section }) return;
        if (FormList.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement container) return;

        var top = container.TranslatePoint(new Point(0, 0), FormScroll).Y;
        ScrollTo(FormScroll.VerticalOffset + top - 4);
    }

    private void ScrollTo(double offset)
    {
        offset = Math.Max(0, Math.Min(offset, FormScroll.ScrollableHeight));
        var animation = new DoubleAnimation(FormScroll.VerticalOffset, offset, TimeSpan.FromMilliseconds(320))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(ScrollOffsetProperty, animation);
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (DataContext is not UnattendViewModel vm) return;

        FormSection? current = null;
        foreach (var section in vm.Sections)
        {
            if (!section.IsVisible) continue;
            if (FormList.ItemContainerGenerator.ContainerFromItem(section) is not FrameworkElement container) continue;

            var top = container.TranslatePoint(new Point(0, 0), FormScroll).Y;
            if (top <= 80) current = section;
            else break;
        }

        if (FormScroll.ScrollableHeight > 0 && FormScroll.VerticalOffset >= FormScroll.ScrollableHeight - 1)
            current = vm.Sections.LastOrDefault(s => s.IsVisible) ?? current;

        foreach (var section in vm.Sections) section.IsCurrent = section == current;
    }
}
