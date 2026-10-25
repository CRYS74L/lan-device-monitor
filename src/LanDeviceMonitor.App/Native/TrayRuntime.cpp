// Derived from CRYS74L/ReTray native/agent/main.cpp at
// a57279b6375be9ea463bf9ee3f232487c795250e. GPL-3.0.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <windowsx.h>
#include <shellapi.h>
#include <atomic>
#include <string>

namespace {
constexpr UINT OwnCallback = WM_USER + 1024;
constexpr UINT NotifyMessage = WM_APP + 17;
constexpr UINT HostOpenMessage = WM_APP + 18;
constexpr UINT HostMenuMessage = WM_APP + 19;
std::atomic<HWND> window{};
HWND uiWindow{};
HICON ownImage{};
std::wstring tooltip;
bool ownAdded{};
constexpr wchar_t windowClass[] = L"LanDeviceMonitor.TrayRuntime";

// ReTray SetOwnIcon: the host supplies its own image and tooltip.
bool SetOwnIcon() {
    NOTIFYICONDATAW d{};
    d.cbSize = sizeof(d);
    d.hWnd = window.load();
    d.uID = 1;
    d.uFlags = NIF_ICON | NIF_MESSAGE | NIF_TIP | NIF_SHOWTIP;
    d.uCallbackMessage = OwnCallback;
    d.hIcon = ownImage;
    wcsncpy_s(d.szTip, std::size(d.szTip), tooltip.c_str(), _TRUNCATE);
    const bool adding = !ownAdded;
    bool ok = Shell_NotifyIconW(adding ? NIM_ADD : NIM_MODIFY, &d) != FALSE;
    if (ok) {
        ownAdded = true;
        if (adding) {
            d.uVersion = NOTIFYICON_VERSION_4;
            ok = Shell_NotifyIconW(NIM_SETVERSION, &d) != FALSE;
            d.uFlags = NIF_TIP | NIF_SHOWTIP;
            ok = Shell_NotifyIconW(NIM_MODIFY, &d) != FALSE && ok;
        }
    }
    return ok;
}

// ReTray's embedded TrayMenu / DispatchPendingInterfaceMenu path.
void TrayMenu(const POINT* anchor = nullptr) {
    POINT point{};
    if (anchor) point = *anchor; else GetCursorPos(&point);
    DWORD pid{};
    if (!uiWindow || !GetWindowThreadProcessId(uiWindow, &pid) || pid != GetCurrentProcessId()) return;
    AllowSetForegroundWindow(pid);
    PostMessageW(uiWindow, HostMenuMessage,
        static_cast<WPARAM>(static_cast<INT_PTR>(point.x)), static_cast<LPARAM>(point.y));
}

struct Notification { const wchar_t* title; const wchar_t* body; };
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM wp, LPARAM lp) {
    static UINT taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    if (msg == taskbarCreated) {
        ownAdded = false;
        if (!SetOwnIcon()) PostQuitMessage(ERROR_GEN_FAILURE);
        return 0;
    }
    if (msg == OwnCallback) {
        auto event = LOWORD(lp);
        if (event == WM_CONTEXTMENU) {
            POINT point{GET_X_LPARAM(wp), GET_Y_LPARAM(wp)};
            TrayMenu(point.x == -1 && point.y == -1 ? nullptr : &point);
        } else if (event == NIN_SELECT || event == NIN_KEYSELECT || event == WM_LBUTTONUP || event == WM_LBUTTONDBLCLK) {
            // The host coalesces activation and preserves its single-click behavior.
            PostMessageW(uiWindow, HostOpenMessage, event, 0);
        }
        return 0;
    }
    if (msg == NotifyMessage) {
        auto* info = reinterpret_cast<Notification*>(lp);
        NOTIFYICONDATAW d{};
        d.cbSize = sizeof(d); d.hWnd = hwnd; d.uID = 1; d.uFlags = NIF_INFO;
        wcsncpy_s(d.szInfoTitle, std::size(d.szInfoTitle), info->title, _TRUNCATE);
        wcsncpy_s(d.szInfo, std::size(d.szInfo), info->body, _TRUNCATE);
        return Shell_NotifyIconW(NIM_MODIFY, &d);
    }
    if (msg == WM_CLOSE) {
        EndMenu(); PostQuitMessage(0); return 0;
    }
    return DefWindowProcW(hwnd, msg, wp, lp);
}
void Cleanup() {
    auto hwnd = window.exchange(nullptr);
    if (ownAdded) {
        NOTIFYICONDATAW d{}; d.cbSize=sizeof(d); d.hWnd=hwnd; d.uID=1;
        Shell_NotifyIconW(NIM_DELETE, &d); ownAdded=false;
    }
    if (hwnd) DestroyWindow(hwnd);
}
}
extern "C" __declspec(dllexport) HWND WINAPI LanTrayHandle() { return window.load(); }
extern "C" __declspec(dllexport) void WINAPI LanTrayStop() {
    if (auto hwnd=window.load()) PostMessageW(hwnd,WM_CLOSE,0,0);
}
extern "C" __declspec(dllexport) BOOL WINAPI LanTrayNotify(const wchar_t* title,const wchar_t* body) {
    Notification info{title,body};
    auto hwnd=window.load();
    return hwnd && SendMessageW(hwnd,NotifyMessage,0,reinterpret_cast<LPARAM>(&info));
}
extern "C" __declspec(dllexport) DWORD WINAPI LanTrayRun(HICON image, const wchar_t* tip, HWND host, HANDLE ready) {
    // Match ReTray NativeRuntime: blocking native message loop on its own STA.
    ownImage=image; tooltip=tip; uiWindow=host;
    WNDCLASSW wc{};
    wc.hInstance=GetModuleHandleW(nullptr); wc.lpfnWndProc=WindowProc; wc.lpszClassName=windowClass;
    if (!RegisterClassW(&wc) && GetLastError()!=ERROR_CLASS_ALREADY_EXISTS) { SetEvent(ready); return GetLastError(); }
    window=CreateWindowW(wc.lpszClassName,L"局域网设备助手",WS_OVERLAPPED,0,0,0,0,
        nullptr,nullptr,wc.hInstance,nullptr);
    if (!window || !SetOwnIcon()) { Cleanup(); SetEvent(ready); return ERROR_GEN_FAILURE; }
    SetEvent(ready);
    MSG message{}; BOOL result{};
    while ((result=GetMessageW(&message,nullptr,0,0))>0) {
        TranslateMessage(&message); DispatchMessageW(&message);
    }
    DWORD error=result<0?GetLastError():static_cast<DWORD>(message.wParam);
    Cleanup(); return error;
}
