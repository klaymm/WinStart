using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using WinStart.App.Services;
using WinStart.App.ViewModels;

namespace WinStart.App.Views;

public partial class ShellWindow : FluentWindow
{
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;

    public ShellWindow(ShellViewModel viewModel, IDialogService dialogs, ISettingsService settings)
    {
        _dialogs = dialogs;
        _settings = settings;
        InitializeComponent();
        DataContext = viewModel;

        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 24);
        Height = Math.Min(Height, area.Height - 24);

        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _dialogs.SetContentPresenter(RootContentDialog);

        if (DataContext is ShellViewModel vm)
            vm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName != nameof(ShellViewModel.CurrentContent)) return;

                PlayPageTransition();
                MoveNavGlass(animate: true);
            };

        Dispatcher.InvokeAsync(() => MoveNavGlass(animate: false), DispatcherPriority.Loaded);
        NavScroll.ScrollChanged += (_, _) => MoveNavGlass(animate: false);
        NavPanel.SizeChanged += (_, _) => MoveNavGlass(animate: false);

        PreviewKeyDown += (_, args) =>
        {
            if (args.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                args.Handled = true;
            }
            else if (args.Key == Key.Escape && SearchBox.IsKeyboardFocusWithin)
            {
                SearchBox.Text = "";
                PageHost.Focus();
                args.Handled = true;
            }
        };

        if (_settings.Current.ShowSplash)
            PlayIntro();
        else
            IntroOverlay.Visibility = Visibility.Collapsed;
    }

    private void MoveNavGlass(bool animate)
    {
        if (DataContext is not ShellViewModel vm) return;

        var inTop = true;
        var item = vm.TopItems.FirstOrDefault(i => i.IsSelected);
        if (item is null)
        {
            inTop = false;
            item = vm.BottomItems.FirstOrDefault(i => i.IsSelected);
        }

        NavGlass.SetHidden(item is null);
        if (item is null) return;

        var list = inTop ? TopList : BottomList;
        if (list.ItemContainerGenerator.ContainerFromItem(item) is not ContentPresenter container
            || VisualTreeHelper.GetChildrenCount(container) == 0
            || VisualTreeHelper.GetChild(container, 0) is not FrameworkElement button
            || button.ActualHeight <= 0)
            return;

        var target = new Rect(button.TranslatePoint(new Point(0, 0), NavGlass),
            new Size(button.ActualWidth, button.ActualHeight));

        if (animate) NavGlass.Clip = null;
        NavGlass.MoveTo(target, animate, () => NavGlass.Clip = inTop ? NavScrollClip() : null);
    }

    private Geometry NavScrollClip()
    {
        var origin = NavScroll.TranslatePoint(new Point(0, 0), NavGlass);
        return new RectangleGeometry(new Rect(origin.X - 24, origin.Y,
            NavScroll.ActualWidth + 48, NavScroll.ActualHeight));
    }

    private void PlayPageTransition()
    {
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var move = new TranslateTransform(0, 10);
        PageHost.RenderTransform = move;
        PageHost.CacheMode = new BitmapCache();
        PageHost.Opacity = 0;

        var slide = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(280)) { EasingFunction = ease };
        slide.Completed += (_, _) =>
        {
            if (ReferenceEquals(PageHost.RenderTransform, move)) PageHost.CacheMode = null;
        };

        PageHost.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });

        move.BeginAnimation(TranslateTransform.YProperty, slide);
    }

    private void PlayIntro()
    {
        IntroOverlay.Background = OverlayBackground();
        WordMark.Text = $"WinStart {Infrastructure.AppInfo.ShortVersion}";

        var w = ActualWidth > 0 ? ActualWidth : Width;
        var h = ActualHeight > 0 ? ActualHeight : Height;
        var centerY = h / 2;

        const double streakW = 400;
        Canvas.SetTop(Streak, centerY + 22);
        Canvas.SetLeft(Streak, -streakW);

        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var sb = new Storyboard();

        Add(sb, IntroText, "Opacity", 0, 1, 60, 260, easeOut);

        AnimateCanvasLeft(sb, Streak, -streakW, w + streakW, 700, 850, easeInOut);

        Add(sb, WordShift, "X", -1.0, 1.0, 1600, 620, easeInOut);

        Add(sb, IntroOverlay, "Opacity", 1, 0, 2280, 420, easeOut);

        sb.Completed += (_, _) => IntroOverlay.Visibility = Visibility.Collapsed;
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
        double from, double to, double beginMs, double durationMs, IEasingFunction ease)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(path));
        sb.Children.Add(anim);
    }

    private static void AnimateCanvasLeft(Storyboard sb, FrameworkElement target,
        double from, double to, double beginMs, double durationMs, IEasingFunction ease)
    {
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(beginMs),
            EasingFunction = ease
        };
        Storyboard.SetTarget(anim, target);
        Storyboard.SetTargetProperty(anim, new PropertyPath(Canvas.LeftProperty));
        sb.Children.Add(anim);
    }
}
