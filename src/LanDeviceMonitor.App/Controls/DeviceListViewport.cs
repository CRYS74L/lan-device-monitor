using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

public static class DeviceListViewport
{
    public static readonly DependencyProperty DistributeFadeProperty = DependencyProperty.RegisterAttached(
        "DistributeFade", typeof(bool), typeof(DeviceListViewport), new PropertyMetadata(false, (target, _) => { if (target is ScrollViewer viewer && viewer.IsLoaded) Update(viewer); }));
    public static bool GetDistributeFade(DependencyObject target) => (bool)target.GetValue(DistributeFadeProperty);
    public static void SetDistributeFade(DependencyObject target, bool value) => target.SetValue(DistributeFadeProperty, value);
    private sealed record SectionFade(double Height, double Top, double Bottom, double Y, System.Windows.Media.Brush Brush);
    private static readonly DependencyProperty SectionFadeProperty = DependencyProperty.RegisterAttached(
        "SectionFade", typeof(SectionFade), typeof(DeviceListViewport));
    private sealed record FadeState(double Height, double Top, double Bottom, System.Windows.Media.Brush Brush);
    private static readonly DependencyProperty FadeStateProperty = DependencyProperty.RegisterAttached(
        "FadeState", typeof(FadeState), typeof(DeviceListViewport));
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
        if (viewer.Template.FindName("DeviceViewportMask",viewer) is not null && viewer.Template.FindName("PART_VerticalScrollBar",viewer) is System.Windows.Controls.Primitives.ScrollBar bar)
        {
            bar.Margin=new Thickness(0,0,0,5);
            bar.ApplyTemplate();
            if (bar.Template.FindName("PART_Track",bar) is FrameworkElement track) track.Margin=new Thickness(0);
        }
        if ((viewer.Template.FindName("DeviceViewportMask",viewer) ?? viewer.Template.FindName("DetailViewportMask",viewer)) is not FrameworkElement viewport) return;
        var height=Math.Max(1,viewport.ActualHeight);
        // Keep alpha zero at a clipped edge. Grow the fade's length as content
        // moves beyond that edge; changing endpoint alpha leaves a visible cut.
        var top=Math.Min(36,Math.Max(0,viewer.VerticalOffset));
        var bottom=Math.Min(36,Math.Max(0,viewer.ScrollableHeight-viewer.VerticalOffset));
        if (GetDistributeFade(viewer) && viewer.Content is StackPanel sections)
        {
            // Only sections crossing an edge need an intermediate alpha surface.
            // A viewport-wide mask makes a scrolling chart repaint the entire
            // detail page, including static text and history, on every frame.
            viewport.OpacityMask = null;
            foreach (FrameworkElement section in sections.Children)
            {
                if (!section.IsVisible) continue;
                var y = section.TranslatePoint(new System.Windows.Point(), viewport).Y;
                var previousSection = section.GetValue(SectionFadeProperty) as SectionFade;
                var crossesEdge = top > 0 && y < top || bottom > 0 && y + section.ActualHeight > height - bottom;
                if (!crossesEdge)
                {
                    if (previousSection is not null) { section.OpacityMask = null; section.ClearValue(SectionFadeProperty); }
                    continue;
                }
                if (previousSection is not null && previousSection.Height == height && previousSection.Top == top &&
                    previousSection.Bottom == bottom && previousSection.Y == y) continue;
                var sectionBrush = CreateFade(height, top, bottom);
                sectionBrush.MappingMode = BrushMappingMode.Absolute;
                sectionBrush.StartPoint = new System.Windows.Point(0, -y);
                sectionBrush.EndPoint = new System.Windows.Point(0, height - y);
                sectionBrush.Freeze();
                section.OpacityMask = sectionBrush;
                section.SetValue(SectionFadeProperty, new SectionFade(height, top, bottom, y, sectionBrush));
            }
            return;
        }
        if (viewer.Content is StackPanel previousSections)
            foreach (FrameworkElement section in previousSections.Children)
                if (section.GetValue(SectionFadeProperty) is SectionFade)
                { section.OpacityMask = null; section.ClearValue(SectionFadeProperty); }
        if (viewport.GetValue(FadeStateProperty) is FadeState previous &&
            previous.Height == height && previous.Top == top && previous.Bottom == bottom &&
            ReferenceEquals(viewport.OpacityMask, previous.Brush)) return;
        var brush=CreateFade(height,top,bottom);
        brush.Freeze();viewport.OpacityMask=brush;
        viewport.SetValue(FadeStateProperty, new FadeState(height, top, bottom, brush));
    }
    private static LinearGradientBrush CreateFade(double height, double top, double bottom)
    {
        var brush=new LinearGradientBrush {StartPoint=new System.Windows.Point(0,0),EndPoint=new System.Windows.Point(0,1)};
        foreach(var (position,opacity) in new[]{(0d,0d),(.25,.15625),(.5,.5),(.75,.84375),(1d,1d)})
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity*255),0,0,0),position*top/height));
        foreach(var (position,opacity) in new[]{(0d,1d),(.25,.84375),(.5,.5),(.75,.15625),(1d,0d)})
            brush.GradientStops.Add(new GradientStop(System.Windows.Media.Color.FromArgb((byte)Math.Round(opacity*255),0,0,0),1-bottom/height+position*bottom/height));
        return brush;
    }
}
