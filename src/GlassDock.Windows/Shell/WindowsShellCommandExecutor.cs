using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using GlassDock.Core;
using Microsoft.Win32.SafeHandles;

namespace GlassDock.Windows.Shell;

public interface IWindowsShellCommandBackend
{
    DockCommandResult LaunchAppUserModelId(string appUserModelId);
    DockCommandResult LaunchShellLink(string shellLinkPath);
    DockCommandResult LaunchExecutable(string executablePath);
    DockCommandResult OpenPath(string path);
    bool TryFindApplicationWindow(DockTargetIdentity identity, out IntPtr windowHandle);
    bool TrySetForegroundWindow(IntPtr windowHandle);
}

/// <summary>
/// Routes Launch, Activate, and Open as distinct intents. The default backend uses documented
/// Shell/activation APIs and never bypasses SetForegroundWindow restrictions.
/// </summary>
public sealed class WindowsShellCommandExecutor
{
    private readonly IWindowsShellCommandBackend _backend;

    public WindowsShellCommandExecutor(IWindowsShellCommandBackend? backend = null) =>
        _backend = backend ?? new WindowsShellCommandBackend();

    public ValueTask<DockCommandResult> ExecuteAsync(
        DockCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new ValueTask<DockCommandResult>(Task.Run(
            () => Execute(command, cancellationToken), cancellationToken));
    }

    private DockCommandResult Execute(DockCommand command, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return command.Kind switch
            {
                DockCommandKind.Launch => Launch(command.TargetIdentity),
                DockCommandKind.Activate => Activate(command.TargetIdentity),
                DockCommandKind.Open => Open(command.TargetIdentity),
                _ => new DockCommandResult(DockCommandOutcome.Unsupported, "unsupported-command-kind")
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new DockCommandResult(DockCommandOutcome.AccessDenied, "shell-command-access-denied");
        }
        catch (Win32Exception exception)
        {
            var outcome = exception.NativeErrorCode is 5 or 65
                ? DockCommandOutcome.AccessDenied
                : DockCommandOutcome.Failed;
            return new DockCommandResult(outcome, $"shell-command-win32-{exception.NativeErrorCode}");
        }
        catch (COMException exception)
        {
            var error = exception.HResult & 0xFFFF;
            var outcome = error == 5 ? DockCommandOutcome.AccessDenied : DockCommandOutcome.Failed;
            return new DockCommandResult(outcome, $"shell-command-0x{unchecked((uint)exception.HResult):X8}");
        }
    }

