namespace LanDeviceMonitor.Controls;

// DrawingContext values use the same bounded cubic ease-out as SegmentedControl.
internal readonly record struct ChartValueMotion(double From, double To, double Started)
{
    internal double Read(double now, double durationSeconds)
    {
        var progress = durationSeconds <= 0 ? 1 : Math.Clamp((now - Started) / durationSeconds, 0, 1);
        return From + (To - From) * (1 - Math.Pow(1 - progress, 3));
    }

    internal ChartValueMotion Retarget(double target, double now, double durationSeconds) =>
        target == To ? this : new(Read(now, durationSeconds), target, now);
}
