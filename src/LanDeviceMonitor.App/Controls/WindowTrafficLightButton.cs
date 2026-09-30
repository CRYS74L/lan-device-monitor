using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

public sealed class WindowTrafficLightButton : Button
{
    public static readonly DependencyProperty GlyphVisibleProperty = DependencyProperty.Register(
        nameof(GlyphVisible), typeof(bool), typeof(WindowTrafficLightButton), new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
    public bool GlyphVisible { get => (bool)GetValue(GlyphVisibleProperty); set => SetValue(GlyphVisibleProperty, value); }
    public string Kind { get; set; } = "Close";
    private static readonly Geometry Green = FrozenGeometry("M7,5.86 H10.95 Q11.65,5.86 11.59,6.56 Q11.56,7.10 10.90,7.76 L7.76,10.90 Q7.10,11.56 6.56,11.59 Q5.86,11.65 5.86,10.95 V7 C5.86,6.37 6.37,5.86 7,5.86 Z M13,14.14 H9.05 Q8.35,14.14 8.41,13.44 Q8.44,12.90 9.10,12.24 L12.24,9.10 Q12.90,8.44 13.44,8.41 Q14.14,8.35 14.14,9.05 V13 C14.14,13.63 13.63,14.14 13,14.14 Z");
    private static Geometry FrozenGeometry(string data) { var value = Geometry.Parse(data); value.Freeze(); return value; }
    private static SolidColorBrush Brush(string value) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }
    private static readonly SolidColorBrush GreenInk = Brush("#00812E");
    private static readonly Pen YellowPen = RoundPen("#665000");
    private static readonly Pen RedPen = RoundPen("#99000000");
    private static Pen RoundPen(string color) { var pen = new Pen(Brush(color), 1.82) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }; pen.Freeze(); return pen; }
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsPressedProperty || e.Property == IsEnabledProperty || e.Property == BackgroundProperty) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        // One drawing origin for the circle and every symbol: no separate layout/snapping passes.
        drawing.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));
        drawing.PushTransform(new TranslateTransform((ActualWidth - 20) / 2, (ActualHeight - 20) / 2));
        if (!IsEnabled) drawing.PushOpacity(0.4);
        drawing.DrawEllipse(Background, null, new Point(10,10), 10,10);
        if (IsPressed) drawing.DrawEllipse(Brush("#28000000"), null, new Point(10,10), 10,10);
        if (GlyphVisible)
        {
            if (Kind == "Minimize") drawing.DrawLine(YellowPen, new Point(7.09,10), new Point(12.91,10));
            else if (Kind == "Maximize") drawing.DrawGeometry(GreenInk, null, Green);
            else
            {
                drawing.DrawLine(RedPen, new Point(7.2,7.2), new Point(12.8,12.8));
                drawing.DrawLine(RedPen, new Point(12.8,7.2), new Point(7.2,12.8));
            }
        }
        if (!IsEnabled) drawing.Pop();
        drawing.Pop();
    }
}
