using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Wpf.Ui.Controls;

namespace WinStart.App.Infrastructure;

public static class DialogMotion
{
    private enum State { Open, Closing, Closed }

    public static void Attach(ContentDialog dialog)
    {
        var scale = new ScaleTransform(0.9, 0.9);
        var shift = new TranslateTransform(0, 14);
        dialog.RenderTransformOrigin = new Point(0.5, 0.5);
        dialog.RenderTransform = new TransformGroup { Children = { scale, shift } };
        dialog.Opacity = 0;

        dialog.Loaded += (_, _) => PlayOpen(dialog, scale, shift);

        var state = State.Open;
        dialog.Closing += (d, args) =>
        {
            if (state == State.Closed) return;

            args.Cancel = true;
            if (state == State.Closing) return;

            state = State.Closing;
            var result = args.Result;
            PlayClose(d, scale, shift, () =>
            {
                state = State.Closed;
                d.Hide(result);
            });
        };
    }

    private static void PlayOpen(UIElement dialog, ScaleTransform scale, TranslateTransform shift)
    {
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var spring = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 };

        dialog.BeginAnimation(UIElement.OpacityProperty,
            new DoubleAnimation(0, 1, Ms(220)) { EasingFunction = easeOut });

        var grow = new DoubleAnimation(0.9, 1, Ms(460)) { EasingFunction = spring };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        shift.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(14, 0, Ms(420)) { EasingFunction = spring });

        var blur = new BlurEffect { Radius = 12, RenderingBias = RenderingBias.Performance };
        dialog.Effect = blur;
        var unblur = new DoubleAnimation(12, 0, Ms(280)) { EasingFunction = easeOut };
        unblur.Completed += (_, _) =>
        {
            if (dialog.Effect == blur) dialog.Effect = null;
        };
        blur.BeginAnimation(BlurEffect.RadiusProperty, unblur);
    }

    private static void PlayClose(UIElement dialog, ScaleTransform scale, TranslateTransform shift, Action done)
    {
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        var blur = new BlurEffect { Radius = 0, RenderingBias = RenderingBias.Performance };
        dialog.Effect = blur;
        blur.BeginAnimation(BlurEffect.RadiusProperty, new DoubleAnimation(0, 10, Ms(200)) { EasingFunction = easeIn });

        var shrink = new DoubleAnimation(0.94, Ms(200)) { EasingFunction = easeIn };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
        shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, Ms(200)) { EasingFunction = easeIn });

        var fade = new DoubleAnimation(0, Ms(190)) { EasingFunction = easeIn };
        fade.Completed += (_, _) => done();
        dialog.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private static TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms);
}
