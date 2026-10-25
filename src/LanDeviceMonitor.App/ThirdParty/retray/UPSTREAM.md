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
