using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LanDeviceMonitor.Services;

internal static class SmoothScrollBehavior
{
    private const double DeviceListWheelDistance = 30d;
    private const double MaximumPendingDistance = 60d;
    private const double AnimationDurationMilliseconds = 150d;
    private static readonly ConditionalWeakTable<ScrollViewer, DeviceListScrollController>
        DeviceListControllers = new();

    public static bool TryHandle(DependencyObject source, MouseWheelEventArgs e)
    {
        if (e.Delta == 0 ||
            FindVisualParent<System.Windows.Controls.ListBox>(source) is null)
        {
            return false;
        }

        var viewer = FindVisualParent<ScrollViewer>(source);
        if (viewer is null || viewer.ScrollableHeight <= 0)
        {
            return false;
        }

        var controller = DeviceListControllers.GetValue(
            viewer,
            target => new DeviceListScrollController(target));
        return controller.ScrollBy(GetDeviceListWheelDelta(e.Delta));
    }

    internal static double GetDeviceListWheelDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, DeviceListWheelDistance);

    internal static double EaseOutCubic(double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        return 1d - Math.Pow(1d - clamped, 3d);
    }

    internal static double GetBoundedTargetOffset(
        double currentOffset,
        double previousTargetOffset,
        double wheelDelta,
        double scrollableHeight)
    {
        var remaining = previousTargetOffset - currentOffset;
        if (Math.Sign(remaining) != Math.Sign(wheelDelta))
        {
            remaining = 0d;
        }

        var boundedDistance = Math.Clamp(
            remaining + wheelDelta,
            -MaximumPendingDistance,
            MaximumPendingDistance);
        return Math.Clamp(
            currentOffset + boundedDistance,
            0d,
            scrollableHeight);
    }

    private static double GetScaledDelta(int wheelDelta, double distance)
    {
        if (wheelDelta == 0)
        {
            return 0d;
        }

        var magnitude = Math.Clamp(Math.Abs(wheelDelta) / 120d, 0.05d, 1d);
        return -Math.Sign(wheelDelta) * distance * magnitude;
    }

    private static T? FindVisualParent<T>(DependencyObject? current)
        where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class DeviceListScrollController
    {
        private readonly ScrollViewer _viewer;
        private readonly Stopwatch _animationStopwatch = new();
        private double _animationStartOffset;
        private double _animationTargetOffset;
        private bool _isAnimating;

        public DeviceListScrollController(ScrollViewer viewer)
        {
            _viewer = viewer;
        }

        public bool ScrollBy(double wheelDelta)
        {
            var current = _viewer.VerticalOffset;
            var target = GetBoundedTargetOffset(
                current,
                _isAnimating ? _animationTargetOffset : current,
                wheelDelta,
                _viewer.ScrollableHeight);
            if (Math.Abs(target - current) < 0.1d)
            {
                return false;
            }

            if (!SystemParameters.ClientAreaAnimation)
            {
                StopAnimation();
                _viewer.ScrollToVerticalOffset(target);
                return true;
            }

            _animationStartOffset = current;
            _animationTargetOffset = target;
            _animationStopwatch.Restart();
            if (!_isAnimating)
            {
                _isAnimating = true;
                CompositionTarget.Rendering += CompositionTarget_Rendering;
            }

            return true;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_viewer.IsVisible)
            {
                StopAnimation();
                return;
            }

            var progress =
                _animationStopwatch.Elapsed.TotalMilliseconds /
                AnimationDurationMilliseconds;
            var eased = EaseOutCubic(progress);
            _viewer.ScrollToVerticalOffset(
                _animationStartOffset +
                ((_animationTargetOffset - _animationStartOffset) * eased));

            if (progress >= 1d)
            {
                _viewer.ScrollToVerticalOffset(_animationTargetOffset);
                StopAnimation();
            }
        }

        private void StopAnimation()
        {
            if (!_isAnimating)
            {
                return;
            }

            _isAnimating = false;
            _animationStopwatch.Stop();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
        }
    }
}
