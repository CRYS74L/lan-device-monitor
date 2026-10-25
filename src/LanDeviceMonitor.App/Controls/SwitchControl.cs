using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public class SwitchControl : System.Windows.Controls.CheckBox
{
    private static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        "Progress", typeof(double), typeof(SwitchControl), new PropertyMetadata(0d, ProgressChanged));
    private readonly TranslateTransform _translation = new();
    private FrameworkElement? _accentTrack;
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (GetTemplateChild("PART_Knob") is FrameworkElement knob)
            knob.RenderTransform = _translation;
        _accentTrack = GetTemplateChild("PART_AccentTrack") as FrameworkElement;
        UpdateKnob(false);
    }

    protected override void OnChecked(RoutedEventArgs e)
    {
        base.OnChecked(e);
        UpdateKnob(true);
    }

    protected override void OnUnchecked(RoutedEventArgs e)
    {
        base.OnUnchecked(e);
        UpdateKnob(true);
    }

    private void UpdateKnob(bool animate)
    {
        var target = IsChecked == true ? 1d : 0d;
        var useMotion = animate && IsLoaded && SystemParameters.ClientAreaAnimation;
        var current = (double)GetValue(ProgressProperty);
        BeginAnimation(ProgressProperty, null);
        if (!useMotion || Math.Abs(current - target) < 0.000001)
        {
            SetValue(ProgressProperty, target);
            ApplyProgress(target);
            return;
        }
        SetValue(ProgressProperty, current);
        var motion = new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(180))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        motion.Completed += (_, _) => {
            SetValue(ProgressProperty, target);
            BeginAnimation(ProgressProperty, null);
        };
        BeginAnimation(ProgressProperty, motion, HandoffBehavior.SnapshotAndReplace);
    }

    private static void ProgressChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((SwitchControl)sender).ApplyProgress((double)args.NewValue);

    private void ApplyProgress(double value)
    {
        _translation.X = value * 20;
        if (_accentTrack is not null) _accentTrack.Opacity = value;
    }
}
