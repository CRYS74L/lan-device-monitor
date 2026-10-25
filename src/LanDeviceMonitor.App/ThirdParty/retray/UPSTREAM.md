# ReTray tray menu

Source: https://github.com/CRYS74L/ReTray/tree/a57279b6375be9ea463bf9ee3f232487c795250e

The tray menu uses ReTray's `TrayCommandMenu.cs`, `TrayMenuPlacement`,
`TrayMenuSurfaceTemplate`, `TrayCommandItemStyle`, `TrayDropdownMenuStyle`,
and the menu-item behavior in `StudioControlMotion.cs` from
`ui/ReTray.Wpf/src/LanDeviceMonitor.App/` at this revision.

Integration changes: namespace, application callbacks and automation IDs.
The existing embedded application font and theme palette are retained.
Selector-only additions are excluded from the motion helper.

License: GPL-3.0 (see LICENSE).

`Native/TrayRuntime.cpp` extracts `native/agent/main.cpp` SetOwnIcon,
TrayMenu/DispatchPendingInterfaceMenu, OwnCallback, native window creation and
GetMessage/DispatchMessage loop. `Services/NativeTrayIcon.cs` hosts this loop on
an independent STA thread, as ReTray's `Services/NativeRuntime.cs` does, and
forwards posted menu requests through the main WPF window.

Integration changes: app-specific window/message names, host-provided image and
tooltip, an export boundary for startup/shutdown and notification display, and
host-side activation coalescing. ReTray's unrelated tray-management backend is
excluded.
