using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GlassDock.PoC.WinUI;

internal sealed class NativeTrayIcon : IDisposable
{
    private const uint NimAdd = 0x00000000;
    private const uint NimDelete = 0x00000002;
    private const uint NifMessage = 0x00000001;
    private const uint NifIcon = 0x00000002;
    private const uint NifTip = 0x00000004;
    private const uint WmAppTray = 0x8000 + 41;
    private const uint WmAppM0TestHide = 0x8000 + 42;
    private const uint WmContextMenu = 0x007B;
    private const uint WmRButtonUp = 0x0205;
    private const uint WmLButtonDblClk = 0x0203;
    private const uint WmNull = 0x0000;
    private const uint MfString = 0x00000000;
    private const uint MfSeparator = 0x00000800;
    private const uint TpmRightButton = 0x0002;
    private const uint TpmReturnCommand = 0x0100;
    private const uint ExitCommand = 2;
    private const uint ShowCommand = 1;

    private readonly nint _windowHandle;
    private readonly Action _showDock;
    private readonly Action _exit;
    private readonly SubclassProcedure _subclassProcedure;
    private readonly nint _iconHandle;
    private bool _disposed;

    public NativeTrayIcon(nint windowHandle, Action showDock, Action exit)
    {
        _windowHandle = windowHandle;
        _showDock = showDock;
        _exit = exit;
        _subclassProcedure = WindowProcedure;
        _iconHandle = LoadIcon(nint.Zero, new nint(32512));

        if (!SetWindowSubclass(_windowHandle, _subclassProcedure, 41, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not register the Dock tray callback.");

        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmAppTray,
            IconHandle = _iconHandle,
            Tip = "GlassDock-Win M0 WinUI spike",
            Info = string.Empty,
            InfoTitle = string.Empty
        };

        if (!Shell_NotifyIcon(NimAdd, ref data))
        {
            RemoveWindowSubclass(_windowHandle, _subclassProcedure, 41);
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not add the Dock tray icon.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        var data = new NotifyIconData
        {
            Size = (uint)Marshal.SizeOf<NotifyIconData>(),
            WindowHandle = _windowHandle,
            Id = 1,
            Flags = NifMessage | NifIcon | NifTip,
            CallbackMessage = WmAppTray,
            IconHandle = _iconHandle,
            Tip = "GlassDock-Win M0 WinUI spike",
            Info = string.Empty,
            InfoTitle = string.Empty
        };
        _ = Shell_NotifyIcon(NimDelete, ref data);
        _ = RemoveWindowSubclass(_windowHandle, _subclassProcedure, 41);
    }

    private nint WindowProcedure(nint windowHandle, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData)
    {
        if (message == WmAppM0TestHide &&
            string.Equals(Environment.GetEnvironmentVariable("GLASSDOCK_M0_TEST_ALLOW_HIDE"), "1", StringComparison.Ordinal))
        {
            _ = ShowWindow(_windowHandle, 0);
            return 0;
        }

        if (message == WmAppTray)
        {
            var eventMessage = unchecked((uint)lParam.ToInt64());
            if (eventMessage is WmRButtonUp or WmContextMenu)
            {
                ShowContextMenu();
                return 0;
            }

            if (eventMessage == WmLButtonDblClk)
            {
                _showDock();
                return 0;
            }
        }

        return DefSubclassProc(windowHandle, message, wParam, lParam);
    }

    private void ShowContextMenu()
    {
        var menu = CreatePopupMenu();
        if (menu == nint.Zero)
            return;

        try
        {
            _ = AppendMenu(menu, MfString, ShowCommand, "Show Dock");
            _ = AppendMenu(menu, MfSeparator, 0, null);
            _ = AppendMenu(menu, MfString, ExitCommand, "Exit GlassDock");
            _ = GetCursorPos(out var cursor);
            _ = SetForegroundWindow(_windowHandle);
            var command = TrackPopupMenu(menu, TpmRightButton | TpmReturnCommand,
                cursor.X, cursor.Y, 0, _windowHandle, nint.Zero);
            _ = PostMessage(_windowHandle, WmNull, nint.Zero, nint.Zero);

            if (command == ShowCommand)
                _showDock();
            else if (command == ExitCommand)
                _exit();
        }
        finally
        {
            _ = DestroyMenu(menu);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NotifyIconData
    {
        public uint Size;
        public nint WindowHandle;
        public uint Id;
        public uint Flags;
        public uint CallbackMessage;
        public nint IconHandle;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string Tip;
        public uint State;
        public uint StateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Info;
        public uint TimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string InfoTitle;
        public uint InfoFlags;
        public Guid ItemGuid;
        public nint BalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint SubclassProcedure(nint windowHandle, uint message, nuint wParam, nint lParam,
        nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool SetWindowSubclass(nint windowHandle, SubclassProcedure procedure,
        nuint subclassId, nuint referenceData);

    [DllImport("comctl32.dll", SetLastError = true)]
    private static extern bool RemoveWindowSubclass(nint windowHandle, SubclassProcedure procedure, nuint subclassId);

    [DllImport("comctl32.dll")]
    private static extern nint DefSubclassProc(nint windowHandle, uint message, nuint wParam, nint lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool Shell_NotifyIcon(uint message, ref NotifyIconData data);

    [DllImport("user32.dll")]
    private static extern nint LoadIcon(nint instance, nint iconName);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool AppendMenu(nint menu, uint flags, uint idNewItem, string? newItem);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint TrackPopupMenu(nint menu, uint flags, int x, int y, int reserved,
        nint windowHandle, nint rectangle);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out Point point);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint windowHandle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostMessage(nint windowHandle, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(nint windowHandle, int command);

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint menu);
}
