using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;
using Pen = System.Windows.Media.Pen;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LanDeviceMonitor.Controls;

public sealed class WindowTrafficLightButton : Button
{
    public static readonly DependencyProperty GlyphVisibleProperty = DependencyProperty.Register(
        nameof(GlyphVisible), typeof(bool), typeof(WindowTrafficLightButton), new FrameworkPropertyMetadata(false, OnGlyphVisibleChanged));
    public bool GlyphVisible { get => (bool)GetValue(GlyphVisibleProperty); set => SetValue(GlyphVisibleProperty, value); }
    public string Kind { get; set; } = "Close";
    public const double Diameter = 17;
    private static readonly DependencyProperty GlyphProgressProperty = DependencyProperty.Register("GlyphProgress", typeof(double), typeof(WindowTrafficLightButton), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    private static readonly DependencyProperty GlyphOpacityProperty = DependencyProperty.Register("GlyphOpacity", typeof(double), typeof(WindowTrafficLightButton), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));
    internal double GlyphProgress => (double)GetValue(GlyphProgressProperty);
    internal double GlyphOpacity => (double)GetValue(GlyphOpacityProperty);
    private static void OnGlyphVisibleChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args)
    {
        var button = (WindowTrafficLightButton)sender;
        bool show = (bool)args.NewValue;
        button.Transition(GlyphProgressProperty, show ? 1 : 0, 100, show);
        button.Transition(GlyphOpacityProperty, show ? 1 : 0, 80, show);
    }
    private int _progressGeneration;
    private int _opacityGeneration;
    private void Transition(DependencyProperty property, double target, double milliseconds, bool entering)
    {
        double current = (double)GetValue(property);
        int generation = property == GlyphProgressProperty ? ++_progressGeneration : ++_opacityGeneration;
        BeginAnimation(property, null);
        SetValue(property, current);
        if (!IsLoaded || !SystemParameters.ClientAreaAnimation || Math.Abs(current-target)<0.001) { SetValue(property,target); return; }
        var animation = new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(milliseconds * Math.Abs(target-current)))
        { EasingFunction = new SineEase { EasingMode = entering ? EasingMode.EaseOut : EasingMode.EaseIn } };
        animation.Completed += (_, _) =>
        {
            if (generation != (property == GlyphProgressProperty ? _progressGeneration : _opacityGeneration)) return;
            BeginAnimation(property,null);
            SetValue(property,target);
        };
        BeginAnimation(property,animation);
    }
    private static readonly Geometry TopTriangle = FrozenGeometry("M7,5.86 H10.95 Q11.65,5.86 11.59,6.56 Q11.56,7.10 10.90,7.76 L7.76,10.90 Q7.10,11.56 6.56,11.59 Q5.86,11.65 5.86,10.95 V7 C5.86,6.37 6.37,5.86 7,5.86 Z");
    private static readonly Geometry BottomTriangle = FrozenGeometry("M13,14.14 H9.05 Q8.35,14.14 8.41,13.44 Q8.44,12.90 9.10,12.24 L12.24,9.10 Q12.90,8.44 13.44,8.41 Q14.14,8.35 14.14,9.05 V13 C14.14,13.63 13.63,14.14 13,14.14 Z");
    private static Geometry FrozenGeometry(string data) { var value = Geometry.Parse(data); value.Freeze(); return value; }
    private static SolidColorBrush Brush(string value) { var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(value)!; brush.Freeze(); return brush; }
    private static readonly SolidColorBrush PressedInk = Brush("#28000000");
    private System.Windows.Media.Brush? _surfaceBrush;
    private Pen? _glyphPen;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == BackgroundProperty || e.Property == BorderBrushProperty)
        {
            _surfaceBrush = null;
            InvalidateVisual();
        }
        if (e.Property == ForegroundProperty) { _glyphPen = null; InvalidateVisual(); }
        if (e.Property == IsPressedProperty || e.Property == IsEnabledProperty || e.Property == BackgroundProperty) InvalidateVisual();
    }
    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        // One drawing origin for the circle and every symbol: no separate layout/snapping passes.
        drawing.DrawRectangle(System.Windows.Media.Brushes.Transparent, null, new Rect(RenderSize));
        drawing.PushTransform(new TranslateTransform((ActualWidth - Diameter) / 2, (ActualHeight - Diameter) / 2));
        drawing.PushTransform(new ScaleTransform(Diameter / 20, Diameter / 20)); // Fixed size, never animated.
        if (!IsEnabled) drawing.PushOpacity(0.4);
        if (_surfaceBrush is null && Background is SolidColorBrush face && BorderBrush is SolidColorBrush rim)
        {
            var surface = new RadialGradientBrush { Center = new Point(0.5,0.5), GradientOrigin = new Point(0.5,0.5), RadiusX = 0.5, RadiusY = 0.5 };
            surface.GradientStops.Add(new GradientStop(face.Color,0));
            surface.GradientStops.Add(new GradientStop(face.Color,0.77));
            surface.GradientStops.Add(new GradientStop(rim.Color,0.93));
            surface.GradientStops.Add(new GradientStop(rim.Color,1));
            surface.Freeze(); _surfaceBrush = surface;
        }
        drawing.DrawEllipse(_surfaceBrush ?? Background, null, new Point(10,10), 10,10);
        if (IsPressed) drawing.DrawEllipse(PressedInk, null, new Point(10,10), 10,10);
        var progress = GlyphProgress;
        if (GlyphOpacity > 0 && progress > 0)
        {
            drawing.PushOpacity(GlyphOpacity);
            if (_glyphPen is null)
            {
                _glyphPen = new Pen(Foreground.CloneCurrentValue(),1.82) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                if (_glyphPen.CanFreeze) _glyphPen.Freeze();
            }
            if (Kind == "Minimize") drawing.DrawLine(_glyphPen, new Point(10-2.91*progress,10), new Point(10+2.91*progress,10));
            else if (Kind == "Maximize")
            {
                double extent = 4.14 * progress;
                drawing.PushClip(new RectangleGeometry(new Rect(10-extent,10-extent,2*extent,2*extent),0.9,0.9));
                double merge = 0.9*(1-progress);
                drawing.PushTransform(new TranslateTransform(merge,merge)); drawing.DrawGeometry(Foreground,null,TopTriangle); drawing.Pop();
                drawing.PushTransform(new TranslateTransform(-merge,-merge)); drawing.DrawGeometry(Foreground,null,BottomTriangle); drawing.Pop();
                drawing.Pop();
            }
            else
            {
                double half = 2.8*progress;
                drawing.DrawLine(_glyphPen, new Point(10-half,10-half), new Point(10+half,10+half));
                drawing.DrawLine(_glyphPen, new Point(10+half,10-half), new Point(10-half,10+half));
            }
            drawing.Pop();
        }
        if (!IsEnabled) drawing.Pop();
        drawing.Pop();
        drawing.Pop();
    }
}