    private DockCommandResult Launch(DockTargetIdentity identity) => identity.Scheme switch
    {
        DockIdentityScheme.WindowsAppUserModelId when WindowsShellTargetLocator.IsShellLinkPath(identity) =>
            _backend.LaunchShellLink(identity.Value),
        DockIdentityScheme.WindowsAppUserModelId => _backend.LaunchAppUserModelId(identity.ComparisonKey),
        DockIdentityScheme.WindowsShellLinkPath => _backend.LaunchShellLink(identity.Value),
        DockIdentityScheme.WindowsSystemRole when IsFileManagerRole(identity) =>
            _backend.LaunchExecutable(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")),
        _ => new DockCommandResult(DockCommandOutcome.Unsupported, "launch-target-scheme-unsupported")
    };

    private DockCommandResult Activate(DockTargetIdentity identity)
    {
        if (identity.Scheme is not (DockIdentityScheme.WindowsAppUserModelId
            or DockIdentityScheme.WindowsShellLinkPath or DockIdentityScheme.WindowsSystemRole)
            || (identity.Scheme == DockIdentityScheme.WindowsSystemRole && !IsFileManagerRole(identity)))
            return new DockCommandResult(DockCommandOutcome.Unsupported, "activate-target-scheme-unsupported");

        if (identity.Scheme == DockIdentityScheme.WindowsShellLinkPath
            && HasNonEmptyShortcutArguments(identity.ComparisonKey))
        {
            return new DockCommandResult(
                DockCommandOutcome.Unsupported,
                "argument-specific-window-identity-unavailable");
        }

        if (!_backend.TryFindApplicationWindow(identity, out var windowHandle) || windowHandle == IntPtr.Zero)
            return new DockCommandResult(DockCommandOutcome.TargetNotFound, "application-window-not-found");

        return _backend.TrySetForegroundWindow(windowHandle)
            ? new DockCommandResult(DockCommandOutcome.Completed)
            : new DockCommandResult(DockCommandOutcome.ForegroundActivationDenied, "windows-foreground-policy-denied");
    }

    private DockCommandResult Open(DockTargetIdentity identity) => identity.Scheme switch
    {
        DockIdentityScheme.WindowsCanonicalPath =>
            _backend.OpenPath(identity.Value),
        DockIdentityScheme.WindowsKnownFolderId =>
            new DockCommandResult(DockCommandOutcome.Unsupported, "known-folder-open-not-yet-implemented"),
        _ => new DockCommandResult(DockCommandOutcome.Unsupported, "open-target-scheme-unsupported")
    };

    private static bool HasNonEmptyShortcutArguments(string comparisonKey)
    {
        const string marker = "|args64:";
        var markerIndex = comparisonKey.IndexOf(marker, StringComparison.Ordinal);
        if (markerIndex < 0)
            return true;

        try
        {
            var encodedArguments = comparisonKey[(markerIndex + marker.Length)..];
            return Encoding.UTF8.GetString(Convert.FromBase64String(encodedArguments)).Length > 0;
        }
        catch (FormatException)
        {
            return true;
        }
    }

    private static bool IsFileManagerRole(DockTargetIdentity identity) =>
        string.Equals(identity.Value, "windows.file-manager", StringComparison.OrdinalIgnoreCase)
        && string.Equals(identity.ComparisonKey, "windows.file-manager", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Native, non-elevated Windows Shell command backend.</summary>
public sealed class WindowsShellCommandBackend : IWindowsShellCommandBackend
{
    private static readonly Guid ActivationManagerClassId = new("45BA127D-10A8-46EA-8AB7-56EA9078943C");
    private const uint ProcessQueryLimitedInformation = 0x1000;

    public DockCommandResult LaunchAppUserModelId(string appUserModelId)
    {
        var coResult = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitMultithreaded);
        var initialized = coResult >= 0;
        if (!initialized && coResult != NativeMethods.RpcEChangedMode)
            return new DockCommandResult(DockCommandOutcome.Failed, "com-initialization-failed");

        object? managerObject = null;
        try
        {
            var type = Type.GetTypeFromCLSID(ActivationManagerClassId, throwOnError: true)!;
            managerObject = Activator.CreateInstance(type)!;
            var manager = (IApplicationActivationManager)managerObject;
            var result = manager.ActivateApplication(appUserModelId, null, 0, out _);
            return MapHResult(result, "app-activation");
        }
        finally
        {
            if (managerObject is not null && Marshal.IsComObject(managerObject))
                Marshal.ReleaseComObject(managerObject);
            if (initialized)
                NativeMethods.CoUninitialize();
        }
    }

    public DockCommandResult LaunchShellLink(string shellLinkPath) => ExecuteShellVerb(shellLinkPath);

    public DockCommandResult LaunchExecutable(string executablePath) => ExecuteShellVerb(executablePath);

    public DockCommandResult OpenPath(string path) => ExecuteShellVerb(path);

    public bool TryFindApplicationWindow(DockTargetIdentity identity, out IntPtr windowHandle)
    {
        var candidates = new List<IntPtr>();
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window))
                return true;
            if (identity.Scheme == DockIdentityScheme.WindowsSystemRole && !IsFileManagerWindow(window))
                return true;

            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0 || !MatchesProcess(processId, identity))
                return true;

