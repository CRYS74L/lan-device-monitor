// Port of ReTray native/agent/main.cpp SetOwnIcon and OwnCallback
// at a57279b6375be9ea463bf9ee3f232487c795250e (GPL-3.0).
using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace LanDeviceMonitor.Services;

internal sealed class NativeTrayIcon : IDisposable
{
    internal const int CallbackMessage = 0x0400 + 1024;
    internal const int ContextMenuEvent = 0x007B;
    internal const int SelectEvent = 0x0400;
    internal const int KeySelectEvent = 0x0401;
    private readonly HwndSource _source;
    private readonly Icon _icon;
    private readonly string _tip;
    private readonly Action _open;
    private readonly Action<Point> _menu;
    private readonly uint _taskbarCreated;
    private bool _added;
    private bool _disposed;
    private bool _openQueued;
    private bool _secondRelease;
    internal nint Handle => _source.Handle;
    internal bool IsAdded => _added;

    internal NativeTrayIcon(Icon icon, string tip, Action open, Action<Point> menu)
    {
        _icon = icon; _tip = tip; _open = open; _menu = menu;
        _taskbarCreated = RegisterWindowMessageW("TaskbarCreated");
        _source = new HwndSource(new HwndSourceParameters("LanDeviceMonitor.TrayCallback") {
            WindowStyle = unchecked((int)0x80000000), ExtendedWindowStyle = 0x08000080,
            Width = 1, Height = 1
        });
        _source.AddHook(WindowProc);
        try { Register(); }
        catch { _source.Dispose(); throw; }
    }

    private void Register()
    {
        var data = Data();
        data.Flags = 0x1 | 0x2 | 0x4 | 0x80; // MESSAGE, ICON, TIP, SHOWTIP.
        data.Callback = CallbackMessage; data.Icon = _icon.Handle; data.Tip = _tip;
        Check(Shell_NotifyIconW(0, ref data), "NIM_ADD");
        _added = true;
        try {
            data.Version = 4;
            Check(Shell_NotifyIconW(4, ref data), "NIM_SETVERSION");
            // ReTray reapplies the standard tooltip after the V4 transition.
            data.Flags = 0x4 | 0x80;
            Check(Shell_NotifyIconW(1, ref data), "NIM_MODIFY tooltip");
        } catch {
            Shell_NotifyIconW(2, ref data); _added = false; throw;
        }
    }

    private nint WindowProc(nint hwnd, int message, nint word, nint value, ref bool handled)
    {
        if (_disposed) return 0;
        if ((uint)message == _taskbarCreated) {
            handled = true; _added = false; Register(); return 0;
        }
        if (message != CallbackMessage) return 0;
        handled = true;
        // V4 packs the event and icon ID in lParam and signed coordinates in wParam.
        if (((long)value >> 16 & 0xffff) != 1) return 0;
        var kind = (int)((long)value & 0xffff);
        if (kind == ContextMenuEvent) {
            var x = (short)((long)word & 0xffff);
            var y = (short)(((long)word >> 16) & 0xffff);
            if (x == -1 && y == -1) { GetCursorPos(out var cursor); x = (short)cursor.X; y = (short)cursor.Y; }
            var point = new Point(x, y);
            AllowSetForegroundWindow((uint)Environment.ProcessId);
            _source.Dispatcher.BeginInvoke(new Action(() => { if (!_disposed) _menu(point); }));
        }
        else if (kind == 0x0203) {
            _secondRelease = true;
        }
        else if (kind == 0x0202 && _secondRelease) {
            _secondRelease = false;
        }
        else if (kind is SelectEvent or KeySelectEvent or 0x0202) {
            QueueOpen();
        }
        return 0;
    }

    private void QueueOpen()
    {
        // V4 can report both the raw release and NIN_SELECT for one activation.
        if (_openQueued) return;
        _openQueued = true;
        _source.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => {
            _openQueued = false; if (!_disposed) _open();
        }));
    }

    internal void ShowNotification(string title, string message)
    {
        if (_disposed) return;
        var data = Data(); data.Flags = 0x10;
        data.Info = message.Length > 255 ? message[..255] : message;
        data.InfoTitle = title.Length > 63 ? title[..63] : title;
        Check(Shell_NotifyIconW(1, ref data), "NIM_MODIFY notification");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_added) { var data = Data(); Shell_NotifyIconW(2, ref data); _added = false; }
        _source.RemoveHook(WindowProc); _source.Dispose();
    }

    private NotifyIconData Data() => new() {
        Size = (uint)Marshal.SizeOf<NotifyIconData>(), Window = Handle, Id = 1,
        Tip = string.Empty, Info = string.Empty, InfoTitle = string.Empty
    };
    private static void Check(bool success, string operation)
    {
        if (!success) throw new Win32Exception(Marshal.GetLastWin32Error(), $"Tray {operation} failed.");
    }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint Window;
        public uint Id, Flags, Callback;
        public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State, StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint Version;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid Guid;
        public nint BalloonIcon;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X,Y; }
    [DllImport("shell32.dll", CharSet=CharSet.Unicode, SetLastError=true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Shell_NotifyIconW(uint message, ref NotifyIconData data);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern uint RegisterWindowMessageW(string value);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorPos(out NativePoint point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool AllowSetForegroundWindow(uint processId);
}
