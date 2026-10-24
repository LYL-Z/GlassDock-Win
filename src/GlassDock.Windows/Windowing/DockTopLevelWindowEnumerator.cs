using System.ComponentModel;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Windowing;

/// <summary>Read-only Win32 enumeration used to confirm the WPF HWND before a window capture.</summary>
public static class DockTopLevelWindowEnumerator
{
    public static IReadOnlyList<DockWindowObservation> Enumerate()
    {
        var observations = new List<DockWindowObservation>();
        Exception? callbackFailure = null;
        EnumWindowsCallback callback = (handle, state) =>
        {
            try
            {
                GetWindowThreadProcessId(handle, out var processId);
                var hasBounds = GetWindowRect(handle, out var rectangle);
                observations.Add(new DockWindowObservation(
                    handle,
                    processId,
                    IsWindow(handle),
                    IsWindowVisible(handle),
                    hasBounds
                        ? new DockWindowBounds(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom)
                        : default));
                return true;
            }
            catch (Exception exception)
            {
                callbackFailure = exception;
                return false;
            }
        };

        var succeeded = EnumWindows(callback, IntPtr.Zero);
        GC.KeepAlive(callback);
        if (callbackFailure is not null)
            throw new InvalidOperationException("Win32 window enumeration callback failed.", callbackFailure);
        if (!succeeded)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Win32 EnumWindows failed while locating the run-owned Dock HWND.");

        return observations.AsReadOnly();
    }

    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr state);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(IntPtr handle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr handle);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr handle, out NativeRect rectangle);
}