            candidates.Add(window);
            return true;
        }, IntPtr.Zero);

        if (candidates.Count == 0)
        {
            windowHandle = IntPtr.Zero;
            return false;
        }

        var foreground = NativeMethods.GetForegroundWindow();
        windowHandle = candidates.Contains(foreground) ? foreground : candidates[0];
        return true;
    }

    public bool TrySetForegroundWindow(IntPtr windowHandle) =>
        windowHandle != IntPtr.Zero && NativeMethods.SetForegroundWindow(windowHandle);

    private static bool IsFileManagerWindow(IntPtr window)
    {
        var className = new StringBuilder(256);
        var length = NativeMethods.GetClassNameW(window, className, className.Capacity);
        var name = length > 0 ? className.ToString() : string.Empty;
        return string.Equals(name, "CabinetWClass", StringComparison.Ordinal)
            || string.Equals(name, "ExploreWClass", StringComparison.Ordinal);
    }

    private static bool MatchesProcess(uint processId, DockTargetIdentity identity)
    {
        using var process = NativeMethods.OpenProcess(ProcessQueryLimitedInformation, false, processId);
        if (process.IsInvalid)
            return false;

        if (identity.Scheme == DockIdentityScheme.WindowsAppUserModelId)
        {
            var length = 0u;
            var queryResult = NativeMethods.GetApplicationUserModelId(process, ref length, null);
            if (queryResult != NativeMethods.ErrorInsufficientBuffer || length is 0 or > 1024)
                return false;
            var appUserModelId = new StringBuilder((int)length);
            queryResult = NativeMethods.GetApplicationUserModelId(process, ref length, appUserModelId);
            return queryResult == 0
                && string.Equals(appUserModelId.ToString(), identity.ComparisonKey, StringComparison.OrdinalIgnoreCase);
        }

        if (identity.Scheme == DockIdentityScheme.WindowsSystemRole)
        {
            if (!string.Equals(identity.ComparisonKey, "windows.file-manager", StringComparison.OrdinalIgnoreCase))
                return false;
            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return !string.IsNullOrWhiteSpace(windowsDirectory)
                && MatchesExecutablePath(process, Path.Combine(windowsDirectory, "explorer.exe"));
        }

        if (identity.Scheme != DockIdentityScheme.WindowsShellLinkPath)
            return false;

        const string pathMarker = "path64:";
        const string argumentsMarker = "|args64:";
        if (!identity.ComparisonKey.StartsWith(pathMarker, StringComparison.Ordinal))
            return false;
        var argumentsIndex = identity.ComparisonKey.IndexOf(argumentsMarker, StringComparison.Ordinal);
        if (argumentsIndex < pathMarker.Length)
            return false;

        string expectedPath;
        try
        {
            expectedPath = Encoding.UTF8.GetString(Convert.FromBase64String(
                identity.ComparisonKey[pathMarker.Length..argumentsIndex]));
        }
        catch (FormatException)
        {
            return false;
        }

        return MatchesExecutablePath(process, expectedPath);
    }

    private static bool MatchesExecutablePath(SafeProcessHandle process, string expectedPath)
    {
        var pathBuffer = new StringBuilder(32768);
        var pathLength = (uint)pathBuffer.Capacity;
        if (!NativeMethods.QueryFullProcessImageNameW(process, 0, pathBuffer, ref pathLength))
            return false;
        return WindowsStartMenuApplicationDiscovery.FileIdentityResolver.TryGetCanonicalPath(
            pathBuffer.ToString(), out var actualPath, out _)
            && WindowsStartMenuApplicationDiscovery.FileIdentityResolver.TryGetCanonicalPath(
                expectedPath, out var canonicalExpectedPath, out _)
            && StringComparer.OrdinalIgnoreCase.Equals(actualPath, canonicalExpectedPath);
    }

    private static DockCommandResult ExecuteShellVerb(string path)
    {
        var info = new ShellExecuteInfo
        {
            Size = Marshal.SizeOf<ShellExecuteInfo>(),
            Mask = NativeMethods.SeeMaskNoCloseProcess,
            Verb = "open",
            File = path,
            Show = NativeMethods.SwShowNormal
        };

        if (!NativeMethods.ShellExecuteExW(ref info))
        {
            var error = Marshal.GetLastWin32Error();
            return new DockCommandResult(
                error is 5 or 65 ? DockCommandOutcome.AccessDenied : DockCommandOutcome.Failed,
                $"shell-execute-win32-{error}");
        }

        if (info.Process != IntPtr.Zero)
            NativeMethods.CloseHandle(info.Process);
        return new DockCommandResult(DockCommandOutcome.Completed);
    }

    private static DockCommandResult MapHResult(int result, string codePrefix)
    {
        if (result >= 0)
            return new DockCommandResult(DockCommandOutcome.Completed);
        var error = result & 0xFFFF;
        var outcome = error == 5 ? DockCommandOutcome.AccessDenied
            : error is 2 or 3 or 1168 ? DockCommandOutcome.TargetNotFound
            : DockCommandOutcome.Failed;
        return new DockCommandResult(outcome, $"{codePrefix}-0x{unchecked((uint)result):X8}");
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShellExecuteInfo
    {
        public int Size;
        public uint Mask;
        public IntPtr Window;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Verb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? File;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Parameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Directory;
        public int Show;
        public IntPtr Instance;
        public IntPtr IdList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Class;
        public IntPtr ClassKey;
        public uint HotKey;
        public IntPtr IconOrMonitor;
        public IntPtr Process;
    }

    [ComImport]
    [Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IApplicationActivationManager
    {
        [PreserveSig] int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string? arguments,
            uint options,
            out uint processId);
        [PreserveSig] int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string? verb,
            out uint processId);
        [PreserveSig] int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out uint processId);
    }

    private static class NativeMethods
    {
        public const int CoinitMultithreaded = 0;
        public const int RpcEChangedMode = unchecked((int)0x80010106);
        public const int ErrorInsufficientBuffer = 122;
        public const uint SeeMaskNoCloseProcess = 0x00000040;
        public const int SwShowNormal = 1;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShellExecuteExW(ref ShellExecuteInfo info);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr handle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr state);

        public delegate bool EnumWindowsCallback(IntPtr window, IntPtr state);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int GetClassNameW(IntPtr window, [Out] StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr window);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern SafeProcessHandle OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool QueryFullProcessImageNameW(
            SafeProcessHandle process,
            uint flags,
            [Out] StringBuilder imageName,
            ref uint size);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetApplicationUserModelId(
            SafeProcessHandle process,
            ref uint applicationUserModelIdLength,
            [Out] StringBuilder? applicationUserModelId);
    }

    private sealed class SafeProcessHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private SafeProcessHandle() : base(true) { }

        protected override bool ReleaseHandle() => NativeMethods.CloseHandle(handle);
    }
}
