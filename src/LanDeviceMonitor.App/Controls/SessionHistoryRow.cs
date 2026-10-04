using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Size = System.Windows.Size;
using Panel = System.Windows.Controls.Panel;

namespace LanDeviceMonitor.Controls;

/// <summary>Keep the duration alongside its dates whenever the complete content fits.</summary>
public sealed class SessionHistoryRow : Panel
{
    private const double MarkerWidth = 24;
    private const double InlineGap = 12;
    private const double StackedGap = 10;
    private bool _inline;
    private double _detailsHeight;
    private double _badgeWidth;

    protected override Size MeasureOverride(Size available)
    {
        if (InternalChildren.Count != 3) return new Size();
        foreach (UIElement child in InternalChildren) child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var details = InternalChildren[1].DesiredSize;
        var badge = InternalChildren[2].DesiredSize;
        _detailsHeight = details.Height;
        _badgeWidth = badge.Width;
        var required = MarkerWidth + details.Width + InlineGap + badge.Width;
        var width = double.IsInfinity(available.Width) ? required : available.Width;
        _inline = double.IsInfinity(available.Width) || required + 2 / VisualTreeHelper.GetDpi(this).DpiScaleX <= width;
        return new Size(width, _inline ? Math.Max(details.Height, badge.Height) : details.Height + StackedGap + badge.Height);
    }

    protected override Size ArrangeOverride(Size size)
    {
        if (InternalChildren.Count != 3) return size;
        InternalChildren[0].Arrange(new Rect(0, 0, MarkerWidth, _detailsHeight));
        InternalChildren[1].Arrange(new Rect(MarkerWidth, 0, Math.Max(0, size.Width - MarkerWidth - (_inline ? _badgeWidth + InlineGap : 0)), _detailsHeight));
        InternalChildren[2].Arrange(new Rect(_inline ? Math.Max(0, size.Width - _badgeWidth) : MarkerWidth,
            _inline ? 0 : _detailsHeight + StackedGap, _badgeWidth,
            _inline ? _detailsHeight : InternalChildren[2].DesiredSize.Height));
        return size;
    }
}
