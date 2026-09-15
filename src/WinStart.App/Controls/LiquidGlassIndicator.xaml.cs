using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace WinStart.App.Controls;

public partial class LiquidGlassIndicator : UserControl
{
    private static readonly DependencyProperty TopEdgeProperty = DependencyProperty.Register(
        "TopEdge", typeof(double), typeof(LiquidGlassIndicator), new PropertyMetadata(0.0, OnEdgeChanged));

    private static readonly DependencyProperty BottomEdgeProperty = DependencyProperty.Register(
        "BottomEdge", typeof(double), typeof(LiquidGlassIndicator), new PropertyMetadata(0.0, OnEdgeChanged));

    private double _restHeight = 42;
    private bool _placed;
    private int _moveId;

    private bool _hidden;

    public LiquidGlassIndicator() => InitializeComponent();

    public void SetHidden(bool hidden)
    {
        if (_hidden == hidden) return;
        _hidden = hidden;
        if (!_placed) return;

        Glass.BeginAnimation(OpacityProperty,
            new DoubleAnimation(hidden ? 0 : 1, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase() });
    }

    private double TopEdge
    {
        get => (double)GetValue(TopEdgeProperty);
        set => SetValue(TopEdgeProperty, value);
    }

    private double BottomEdge
    {
        get => (double)GetValue(BottomEdgeProperty);
        set => SetValue(BottomEdgeProperty, value);
    }

    public void MoveTo(Rect target, bool animate, Action? completed = null)
    {
        var id = ++_moveId;
        _restHeight = target.Height;
        Glass.Width = target.Width;
        Canvas.SetLeft(Glass, target.X);

        var fromTop = TopEdge;
        var fromBottom = BottomEdge;
        BeginAnimation(TopEdgeProperty, null);
        BeginAnimation(BottomEdgeProperty, null);

        var distance = Math.Abs(target.Top - fromTop);
        if (!animate || !_placed || distance < 0.5)
        {
            TopEdge = target.Top;
            BottomEdge = target.Bottom;
            Apply();

            if (!_placed)
            {
                _placed = true;
                Glass.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            }

            completed?.Invoke();
            return;
        }

        TopEdge = fromTop;
        BottomEdge = fromBottom;

        var down = target.Top > fromTop;

        var leadMs = Math.Clamp(250 + distance * 0.45, 280, 460);
        var trailMs = leadMs + Math.Clamp(80 + distance * 0.12, 100, 170);
        const double trailDelayMs = 30;
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };

        var lead = new DoubleAnimation(down ? target.Bottom : target.Top, TimeSpan.FromMilliseconds(leadMs))
        {
            EasingFunction = ease
        };
        var trail = new DoubleAnimation(down ? target.Top : target.Bottom, TimeSpan.FromMilliseconds(trailMs))
        {
            BeginTime = TimeSpan.FromMilliseconds(trailDelayMs),
            EasingFunction = ease
        };
        trail.Completed += (_, _) =>
        {
            if (id == _moveId) completed?.Invoke();
        };

        BeginAnimation(down ? BottomEdgeProperty : TopEdgeProperty, lead);
        BeginAnimation(down ? TopEdgeProperty : BottomEdgeProperty, trail);

        var totalMs = trailDelayMs + trailMs;
        PlaySheen(totalMs);
        PlayJelly(totalMs - 70);
    }

    private static void OnEdgeChanged(DependencyObject d, DependencyPropertyChangedEventArgs e) =>
        ((LiquidGlassIndicator)d).Apply();

    private void Apply()
    {
        var top = TopEdge;
        var height = Math.Max(BottomEdge - top, _restHeight * 0.6);
        Canvas.SetTop(Glass, top);
        Glass.Height = height;

        var stretch = height / Math.Max(_restHeight, 1);
        StretchScale.ScaleX = Math.Clamp(1 / Math.Sqrt(stretch), 0.88, 1.04);
        PillScale.ScaleY = Math.Clamp(stretch, 1, 3);
    }

    private void PlayJelly(double beginMs)
    {
        var begin = TimeSpan.FromMilliseconds(Math.Max(0, beginMs));
        JellyScale.BeginAnimation(ScaleTransform.ScaleYProperty,
            Keys(begin, (1, 0), (0.9, 110), (1.04, 250), (0.99, 350), (1, 440)));
        JellyScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            Keys(begin, (1, 0), (1.03, 110), (0.985, 250), (1.005, 350), (1, 440)));
    }

    private void PlaySheen(double durationMs)
    {
        Sheen.BeginAnimation(OpacityProperty,
            Keys(TimeSpan.Zero, (0.45, 0), (1, durationMs * 0.4), (0.45, durationMs)));

        var sweep = new PointAnimationUsingKeyFrames();
        foreach (var (x, ms) in new[] { (0.3, 0.0), (0.72, durationMs * 0.45), (0.3, durationMs) })
            sweep.KeyFrames.Add(new EasingPointKeyFrame(new Point(x, 0), Ms(ms),
                new SineEase { EasingMode = EasingMode.EaseInOut }));

        SheenBrush.BeginAnimation(RadialGradientBrush.CenterProperty, sweep);
        SheenBrush.BeginAnimation(RadialGradientBrush.GradientOriginProperty, sweep);
    }

    private static DoubleAnimationUsingKeyFrames Keys(TimeSpan begin, params (double Value, double Ms)[] frames)
    {
        var anim = new DoubleAnimationUsingKeyFrames { BeginTime = begin };
        foreach (var (value, ms) in frames)
            anim.KeyFrames.Add(new EasingDoubleKeyFrame(value, Ms(ms),
                new SineEase { EasingMode = EasingMode.EaseInOut }));
        return anim;
    }

    private static KeyTime Ms(double ms) => KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(ms));
}
