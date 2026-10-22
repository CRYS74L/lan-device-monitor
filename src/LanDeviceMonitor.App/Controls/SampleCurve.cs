using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

internal static class SampleCurve
{
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
