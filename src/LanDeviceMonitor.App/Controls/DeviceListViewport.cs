using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

public static class DeviceListViewport
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(DeviceListViewport), new PropertyMetadata(false, Attach));
    public static bool GetEnabled(DependencyObject target) => (bool)target.GetValue(EnabledProperty);
    public static void SetEnabled(DependencyObject target, bool value) => target.SetValue(EnabledProperty, value);
    private static void Attach(DependencyObject target, DependencyPropertyChangedEventArgs args)
    {
        if (target is not ScrollViewer viewer || args.NewValue is not true) return;
        viewer.Loaded += (_, _) => Update(viewer);
        viewer.ScrollChanged += (_, _) => Update(viewer);
        viewer.SizeChanged += (_, _) => Update(viewer);
    }
    private static void Update(ScrollViewer viewer)
    {
        if (viewer.Template.FindName("PART_VerticalScrollBar",viewer) is System.Windows.Controls.Primitives.ScrollBar bar)
        {
            bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track",bar) is FrameworkElement track) track.Margin=new Thickness(0);
        }
        if (viewer.Template.FindName("PART_ScrollContentPresenter", viewer) is not FrameworkElement viewport) return;
        var edge = Math.Min(.12, 12 / Math.Max(1, viewport.ActualHeight));
        // Absolute viewport coordinates keep the fade at the clipping boundary,
        // rather than mapping it over the scroll content's much taller drawing bounds.
        var brush = new LinearGradientBrush { MappingMode=BrushMappingMode.Absolute,
            StartPoint=new System.Windows.Point(0,0), EndPoint=new System.Windows.Point(0,Math.Max(1,viewport.ActualHeight)) };
        brush.GradientStops.Add(new GradientStop(viewer.VerticalOffset > .5 ? Colors.Transparent : Colors.Black, 0));
        brush.GradientStops.Add(new GradientStop(Colors.Black, edge));
        brush.GradientStops.Add(new GradientStop(Colors.Black, 1-edge));
        brush.GradientStops.Add(new GradientStop(viewer.VerticalOffset < viewer.ScrollableHeight-.5 ? Colors.Transparent : Colors.Black, 1));
        brush.Freeze(); viewport.OpacityMask = brush;
    }
}
