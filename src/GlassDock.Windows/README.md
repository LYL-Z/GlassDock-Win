# GlassDock.Windows

Windows Shell, HWND, Win32, and COM adapters. Platform details stay behind narrow interfaces.

The M2 Windows Shell adapter reads `.lnk` files only from the current-user and common Start Menu
Programs known folders, and reads packaged AUMID entries from the Windows AppsFolder Shell
namespace. Classic targets are matched by normalized final path and exact shortcut arguments;
explicit AUMIDs use Core's case-insensitive scheme rules. Results remain a catalog until the user
chooses to pin an item, so rescanning never rewrites Dock state or automatically pins applications.

Icons are extracted at runtime with `IShellItemImageFactory` and returned as bounded, alpha-aware
PNG bytes. Extracted third-party artwork stays local and is not redistributed. Command dispatch
distinguishes launch, activate, and open. Foreground activation calls `SetForegroundWindow` once and
returns `ForegroundActivationDenied` when Windows declines it; it does not use focus-stealing
bypasses. Actual launch and activation commands are implemented but have not been invoked in tests.
`Open` handles canonical filesystem paths; Core known-folder IDs remain explicitly unsupported by
this Shell command slice until a Known Folder resolver is added.

The discovery catalog is scoped to Start Menu shortcuts and AppsFolder entries, not a complete
inventory of every installed package. The adapter does not scan Program Files, private folders, or
the registry for applications. It does not modify system settings. Use
`./scripts/probe-windows-shell.ps1` for a read-only local discovery and icon probe; evidence stays in
the ignored `.local/evidence/windows-shell-discovery/` directory.

The implementation follows Microsoft documentation for [Known Folders](https://learn.microsoft.com/windows/win32/shell/known-folders),
[Shell Link properties](https://learn.microsoft.com/windows/win32/shell/appids),
[AppsFolder AUMIDs](https://learn.microsoft.com/en-us/windows/configuration/store/find-aumid),
[Shell icon extraction](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-ishellitemimagefactory-getimage),
[packaged app activation](https://learn.microsoft.com/windows/win32/api/shobjidl_core/nf-shobjidl_core-iapplicationactivationmanager-activateapplication),
and [foreground activation policy](https://learn.microsoft.com/windows/win32/api/winuser/nf-winuser-setforegroundwindow).
