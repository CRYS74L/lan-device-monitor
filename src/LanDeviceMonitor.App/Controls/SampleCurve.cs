using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

internal static class SampleCurve
{
    internal readonly record struct Bounds(double Minimum, double Maximum);

    internal static Bounds? VisibleBounds(IReadOnlyList<Point> points, IReadOnlyList<EChartsCurve.Segment> segments,
        double left, double right)
    {
        if (points.Count == 0 || right < points[0].X || left > points[^1].X) return null;
        var min = double.PositiveInfinity; var max = double.NegativeInfinity;
        foreach (var segment in segments)
        {
            if (segment.End.X < left || segment.Start.X > right) continue;
            var a = SegmentValue(segment, Math.Max(left, segment.Start.X));
            var b = SegmentValue(segment, Math.Min(right, segment.End.X));
            min = Math.Min(min, Math.Min(a, b)); max = Math.Max(max, Math.Max(a, b));
        }
        foreach (var point in points)
            if (point.X >= left && point.X <= right)
            { min = Math.Min(min, point.Y); max = Math.Max(max, point.Y); }
        return double.IsFinite(min) ? new(min, max) : null;
    }

    internal static double ValueAt(IReadOnlyList<Point> points, IReadOnlyList<EChartsCurve.Segment> segments, double x)
    {
        if (segments.Count == 0) return points[0].Y;
        var lo = 0; var hi = segments.Count;
        while (lo < hi) { var mid = (lo + hi) >> 1; if (segments[mid].End.X < x) lo = mid + 1; else hi = mid; }
        if (lo == segments.Count) return segments[^1].End.Y;
        var segment = segments[lo];
        return SegmentValue(segment, x);
    }

    private static double SegmentValue(EChartsCurve.Segment segment, double x)
    {
        if (x <= segment.Start.X) return segment.Start.Y;
        if (x >= segment.End.X) return segment.End.Y;
        if (segment.Start.Y == segment.End.Y) return segment.Start.Y;
        // Time is not the Bezier parameter when ECharts bends the x controls.
        var lower = 0d; var upper = 1d;
        for (var i = 0; i < 36; i++)
        {
            var middle = (lower + upper) / 2;
            if (Cubic(segment.Start.X, segment.First.X, segment.Second.X, segment.End.X, middle) < x) lower = middle;
            else upper = middle;
        }
        return Cubic(segment.Start.Y, segment.First.Y, segment.Second.Y, segment.End.Y, (lower + upper) / 2);
    }

    private static double Cubic(double a, double b, double c, double d, double t)
    { var u = 1 - t; return u * u * u * a + 3 * u * u * t * b + 3 * u * t * t * c + t * t * t * d; }
}
