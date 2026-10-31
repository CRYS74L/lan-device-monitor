using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using System.Windows.Threading;
using LanDeviceMonitor;
using LanDeviceMonitor.Services;

internal static class BackgroundStartupRegression
{
    internal static int Run()
    {
        Exception? error = null;
        var failures = new List<string>();
        var thread = new Thread(() =>
        {
            App? app = null;
            var directory = Path.Combine(Environment.CurrentDirectory, "runtime-data", "background-startup", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                app = TestAppFactory.Create();
                app.InitializeComponent();
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                foreach (var maximized in new[] { false, true })
                    VerifyWindow(app, directory, maximized, failures);
            }
            catch (Exception exception) { error = exception; }
            finally
            {
                app?.Shutdown();
                Directory.Delete(directory, recursive: true);
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        if (!thread.Join(TimeSpan.FromSeconds(30)))
        {
            Console.Error.WriteLine("Background startup regression timed out.");
            return 1;
        }
        if (error is not null) { Console.Error.WriteLine(error); return 1; }
        if (failures.Count > 0)
        {
            Console.Error.WriteLine("Background startup failures: " + string.Join("; ", failures));
            return 1;
        }
        Console.WriteLine("Background startup PASS: hidden HWND, first content, saved normal/maximized bounds, native minimize, tray hide and reopen.");
        return 0;
    }

    private static void VerifyWindow(App app, string directory, bool maximized, List<string> failures)
    {
        // Use a synthetic placement and no InitializeAsync: this test must not
        // read the user's devices/credentials or connect to their router.
        var workArea = Screen.PrimaryScreen!.WorkingArea;
        var saved = new SavedWindowPlacement(workArea.Left + 60, workArea.Top + 60,
            workArea.Left + 1060, workArea.Top + 960, maximized);
        var path = Path.Combine(directory, maximized ? "maximized.json" : "normal.json");
        File.WriteAllText(path, JsonSerializer.Serialize(saved));
        var window = new MainWindow(new NotificationService());
        app.MainWindow = window;
        using var placement = new WindowPlacementService(window, path);
        var showedBeforeWpf = false;
        var restored = false;
        window.SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source.AddHook((nint hwnd, int message, nint word, nint value, ref bool handled) =>
            {
                if (message == 0x0018 && word != 0 && !window.IsVisible)
                    showedBeforeWpf = true;
                return 0;
            });
            restored = placement.Restore();
            MicaWindowService.Apply(window);
        };
        try
        {
            // NativeTrayIcon creates this handle before App decides whether to
            // call Show(). Reproduce that exact ordering, including restoration.
            var handle = new WindowInteropHelper(window).EnsureHandle();
            Pump(200);
            Check(restored, "saved placement restored", maximized, failures);
            Check(!IsWindowVisible(handle) && !window.IsVisible && !showedBeforeWpf,
                $"stays hidden (native={IsWindowVisible(handle)}, WPF={window.IsVisible}, earlyShow={showedBeforeWpf})", maximized, failures);
            Check(HwndSource.FromHwnd(handle).RootVisual is null,
                "hidden window does not attach an unrequested visual tree", maximized, failures);
            placement.SaveNow();
            Check(JsonSerializer.Deserialize<SavedWindowPlacement>(File.ReadAllText(path)) == saved,
                "background exit preserves saved placement", maximized, failures);

            window.Show();
            window.RestoreFromTray();
            Pump(250);
            window.UpdateLayout();
            Check(IsWindowVisible(handle) && window.IsVisible && window.IsLoaded &&
                window.WindowRoot.IsVisible && window.WindowRoot.ActualWidth > 0 &&
                HwndSource.FromHwnd(handle).RootVisual is not null,
                "explicit open renders content", maximized, failures);
            Check(window.WindowState == (maximized ? WindowState.Maximized : WindowState.Normal),
                $"saved state preserved ({window.WindowState})", maximized, failures);
            var bounds = new NativeWindowPlacement { Length = Marshal.SizeOf<NativeWindowPlacement>() };
            if (!GetWindowPlacement(handle, ref bounds)) throw new InvalidOperationException("Cannot read test placement.");
            Check(bounds.NormalPosition.Left == saved.Left && bounds.NormalPosition.Top == saved.Top &&
                bounds.NormalPosition.Right == saved.Right && bounds.NormalPosition.Bottom == saved.Bottom,
                $"saved restore bounds preserved ({bounds.NormalPosition.Left},{bounds.NormalPosition.Top},{bounds.NormalPosition.Right},{bounds.NormalPosition.Bottom})", maximized, failures);

            window.RequestNativeMinimize();
            PumpUntil(() => window.WindowState == WindowState.Minimized);
            Check(window.WindowState == WindowState.Minimized, "taskbar/native minimize completes", maximized, failures);
            window.RestoreFromTray();
            Pump(200);
            Check(window.IsVisible && IsWindowVisible(handle) && window.WindowState != WindowState.Minimized,
                "minimized window reopens", maximized, failures);
            window.MinimizeToTrayRequested += (_, _) => window.Dispatcher.BeginInvoke(
                new Action(window.HideToTrayPreservingVisual), DispatcherPriority.Normal);
            window.Close();
            Pump(200);
            Check(!IsWindowVisible(handle), "close hides to tray", maximized, failures);
            window.RestoreFromTray();
            Pump(200);
            Check(IsWindowVisible(handle) && window.WindowRoot.IsVisible,
                "tray reopen keeps content", maximized, failures);
        }
        finally { window.PrepareForExit(); window.Close(); }
    }

    private static void Check(bool condition, string name, bool maximized, List<string> failures)
    {
        var message = $"{name}, maximized={maximized}";
        Console.WriteLine($"Background startup {(condition ? "PASS" : "FAIL")}: {message}");
        if (!condition) failures.Add(message);
    }

    private static void PumpUntil(Func<bool> condition)
    {
        var clock = Stopwatch.StartNew();
        while (!condition() && clock.Elapsed < TimeSpan.FromSeconds(3)) Pump(20);
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
    }

    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowPlacement(nint handle, ref NativeWindowPlacement placement);
    [StructLayout(LayoutKind.Sequential)] private struct NativeWindowPlacement
    {
        public int Length, Flags, ShowCommand;
        public System.Drawing.Point MinimumPosition, MaximumPosition;
        public NativeRect NormalPosition;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
}
