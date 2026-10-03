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
            bar.Margin=new Thickness(0,0,0,5);
            bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track",bar) is FrameworkElement track) track.Margin=new Thickness(0);
        }
        if (viewer.Template.FindName("PART_ScrollContentPresenter", viewer) is not FrameworkElement viewport) return;
        var height=Math.Max(1,viewport.ActualHeight);
        var edge=Math.Min(.22,36/height);
        if(viewport.OpacityMask is not LinearGradientBrush brush || brush.IsFrozen)
        {
            brush=new LinearGradientBrush {MappingMode=BrushMappingMode.Absolute,StartPoint=new System.Windows.Point(0,0)};
            foreach(var stop in new[]{0d,.5,1,1,1,1}) brush.GradientStops.Add(new GradientStop(Colors.Black,stop));
            viewport.OpacityMask=brush;
        }
        brush.EndPoint=new System.Windows.Point(0,height);
        brush.GradientStops[1].Offset=edge*.5;brush.GradientStops[2].Offset=edge;
        brush.GradientStops[3].Offset=1-edge;brush.GradientStops[4].Offset=1-edge*.5;brush.GradientStops[5].Offset=1;
        var top=Math.Clamp(viewer.VerticalOffset/36,0,1);
        var bottom=Math.Clamp((viewer.ScrollableHeight-viewer.VerticalOffset)/36,0,1);
        Fade(brush.GradientStops[0],1-top);Fade(brush.GradientStops[1],1-top*.2);
        Fade(brush.GradientStops[4],1-bottom*.2);Fade(brush.GradientStops[5],1-bottom);
    }
    private static void Fade(GradientStop stop,double opacity)
    {
        var target=System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity*255),0,0,0);
        var animation=new System.Windows.Media.Animation.ColorAnimation(stop.Color,target,TimeSpan.FromMilliseconds(180))
        { EasingFunction=new System.Windows.Media.Animation.CubicEase {EasingMode=System.Windows.Media.Animation.EasingMode.EaseOut} };
        stop.BeginAnimation(GradientStop.ColorProperty,animation);
    }
}
