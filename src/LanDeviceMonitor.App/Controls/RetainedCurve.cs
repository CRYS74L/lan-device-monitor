using Point = System.Windows.Point;

namespace LanDeviceMonitor.Controls;

// Keep the part of a live ECharts segment that has already entered the viewport.
// De Casteljau subdivision preserves that prefix exactly; only the unseen suffix
// receives the new endpoint control. The join keeps the existing tangent.
internal static class RetainedCurve
{
    internal static EChartsCurve.Segment[] Update(IReadOnlyList<EChartsCurve.Segment> previous,
        EChartsCurve.Segment target, double visibleEnd, double shift = 0)
    {
        var old = previous.Select(s => Shift(s, shift)).ToArray();
        if (old.Length == 0 || !SamePoint(old[0].Start, target.Start) || !SamePoint(old[^1].End, target.End) || visibleEnd <= target.Start.X)
            return [target];
        if (visibleEnd >= target.End.X) return old;
        // An unchanged target needs no additional subdivision.
        if (old.Length == 1 && old[0] == target) return old;
        var result = new List<EChartsCurve.Segment>();
        foreach (var segment in old)
        {
            if (segment.End.X <= visibleEnd) { result.Add(segment); continue; }
            if (segment.Start.X >= visibleEnd) break;
            var (left, right) = Split(segment, visibleEnd);
            result.Add(left);
            result.Add(new(right.Start, right.First,
                FitEndTangent(target.Second, right.Start, target.End), target.End));
            return result.ToArray();
        }
        var last = result[^1];
        // The boundary can coincide with a previous subdivision knot.
        var next = old.First(s => s.Start.X >= visibleEnd);
        result.Add(new(last.End, next.First,
            FitEndTangent(target.Second, last.End, target.End), target.End));
        return result.ToArray();
    }

    private static (EChartsCurve.Segment, EChartsCurve.Segment) Split(EChartsCurve.Segment s, double x)
    {
        var lo = 0d; var hi = 1d;
        for (var i = 0; i < 48; i++)
        {
            var t = (lo + hi) / 2;
            var a = Lerp(s.Start, s.First, t); var b = Lerp(s.First, s.Second, t); var c = Lerp(s.Second, s.End, t);
            if (Lerp(Lerp(a, b, t), Lerp(b, c, t), t).X < x) lo = t; else hi = t;
        }
        var u = (lo + hi) / 2;
        var ab = Lerp(s.Start, s.First, u); var bc = Lerp(s.First, s.Second, u); var cd = Lerp(s.Second, s.End, u);
        var abc = Lerp(ab, bc, u); var bcd = Lerp(bc, cd, u); var p = Lerp(abc, bcd, u);
        return (new(s.Start, ab, abc, p), new(p, bcd, cd, s.End));
    }

    private static Point Lerp(Point a, Point b, double t) => new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    private static bool SamePoint(Point a, Point b) => Math.Abs(a.X - b.X) < 1e-9 && Math.Abs(a.Y - b.Y) < 1e-12;
    private static Point FitEndTangent(Point control, Point start, Point end)
    {
        var v = control - end;
        var factor = 1d;
        if (v.X != 0) factor = Math.Min(factor, (v.X < 0 ? Math.Min(start.X, end.X) - end.X : Math.Max(start.X, end.X) - end.X) / v.X);
        if (v.Y != 0) factor = Math.Min(factor, (v.Y < 0 ? Math.Min(start.Y, end.Y) - end.Y : Math.Max(start.Y, end.Y) - end.Y) / v.Y);
        return end + v * Math.Max(0, factor);
    }
    private static EChartsCurve.Segment Shift(EChartsCurve.Segment s, double x) => new(
        new(s.Start.X + x, s.Start.Y), new(s.First.X + x, s.First.Y),
        new(s.Second.X + x, s.Second.Y), new(s.End.X + x, s.End.Y));
}
