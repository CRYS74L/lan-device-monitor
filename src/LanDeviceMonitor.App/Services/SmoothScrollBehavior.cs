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
    private const double DeviceListWheelDistance = 18d;
    private const double DeviceListTailDistance = 2d;
    private const double TailDelayMilliseconds = 45d;
    private const double TailDurationMilliseconds = 90d;
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
        return controller.ScrollBy(
            GetDeviceListWheelDelta(e.Delta),
            GetDeviceListTailDelta(e.Delta));
    }

    internal static double GetDeviceListWheelDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, DeviceListWheelDistance);

    internal static double GetDeviceListTailDelta(int wheelDelta) =>
        GetScaledDelta(wheelDelta, DeviceListTailDistance);

    internal static double EaseOutCubic(double progress)
    {
        var clamped = Math.Clamp(progress, 0d, 1d);
        return 1d - Math.Pow(1d - clamped, 3d);
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
        private readonly DispatcherTimer _tailDelayTimer;
        private readonly Stopwatch _tailStopwatch = new();
        private double _pendingTailDelta;
        private double _tailStartOffset;
        private double _tailTargetOffset;
        private bool _isRenderingTail;

        public DeviceListScrollController(ScrollViewer viewer)
        {
            _viewer = viewer;
            _tailDelayTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(TailDelayMilliseconds),
                DispatcherPriority.Input,
                TailDelayTimer_Tick,
                viewer.Dispatcher);
            _tailDelayTimer.Stop();
        }

        public bool ScrollBy(double wheelDelta, double tailDelta)
        {
            StopTailAnimation();
            _tailDelayTimer.Stop();

            var current = _viewer.VerticalOffset;
            var target = Math.Clamp(
                current + wheelDelta,
                0d,
                _viewer.ScrollableHeight);
            if (Math.Abs(target - current) < 0.1d)
            {
                return false;
            }

            _viewer.ScrollToVerticalOffset(target);
            if (SystemParameters.ClientAreaAnimation)
            {
                _pendingTailDelta = tailDelta;
                _tailDelayTimer.Start();
            }

            return true;
        }

        private void TailDelayTimer_Tick(object? sender, EventArgs e)
        {
            _tailDelayTimer.Stop();
            if (!_viewer.IsVisible)
            {
                return;
            }

            _tailStartOffset = _viewer.VerticalOffset;
            _tailTargetOffset = Math.Clamp(
                _tailStartOffset + _pendingTailDelta,
                0d,
                _viewer.ScrollableHeight);
            if (Math.Abs(_tailTargetOffset - _tailStartOffset) < 0.1d)
            {
                return;
            }

            _tailStopwatch.Restart();
            _isRenderingTail = true;
            CompositionTarget.Rendering += CompositionTarget_Rendering;
        }

        private void CompositionTarget_Rendering(object? sender, EventArgs e)
        {
            if (!_viewer.IsVisible)
            {
                StopTailAnimation();
                return;
            }

            var progress =
                _tailStopwatch.Elapsed.TotalMilliseconds / TailDurationMilliseconds;
            var eased = EaseOutCubic(progress);
            _viewer.ScrollToVerticalOffset(
                _tailStartOffset +
                ((_tailTargetOffset - _tailStartOffset) * eased));

            if (progress >= 1d)
            {
                _viewer.ScrollToVerticalOffset(_tailTargetOffset);
                StopTailAnimation();
            }
        }

        private void StopTailAnimation()
        {
            if (!_isRenderingTail)
            {
                return;
            }

            _isRenderingTail = false;
            _tailStopwatch.Stop();
            CompositionTarget.Rendering -= CompositionTarget_Rendering;
        }
    }
}
