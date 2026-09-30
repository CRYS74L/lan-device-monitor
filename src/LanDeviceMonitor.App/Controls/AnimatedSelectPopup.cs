using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

/// <summary>Preserves the native ComboBox popup while its 180 ms exit completes.</summary>
public sealed class AnimatedSelectPopup : Popup
{
    private bool _closing;
    private bool _finishClose;
    private int _generation;

    static AnimatedSelectPopup() => IsOpenProperty.OverrideMetadata(typeof(AnimatedSelectPopup),
        new FrameworkPropertyMetadata(false, null, CoerceOpen));

    public AnimatedSelectPopup()
    {
        Opened += (_, _) =>
        {
            HorizontalOffset = -12;
            if (Child is FrameworkElement panel)
            {
                panel.Opacity = 0;
                panel.RenderTransform = new TranslateTransform(0, 2);
                Animate(true);
            }
        };
    }

    private static object CoerceOpen(DependencyObject value, object requested)
    {
        var popup = (AnimatedSelectPopup)value;
        if ((bool)requested)
        {
            if (popup._closing) { popup._closing = false; popup.Animate(true); }
            return true;
        }
        if (popup._finishClose || !popup.IsOpen || !SystemParameters.ClientAreaAnimation) return false;
        if (!popup._closing) { popup._closing = true; popup.Animate(false); }
        return true;
    }

    private void Animate(bool open)
    {
        if (Child is not FrameworkElement panel) return;
        var generation = ++_generation;
        panel.IsHitTestVisible = open;
        if (panel.RenderTransform is not TranslateTransform transform)
            panel.RenderTransform = transform = new TranslateTransform();
        var duration = SystemParameters.ClientAreaAnimation ? TimeSpan.FromMilliseconds(180) : TimeSpan.Zero;
        var opacity = Motion(open ? 1 : 0, duration);
        opacity.Completed += (_, _) =>
        {
            if (open || generation != _generation) return;
            _finishClose = true;
            CoerceValue(IsOpenProperty);
            _finishClose = false;
            _closing = false;
        };
        panel.BeginAnimation(UIElement.OpacityProperty, opacity, HandoffBehavior.SnapshotAndReplace);
        transform.BeginAnimation(TranslateTransform.YProperty, Motion(open ? 0 : 2, duration), HandoffBehavior.SnapshotAndReplace);
    }

    private static DoubleAnimationUsingKeyFrames Motion(double target, TimeSpan duration)
    {
        var animation = new DoubleAnimationUsingKeyFrames();
        animation.KeyFrames.Add(new SplineDoubleKeyFrame(target, KeyTime.FromTimeSpan(duration), new KeySpline(.23, 1, .32, 1)));
        return animation;
    }
}
