using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

/// <summary>A single-selection, keyboard-accessible capsule with a shared sliding indicator.</summary>
public class SegmentedControl : System.Windows.Controls.ListBox
{
    private Border? _indicator;
    private readonly TranslateTransform _translation = new();

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        _indicator = GetTemplateChild("PART_Indicator") as Border;
        if (_indicator is not null) _indicator.RenderTransform = _translation;
        UpdateIndicator(false);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs e)
    {
        base.OnSelectionChanged(e);
        UpdateIndicator(true);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        UpdateIndicator(false);
    }

    private void UpdateIndicator(bool animate)
    {
        if (_indicator is null || Items.Count == 0 || ActualWidth <= 8) return;
        var width = (ActualWidth - 8) / Items.Count;
        _indicator.Width = width;
        _indicator.Visibility = SelectedIndex < 0 ? Visibility.Hidden : Visibility.Visible;
        var target = Math.Max(0, SelectedIndex) * width;
        var current = _translation.X;
        _translation.BeginAnimation(TranslateTransform.XProperty, null);
        _translation.X = target;
        if (animate && IsLoaded && SystemParameters.ClientAreaAnimation)
            _translation.BeginAnimation(TranslateTransform.XProperty,
                new DoubleAnimation(current, target, TimeSpan.FromMilliseconds(220))
                { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();
        SelectAt(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnPreviewMouseMove(System.Windows.Input.MouseEventArgs e)
    {
        base.OnPreviewMouseMove(e);
        if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed)
            SelectAt(e.GetPosition(this).X);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e);
        if (IsMouseCaptured) { ReleaseMouseCapture(); e.Handled = true; }
    }

    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (Items.Count > 0 && e.Key is Key.Left or Key.Right or Key.Home or Key.End)
        {
            SelectedIndex = e.Key switch
            {
                Key.Home => 0,
                Key.End => Items.Count - 1,
                Key.Left => Math.Max(0, SelectedIndex - 1),
                _ => Math.Min(Items.Count - 1, SelectedIndex + 1)
            };
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private void SelectAt(double x)
    {
        if (Items.Count == 0 || ActualWidth <= 8) return;
        SelectedIndex = Math.Clamp((int)((x - 4) / ((ActualWidth - 8) / Items.Count)), 0, Items.Count - 1);
    }
}
