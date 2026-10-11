using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

/// <summary>Motion confined to the compact navigation action in the 32 DIP caption.</summary>
public sealed class CaptionBackButton : Button
{
    public static readonly DependencyProperty IsNavigationAvailableProperty = DependencyProperty.Register(
        nameof(IsNavigationAvailable), typeof(bool), typeof(CaptionBackButton),
        new PropertyMetadata(false, (owner, _) => ((CaptionBackButton)owner).UpdateNavigationState()));

    private readonly TranslateTransform _placementOffset = new(-6, 0);
    private readonly ScaleTransform _interactionScale = new(1, 1);
    private readonly Dictionary<(DependencyObject, DependencyProperty), int> _motionGenerations = new();
    private Border? _hoverSurface;
    private int _navigationGeneration;

    public CaptionBackButton()
    {
        Visibility = Visibility.Collapsed;
        Opacity = 0;
        IsEnabled = false;
        IsHitTestVisible = false;
        RenderTransform = _placementOffset;
    }

    public bool IsNavigationAvailable
    {
        get => (bool)GetValue(IsNavigationAvailableProperty);
        set => SetValue(IsNavigationAvailableProperty, value);
    }

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _hoverSurface = GetTemplateChild("CaptionBackHover") as Border;
        if (GetTemplateChild("CaptionBackMotionRoot") is FrameworkElement root)
        {
            root.RenderTransformOrigin = new Point(.5, .5);
            root.RenderTransform = _interactionScale;
        }
        UpdateInteractionState(false);
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsMouseOverProperty || e.Property == IsPressedProperty || e.Property == IsEnabledProperty)
            UpdateInteractionState();
    }

    private void UpdateNavigationState()
    {
        var available = IsNavigationAvailable;
        var generation = ++_navigationGeneration;
        // An outgoing button becomes non-interactive immediately, while its
        // visual can finish fading or reverse if the next resize restores it.
        IsEnabled = available;
        IsHitTestVisible = available;
        if (available) Visibility = Visibility.Visible;
        var duration = available ? 180 : 140;
        Animate(_placementOffset, TranslateTransform.XProperty, available ? 0 : -6, duration);
        Animate(this, OpacityProperty, available ? 1 : 0, duration, completed: () =>
        {
            if (generation == _navigationGeneration && !IsNavigationAvailable)
                Visibility = Visibility.Collapsed;
        });
    }

    private void UpdateInteractionState(bool animate = true)
    {
        if (_hoverSurface is null) return;
        var pressed = IsEnabled && IsPressed;
        var hover = IsEnabled && IsMouseOver;
        var duration = pressed ? 100 : hover ? 180 : 160;
        Animate(_hoverSurface, OpacityProperty, pressed ? .82 : hover ? 1 : 0, duration, animate);
        Animate(_interactionScale, ScaleTransform.ScaleXProperty, pressed ? .97 : 1, pressed ? 100 : 160, animate);
        Animate(_interactionScale, ScaleTransform.ScaleYProperty, pressed ? .97 : 1, pressed ? 100 : 160, animate);
    }

    private void Animate(DependencyObject target, DependencyProperty property, double value, int milliseconds,
        bool animate = true, Action? completed = null)
    {
        // Snapshot the rendered value before replacing a clock, following the
        // same handoff principle as StudioControlMotion. A hover, press or
        // placement reversal must never jump back to a fixed starting state.
        var current = (double)target.GetValue(property);
        var key = (target, property);
        var generation = _motionGenerations.GetValueOrDefault(key) + 1;
        _motionGenerations[key] = generation;
        BeginMotion(target, property, null);
        if (!animate || !IsLoaded || !SystemParameters.ClientAreaAnimation || Math.Abs(current - value) < .000001)
        {
            target.SetValue(property, value);
            completed?.Invoke();
            return;
        }

        // A newly attached WPF clock can remain unticked until the next
        // rendering turn. Keep its base value at the current presentation too,
        // so that this first turn never flashes the destination value.
        target.SetValue(property, current);

        var motion = new DoubleAnimation(current, value, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        motion.Completed += (_, _) =>
        {
            if (_motionGenerations[key] != generation) return;
            // Release the resting clock so the label returns to stationary
            // text rasterization and a superseded exit cannot collapse it.
            target.SetValue(property, value);
            BeginMotion(target, property, null);
            completed?.Invoke();
        };
        BeginMotion(target, property, motion);
    }

    private static void BeginMotion(DependencyObject target, DependencyProperty property, AnimationTimeline? motion)
    {
        if (target is UIElement element) element.BeginAnimation(property, motion, HandoffBehavior.SnapshotAndReplace);
        else if (target is Animatable value) value.BeginAnimation(property, motion, HandoffBehavior.SnapshotAndReplace);
    }
}
