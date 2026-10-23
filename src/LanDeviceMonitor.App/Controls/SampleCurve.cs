using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

internal static class SampleCurve
{
    internal readonly record struct Bounds(double Minimum, double Maximum);

    internal static Bounds? VisibleBounds(IReadOnlyList<Point> points, IReadOnlyList<EChartsCurve.Segment> segments,
        double left, double right)
    {
        if (points.Count == 0 || right < points[0].X || left > points[^1].X) return null;
        left = Math.Max(left, points[0].X); right = Math.Min(right, points[^1].X);
        var a = ValueAt(points, segments, left); var b = ValueAt(points, segments, right);
        var min = Math.Min(a, b); var max = Math.Max(a, b);
        // Bound the same rendered controls, including the exact clipped ends.
        foreach (var segment in segments)
            if (segment.End.X >= left && segment.End.X <= right)
            { min = Math.Min(min, segment.End.Y); max = Math.Max(max, segment.End.Y); }
        return new(min, max);
    }

    internal static double ValueAt(IReadOnlyList<Point> points, IReadOnlyList<EChartsCurve.Segment> segments, double x)
    {
        if (segments.Count == 0) return points[0].Y;
        var lo = 0; var hi = segments.Count;
        while (lo < hi) { var mid = (lo + hi) >> 1; if (segments[mid].End.X < x) lo = mid + 1; else hi = mid; }
        if (lo == segments.Count) return segments[^1].End.Y;
        var segment = segments[lo];
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
