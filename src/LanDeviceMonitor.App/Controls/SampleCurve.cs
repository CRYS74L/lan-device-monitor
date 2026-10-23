using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

internal static class SampleCurve
{
    internal readonly record struct Bounds(double Minimum, double Maximum);

    internal static Bounds? VisibleBounds(IReadOnlyList<Point> points, IReadOnlyList<double> slopes,
        double left, double right)
    {
        if (points.Count == 0 || right < points[0].X || left > points[^1].X) return null;
        left = Math.Max(left, points[0].X); right = Math.Min(right, points[^1].X);
        // Controls are bounded by their two samples, making each segment monotone.
        // Visible extrema are therefore the clipped endpoints and interior knots.
        var a = ValueAt(points, slopes, left); var b = ValueAt(points, slopes, right);
        var min = Math.Min(a, b); var max = Math.Max(a, b);
        for (var i = LowerBound(points, left); i < points.Count && points[i].X <= right; i++)
        { min = Math.Min(min, points[i].Y); max = Math.Max(max, points[i].Y); }
        return new(min, max);
    }

    internal static double ValueAt(IReadOnlyList<Point> points, IReadOnlyList<double> slopes, double x)
    {
        var next = LowerBound(points, x);
        if (next == 0) return points[0].Y;
        if (next == points.Count) return points[^1].Y;
        var a = points[next - 1]; var b = points[next];
        if (a.Y == b.Y) return a.Y;
        var (first, second) = Controls(a, b, slopes[next - 1], slopes[next]);
        var t = (x - a.X) / (b.X - a.X); var u = 1 - t;
        return u * u * u * a.Y + 3 * u * u * t * first.Y + 3 * u * t * t * second.Y + t * t * t * b.Y;
    }

    internal static (Point First, Point Second) Controls(Point start, Point end, double startSlope, double endSlope)
    {
        var third = (end.X - start.X) / 3;
        return (new(start.X + third, Math.Clamp(start.Y + startSlope * third, Math.Min(start.Y, end.Y), Math.Max(start.Y, end.Y))),
            new(end.X - third, Math.Clamp(end.Y - endSlope * third, Math.Min(start.Y, end.Y), Math.Max(start.Y, end.Y))));
    }

    private static int LowerBound(IReadOnlyList<Point> points, double x)
    {
        var left = 0; var right = points.Count;
        while (left < right)
        { var middle = (left + right) / 2; if (points[middle].X < x) left = middle + 1; else right = middle; }
        return left;
    }

    internal static double[] Slopes(IReadOnlyList<Point> points)
    {
        var n = points.Count;
        var slopes = new double[n];
        if (n < 2) return slopes;
        var delta = new double[n - 1];
        for (var i = 0; i < n - 1; i++)
            delta[i] = (points[i + 1].Y - points[i].Y) / (points[i + 1].X - points[i].X);
        slopes[0] = delta[0];
        slopes[^1] = delta[^1];
        for (var i = 1; i < n - 1; i++)
        {
            if (delta[i - 1] * delta[i] <= 0) continue;
            var before = points[i].X - points[i - 1].X;
            var after = points[i + 1].X - points[i].X;
            var w1 = 2 * after + before;
            var w2 = after + 2 * before;
            slopes[i] = (w1 + w2) / (w1 / delta[i - 1] + w2 / delta[i]);
        }
        return slopes;
    }
}
