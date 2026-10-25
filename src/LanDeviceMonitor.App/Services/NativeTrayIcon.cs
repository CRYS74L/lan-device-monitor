using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Services;

// ReTray's NativeRuntime split: Shell callbacks run on the native STA, and
// only posted application messages cross into the WPF window's dispatcher.
internal sealed class NativeTrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x0400 + 1024;
    internal const int ContextMenuEvent = 0x007B;
    internal const int SelectEvent = 0x0400;
    internal const int KeySelectEvent = 0x0401;
    private const int HostOpenMessage = 0x8000 + 18;
    private const int HostMenuMessage = 0x8000 + 19;
    private const string Library = "LanDeviceMonitor.TrayRuntime.dll";
    private readonly HwndSource _host;
    private readonly Thread _thread;
    private readonly Action _open;
    private readonly Action<Point> _menu;
    private Exception? _failure;
    private bool _disposed;
    private bool _startupFinished;
    private bool _openQueued;
    private bool _secondRelease;
    internal nint Handle => _disposed ? 0 : LanTrayHandle();
    internal bool IsAdded => !_disposed && _thread.IsAlive && Handle != 0;

    internal NativeTrayIcon(Icon icon, string tip, Action open, Action<Point> menu)
    {
        _open = open; _menu = menu;
        var owner = System.Windows.Application.Current.MainWindow ?? throw new InvalidOperationException("The tray requires its WPF owner window.");
        _host = HwndSource.FromHwnd(new WindowInteropHelper(owner).EnsureHandle());
        _host.AddHook(WindowProc);
        var hostHandle = _host.Handle;
        using var ready = new EventWaitHandle(false, EventResetMode.ManualReset);
        var readyHandle = ready.SafeWaitHandle.DangerousGetHandle();
        _thread = new Thread(() => {
            try {
                var error = LanTrayRun(icon.Handle,tip,hostHandle,readyHandle);
                if (error != 0) _failure = new Win32Exception((int)error,"The native tray message loop failed.");
            } catch (Exception error) { _failure = error; ready.Set(); }
            if (_startupFinished && !_disposed)
                _host.Dispatcher.BeginInvoke(new Action(() => throw _failure ?? new InvalidOperationException("The native tray message loop stopped unexpectedly.")));
        }) { IsBackground = true, Name = "LanDeviceMonitor.NativeTrayMessageLoop" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        ready.WaitOne();
        if (Handle == 0) {
            _thread.Join(); _host.RemoveHook(WindowProc);
            throw _failure ?? new InvalidOperationException("Native tray registration failed.");
        }
        _startupFinished = true;
    }

    private nint WindowProc(nint hwnd, int message, nint word, nint value, ref bool handled)
    {
        if (_disposed) return 0;
        if (message == HostMenuMessage) {
            handled = true;
            var point = new Point(word.ToInt32(),value.ToInt32());
            _host.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) _menu(point); }));
        }
        else if (message == HostOpenMessage) {
            handled = true;
            var kind = word.ToInt32();
            if (kind == 0x0203) _secondRelease = true;
            else if (kind == 0x0202 && _secondRelease) _secondRelease = false;
            else if (!_openQueued) {
                _openQueued = true;
                _host.Dispatcher.BeginInvoke(DispatcherPriority.Background,new Action(() => {
                    _openQueued = false; if (!_disposed) _open();
                }));
            }
        }
        return 0;
    }

    internal void ShowNotification(string title,string message)
    {
        if (!_disposed && !LanTrayNotify(title,message))
            throw new InvalidOperationException("The native tray notification could not be displayed.");
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LanTrayStop(); _thread.Join();
        _host.RemoveHook(WindowProc);
    }

    [DllImport(Library, ExactSpelling=true, CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern uint LanTrayRun(nint icon,string tip,nint host,nint ready);
    [DllImport(Library, ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern nint LanTrayHandle();
    [DllImport(Library, ExactSpelling=true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    private static extern void LanTrayStop();
    [DllImport(Library, ExactSpelling=true, CharSet=CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.AssemblyDirectory)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LanTrayNotify(string title,string body);
}
