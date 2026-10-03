using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace LanDeviceMonitor.Controls;

/// <summary>Draws translucent cards with one continuous, pixel-aligned outline.</summary>
public sealed class RoundedCardBorder : Border
{
    protected override void OnRender(DrawingContext drawingContext)
    {
        if (BorderThickness.Left != BorderThickness.Right ||
            BorderThickness.Left != BorderThickness.Top ||
            BorderThickness.Left != BorderThickness.Bottom ||
            CornerRadius.TopLeft != CornerRadius.TopRight ||
            CornerRadius.TopLeft != CornerRadius.BottomLeft ||
            CornerRadius.TopLeft != CornerRadius.BottomRight)
        {
            base.OnRender(drawingContext);
            return;
        }
        var size = RenderSize;
        if (size.Width <= 0 || size.Height <= 0) return;
        var dpi = VisualTreeHelper.GetDpi(this).DpiScaleX;
        var stroke = Math.Round(BorderThickness.Left * dpi) / dpi;
        var radius = Math.Min(CornerRadius.TopLeft, Math.Min(size.Width, size.Height) / 2);
        // Centered MaxWidth containers can position a card at a fractional
        // device pixel. Snap both edges of the stroke, not just its centerline;
        // otherwise a side becomes two half-opacity pixels while the top is one.
        var guidelines = new GuidelineSet();
        foreach (var x in new[] { 0, stroke, size.Width - stroke, size.Width }) guidelines.GuidelinesX.Add(x);
        foreach (var y in new[] { 0, stroke, size.Height - stroke, size.Height }) guidelines.GuidelinesY.Add(y);
        drawingContext.PushGuidelineSet(guidelines);
        drawingContext.DrawRoundedRectangle(Background, null, new Rect(size), radius, radius);
        if (BorderBrush is not null && stroke > 0)
        {
            var half = stroke / 2;
            var bounds = new Rect(half, half, Math.Max(0, size.Width - stroke), Math.Max(0, size.Height - stroke));
            drawingContext.DrawRoundedRectangle(null, new System.Windows.Media.Pen(BorderBrush, stroke), bounds,
                Math.Max(0, radius - half), Math.Max(0, radius - half));
        }
        drawingContext.Pop();
    }
}
