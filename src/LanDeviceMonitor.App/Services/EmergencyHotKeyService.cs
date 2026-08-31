using System.Runtime.InteropServices;
using LanDeviceMonitor.Models;

namespace LanDeviceMonitor.Services;

internal enum EmergencyHotKeyRegistrationStatus
{
    NotStarted,
    Registered,
    Conflict,
    Failed,
    Disabled,
    Stopped
}

internal sealed class EmergencyHotKeyService : IDisposable
{
    internal static string DisplayShortcut => EmergencyHotKeyGesture.Default.DisplayText;
    internal const uint VirtualKey = EmergencyHotKeyGesture.DefaultVirtualKey;
    internal const uint NoRepeatModifier = EmergencyHotKeyGesture.NoRepeatModifier;
    private const uint HotKeyMessage = 0x0312;
    private const uint QuitMessage = 0x0012;
    private const uint PeekMessageNoRemove = 0x0000;
    private const int HotKeyId = 0x4C44;
    private const int HotKeyAlreadyRegisteredError = 1409;

    internal const uint RequiredModifiers =
        EmergencyHotKeyGesture.DefaultModifiers;

    internal const uint Modifiers = RequiredModifiers | NoRepeatModifier;

    internal static bool HasAllFourModifiers =>
        (Modifiers & RequiredModifiers) == RequiredModifiers;

    private readonly Action _onTriggered;
    private readonly EmergencyHotKeyGesture _gesture;
    private readonly object _syncRoot = new();
    private Thread? _messageThread;
    private ManualResetEventSlim? _startupCompleted;
    private uint _nativeThreadId;
    private int _status = (int)EmergencyHotKeyRegistrationStatus.NotStarted;
    private int _disposed;
    private int _triggered;
    private bool _registered;

    public EmergencyHotKeyService(
        Action onTriggered,
        EmergencyHotKeyGesture? gesture = null)
    {
        _onTriggered = onTriggered ?? throw new ArgumentNullException(nameof(onTriggered));
        _gesture = gesture ?? EmergencyHotKeyGesture.Default;
    }

    internal EmergencyHotKeyRegistrationStatus Status =>
        (EmergencyHotKeyRegistrationStatus)Volatile.Read(ref _status);

    internal string StatusText => GetStatusText(Status, _gesture);

    internal bool IsRegistered =>
        Status == EmergencyHotKeyRegistrationStatus.Registered;

    internal static string GetStatusText(EmergencyHotKeyRegistrationStatus status) =>
        GetStatusText(status, EmergencyHotKeyGesture.Default);

    internal static string GetStatusText(
        EmergencyHotKeyRegistrationStatus status,
        EmergencyHotKeyGesture gesture) =>
        status switch
        {
            EmergencyHotKeyRegistrationStatus.Registered =>
                $"结束进程快捷键已注册：{gesture.DisplayText}",
            EmergencyHotKeyRegistrationStatus.Conflict =>
                "结束进程快捷键冲突，未注册",
            EmergencyHotKeyRegistrationStatus.Failed =>
                "结束进程快捷键注册失败",
            EmergencyHotKeyRegistrationStatus.Disabled =>
                "结束进程快捷键未启用",
            EmergencyHotKeyRegistrationStatus.Stopped =>
                "结束进程快捷键已停止",
            _ => "结束进程快捷键未启动"
        };

    internal bool Start()
    {
        lock (_syncRoot)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return false;
            }

            if (_messageThread is not null)
            {
                return IsRegistered;
            }

            _startupCompleted = new ManualResetEventSlim(false);
            _messageThread = new Thread(MessageLoop)
            {
                IsBackground = true,
                Name = "LanDeviceMonitor.EmergencyHotKey"
            };
            _messageThread.Start();
        }

        if (!_startupCompleted.Wait(TimeSpan.FromSeconds(2)))
        {
            SetStatus(EmergencyHotKeyRegistrationStatus.Failed);
            return false;
        }

        return IsRegistered;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        var thread = _messageThread;
        if (thread is null)
        {
            return;
        }

        if (_nativeThreadId != 0)
        {
            _ = PostThreadMessage(
                _nativeThreadId,
                QuitMessage,
                UIntPtr.Zero,
                IntPtr.Zero);
        }

        if (thread != Thread.CurrentThread && thread.IsAlive)
        {
            _ = thread.Join(TimeSpan.FromMilliseconds(500));
        }
    }

    private void MessageLoop()
    {
        _nativeThreadId = GetCurrentThreadId();
        _ = PeekMessage(
            out _,
            IntPtr.Zero,
            0,
            0,
            PeekMessageNoRemove);

        var startupCompleted = _startupCompleted;
        try
        {
            _registered = RegisterHotKey(
                IntPtr.Zero,
                HotKeyId,
                _gesture.RegistrationModifiers,
                _gesture.VirtualKey);
            if (!_registered)
            {
                var errorCode = Marshal.GetLastWin32Error();
                SetStatus(
                    errorCode == HotKeyAlreadyRegisteredError
                        ? EmergencyHotKeyRegistrationStatus.Conflict
                        : EmergencyHotKeyRegistrationStatus.Failed);
                return;
            }

            SetStatus(EmergencyHotKeyRegistrationStatus.Registered);
        }
        catch
        {
            SetStatus(EmergencyHotKeyRegistrationStatus.Failed);
            return;
        }
        finally
        {
            startupCompleted?.Set();
        }

        try
        {
            while (GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
            {
                if (message.Message != HotKeyMessage ||
                    message.WParam.ToUInt64() != HotKeyId ||
                    Interlocked.Exchange(ref _triggered, 1) != 0)
                {
                    continue;
                }

                try
                {
                    _onTriggered();
                }
                catch
                {
                    // The emergency callback is responsible for ending the
                    // process. Do not let an exception kill only this listener.
                }

                break;
            }
        }
        finally
        {
            if (_registered)
            {
                _ = UnregisterHotKey(IntPtr.Zero, HotKeyId);
                _registered = false;
            }

            if (Status == EmergencyHotKeyRegistrationStatus.Registered)
            {
                SetStatus(EmergencyHotKeyRegistrationStatus.Stopped);
            }
        }
    }

    private void SetStatus(EmergencyHotKeyRegistrationStatus status) =>
        Volatile.Write(ref _status, (int)status);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd,
        int id,
        uint fsModifiers,
        uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(
        out NativeMessage message,
        IntPtr hWnd,
        uint minFilter,
        uint maxFilter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PeekMessage(
        out NativeMessage message,
        IntPtr hWnd,
        uint minFilter,
        uint maxFilter,
        uint removeMessage);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(
        uint threadId,
        uint message,
        UIntPtr wParam,
        IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeMessage
    {
        public IntPtr HWnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }
}
