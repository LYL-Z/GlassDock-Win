using System.Runtime.InteropServices;
using System.Text;
using GlassDock.Core;
using Microsoft.Win32.SafeHandles;

namespace GlassDock.Windows.Shell;

public interface IWindowsApplicationDiscovery
{
    ValueTask<WindowsShellCatalogResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Reads only the current-user and common Start Menu Programs known folders. It does not walk
/// Program Files, user documents, the registry, or other parts of the system drive.
/// </summary>
public sealed class WindowsStartMenuApplicationDiscovery : IWindowsApplicationDiscovery
{
    private const int MaximumShortcutCount = 4096;
    private const int MaximumDisplayNameLength = 512;
    private const int MaximumArgumentsLength = 4096;

    public ValueTask<WindowsShellCatalogResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default) =>
        new(Task.Run(() => Discover(cancellationToken), cancellationToken));

    private static WindowsShellCatalogResult Discover(CancellationToken cancellationToken)
    {
        var apartmentResult = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitMultithreaded);
        var initialized = apartmentResult >= 0;
        if (!initialized && apartmentResult != NativeMethods.RpcEChangedMode)
            return WindowsShellCatalogResult.Failed(WindowsShellCatalogStatus.Unsupported, "com-initialization-failed");

        try
        {
            return DiscoverInitialized(cancellationToken);
        }
        finally
        {
            if (initialized)
                NativeMethods.CoUninitialize();
        }
    }

    private static WindowsShellCatalogResult DiscoverInitialized(CancellationToken cancellationToken)
    {
        var issues = new List<WindowsShellDiscoveryIssue>();
        var roots = GetStartMenuRoots(issues);
        if (roots.Count == 0)
        {
            var status = issues.Any(issue => issue.Kind == WindowsShellDiscoveryIssueKind.AccessDenied)
                ? WindowsShellCatalogStatus.AccessDenied
                : WindowsShellCatalogStatus.Failed;
            return WindowsShellCatalogResult.Failed(status, "known-start-menu-folders-unavailable");
        }

        var shortcutPaths = EnumerateShortcuts(roots, issues, cancellationToken);
        var applications = new List<WindowsShellApplicationDescriptor>();
        var identities = new HashSet<DockTargetIdentity>();
        var duplicateCount = 0;

        foreach (var shortcutPath in shortcutPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadata = ShellLinkMetadataReader.Read(shortcutPath);
            if (!metadata.Succeeded)
            {
                issues.Add(new WindowsShellDiscoveryIssue(
                    metadata.AccessDenied ? WindowsShellDiscoveryIssueKind.AccessDenied : WindowsShellDiscoveryIssueKind.InvalidShortcut,
                    metadata.DiagnosticCode));
                continue;
            }

            var displayName = Path.GetFileNameWithoutExtension(shortcutPath);
            if (string.IsNullOrWhiteSpace(displayName) || displayName.Length > MaximumDisplayNameLength)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.InvalidShortcut, "invalid-display-name"));
                continue;
            }

            if (metadata.Arguments.Length > MaximumArgumentsLength)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.UnsupportedTarget, "shortcut-arguments-too-long"));
                continue;
            }

            DockTargetIdentity identity;
            if (!string.IsNullOrWhiteSpace(metadata.AppUserModelId))
            {
                try
                {
                    identity = DockTargetIdentity.Create(
                        DockIdentityScheme.WindowsAppUserModelId,
                        shortcutPath,
                        metadata.AppUserModelId);
                }
                catch (ArgumentException)
                {
                    issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.InvalidShortcut, "invalid-aumid"));
                    continue;
                }
            }
            else
            {
                if (string.IsNullOrWhiteSpace(metadata.TargetPath))
                {
                    issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.UnresolvedTarget, "shortcut-target-not-found"));
                    continue;
                }

                if (!IsExecutableTarget(metadata.TargetPath) || !IsLocalFilesystemTarget(metadata.TargetPath))
                {
                    var isNetworkTarget = !IsLocalFilesystemTarget(metadata.TargetPath);
                    issues.Add(new WindowsShellDiscoveryIssue(
                        WindowsShellDiscoveryIssueKind.UnsupportedTarget,
                        isNetworkTarget ? "network-shortcut-target-not-accessed" : "shortcut-target-is-not-an-application"));
                    continue;
                }

                if (!FileIdentityResolver.TryGetCanonicalPath(metadata.TargetPath, out var canonicalPath, out var accessDenied))
                {
                    issues.Add(new WindowsShellDiscoveryIssue(
                        accessDenied ? WindowsShellDiscoveryIssueKind.AccessDenied : WindowsShellDiscoveryIssueKind.UnresolvedTarget,
                        accessDenied ? "target-identity-access-denied" : "shortcut-target-not-found"));
                    continue;
                }

                var encodedPath = Convert.ToBase64String(Encoding.UTF8.GetBytes(canonicalPath));
                var encodedArguments = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata.Arguments));
                var comparisonKey = $"path64:{encodedPath}|args64:{encodedArguments}";
                identity = DockTargetIdentity.Create(
                    DockIdentityScheme.WindowsShellLinkPath,
                    shortcutPath,
                    comparisonKey);
            }

            if (!identities.Add(identity))
            {
                duplicateCount++;
                continue;
            }

            applications.Add(new WindowsShellApplicationDescriptor(displayName, identity, shortcutPath));
        }

        var appsFolderEntries = WindowsAppsFolderAumidDiscovery.Read(issues, cancellationToken)
            .OrderBy(entry => entry.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.AppUserModelId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        foreach (var entry in appsFolderEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DockTargetIdentity identity;
            try
            {
                identity = DockTargetIdentity.Create(
                    DockIdentityScheme.WindowsAppUserModelId,
                    entry.AppUserModelId,
                    entry.AppUserModelId);
            }
            catch (ArgumentException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.MetadataUnavailable, "apps-folder-invalid-aumid"));
                continue;
            }

            if (!identities.Add(identity))
            {
                duplicateCount++;
                continue;
            }

            applications.Add(new WindowsShellApplicationDescriptor(
                entry.DisplayName,
                identity,
                $"shell:AppsFolder\\{entry.AppUserModelId}"));
        }

        return WindowsShellCatalogResult.Succeeded(
            applications,
            issues,
            shortcutPaths.Count,
            duplicateCount,
            appsFolderEntries.Length);
    }

    private static bool IsExecutableTarget(string targetPath) => Path.GetExtension(targetPath).ToUpperInvariant() is
        ".EXE" or ".COM" or ".BAT" or ".CMD" or ".MSC" or ".APPREF-MS";

    private static bool IsLocalFilesystemTarget(string path)
    {
        if (path.StartsWith("\\\\?\\UNC\\", StringComparison.OrdinalIgnoreCase))
            return false;
        if (path.StartsWith("\\\\?\\", StringComparison.Ordinal))
            return path.Length > 6 && char.IsAsciiLetter(path[4]) && path[5] == ':';
        return !path.StartsWith("\\\\", StringComparison.Ordinal) && Path.IsPathRooted(path);
    }

    private static List<string> GetStartMenuRoots(List<WindowsShellDiscoveryIssue> issues)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Add(Environment.SpecialFolder.Programs);
        Add(Environment.SpecialFolder.CommonPrograms);
        return roots.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();

        void Add(Environment.SpecialFolder folder)
        {
            try
            {
                var path = Environment.GetFolderPath(folder);
                if (string.IsNullOrWhiteSpace(path))
                    return;

                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.Directory) != 0)
                    roots.Add(Path.GetFullPath(path));
                else
                    issues.Add(new WindowsShellDiscoveryIssue(
                        WindowsShellDiscoveryIssueKind.EnumerationFailed,
                        "known-start-menu-folder-is-not-directory"));
            }
            catch (FileNotFoundException)
            {
                // Windows can return a known-folder path before the folder has been created.
            }
            catch (DirectoryNotFoundException)
            {
                // Missing optional Start Menu roots are allowed when another root is available.
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "known-start-menu-folder-access-denied"));
            }
            catch (System.Security.SecurityException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "known-start-menu-folder-access-denied"));
            }
            catch (IOException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.EnumerationFailed, "known-start-menu-folder-unavailable"));
            }
        }
    }

    private static List<string> EnumerateShortcuts(
        IReadOnlyList<string> roots,
        List<WindowsShellDiscoveryIssue> issues,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>(roots.Reverse());

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            if (!visited.Add(directory))
                continue;

            try
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.lnk", SearchOption.TopDirectoryOnly))
                {
                    files.Add(Path.GetFullPath(file));
                    if (files.Count > MaximumShortcutCount)
                    {
                        files.RemoveAt(files.Count - 1);
                        issues.Add(new WindowsShellDiscoveryIssue(
                            WindowsShellDiscoveryIssueKind.EntryLimitReached,
                            "start-menu-shortcut-limit-reached"));
                        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
                    }
                }

                foreach (var child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                {
                    try
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Push(Path.GetFullPath(child));
                    }
                    catch (UnauthorizedAccessException)
                    {
                        issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "start-menu-child-access-denied"));
                    }
                    catch (System.Security.SecurityException)
                    {
                        issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "start-menu-child-access-denied"));
                    }
                    catch (IOException)
                    {
                        issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.EnumerationFailed, "start-menu-child-unavailable"));
                    }
                }
            }
            catch (UnauthorizedAccessException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "start-menu-folder-access-denied"));
            }
            catch (System.Security.SecurityException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.AccessDenied, "start-menu-folder-access-denied"));
            }
            catch (DirectoryNotFoundException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.EnumerationFailed, "start-menu-folder-disappeared"));
            }
            catch (IOException)
            {
                issues.Add(new WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind.EnumerationFailed, "start-menu-folder-unavailable"));
            }
        }

        return files.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
    }

    internal sealed record ShellLinkMetadataResult(
        bool Succeeded,
        string TargetPath,
        string Arguments,
        string? AppUserModelId,
        bool AccessDenied,
        string DiagnosticCode);

    internal static class ShellLinkMetadataReader
    {
        private static readonly PropertyKey AppUserModelIdKey = new(
            new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
            5);
        private static readonly Guid ShellLinkClassId = new("00021401-0000-0000-C000-000000000046");

        public static ShellLinkMetadataResult Read(string path)
        {
            object? shellLinkObject = null;
            try
            {
                shellLinkObject = Activator.CreateInstance(Type.GetTypeFromCLSID(ShellLinkClassId, throwOnError: true)!)!;
                var persistFile = (IPersistFile)shellLinkObject;
                var loadResult = persistFile.Load(path, 0);
                if (loadResult < 0)
                    return Failure(loadResult);

                var shellLink = (IShellLinkW)shellLinkObject;
                var targetBuffer = new StringBuilder(32768);
                var getPathResult = shellLink.GetPath(targetBuffer, targetBuffer.Capacity, IntPtr.Zero, 0);
                if (getPathResult < 0)
                    return Failure(getPathResult);

                var argumentsBuffer = new StringBuilder(32768);
                var getArgumentsResult = shellLink.GetArguments(argumentsBuffer, argumentsBuffer.Capacity);
                if (getArgumentsResult < 0)
                    return Failure(getArgumentsResult);

                var aumid = ReadAppUserModelId(shellLinkObject);
                return new ShellLinkMetadataResult(true, targetBuffer.ToString(), argumentsBuffer.ToString(), aumid, false, "ok");
            }
            catch (UnauthorizedAccessException)
            {
                return new ShellLinkMetadataResult(false, string.Empty, string.Empty, null, true, "shortcut-access-denied");
            }
            catch (COMException exception)
            {
                return Failure(exception.HResult);
            }
            catch (IOException)
            {
                return new ShellLinkMetadataResult(false, string.Empty, string.Empty, null, false, "shortcut-io-failed");
            }
            catch (Exception exception) when (exception is InvalidCastException or InvalidComObjectException or ArgumentException)
            {
                return new ShellLinkMetadataResult(false, string.Empty, string.Empty, null, false, "shortcut-format-invalid");
            }
            finally
            {
                if (shellLinkObject is not null && Marshal.IsComObject(shellLinkObject))
                    Marshal.ReleaseComObject(shellLinkObject);
            }
        }

        private static string? ReadAppUserModelId(object shellLinkObject)
        {
            if (shellLinkObject is not IPropertyStore propertyStore)
                return null;

            var key = AppUserModelIdKey;
            var propVariant = Marshal.AllocCoTaskMem(IntPtr.Size == 8 ? 24 : 16);
            try
            {
                var cleared = new byte[IntPtr.Size == 8 ? 24 : 16];
                Marshal.Copy(cleared, 0, propVariant, cleared.Length);
                var result = propertyStore.GetValue(ref key, propVariant);
                if (result < 0 || unchecked((ushort)Marshal.ReadInt16(propVariant)) != NativeMethods.VtLpwstr)
                    return null;

                var conversionResult = NativeMethods.PropVariantToStringAlloc(propVariant, out var stringPointer);
                if (conversionResult < 0 || stringPointer == IntPtr.Zero)
                    return null;

                try
                {
                    var aumid = Marshal.PtrToStringUni(stringPointer);
                    return string.IsNullOrWhiteSpace(aumid) || aumid.Length > 512 ? null : aumid;
                }
                finally
                {
                    NativeMethods.CoTaskMemFree(stringPointer);
                }
            }
            finally
            {
                NativeMethods.PropVariantClear(propVariant);
                NativeMethods.CoTaskMemFree(propVariant);
            }
        }

        private static ShellLinkMetadataResult Failure(int hresult)
        {
            var win32Error = hresult & 0xFFFF;
            var denied = win32Error is 5 or 65;
            return new ShellLinkMetadataResult(
                false,
                string.Empty,
                string.Empty,
                null,
                denied,
                denied ? "shortcut-access-denied" : "shortcut-load-failed");
        }
    }

    internal static class FileIdentityResolver
    {
        private const uint FileReadAttributes = 0x0080;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;

        public static bool TryGetCanonicalPath(string path, out string canonicalPath, out bool accessDenied)
        {
            canonicalPath = string.Empty;
            accessDenied = false;
            using var handle = NativeMethods.CreateFileW(
                path,
                FileReadAttributes,
                FileShareRead | FileShareWrite | FileShareDelete,
                IntPtr.Zero,
                OpenExisting,
                FileFlagBackupSemantics,
                IntPtr.Zero);
            if (handle.IsInvalid)
            {
                var error = Marshal.GetLastWin32Error();
                accessDenied = error is 5 or 65;
                return false;
            }

            var buffer = new StringBuilder(32768);
            var length = NativeMethods.GetFinalPathNameByHandleW(handle, buffer, (uint)buffer.Capacity, 0);
            if (length == 0 || length >= buffer.Capacity)
            {
                var error = Marshal.GetLastWin32Error();
                accessDenied = error is 5 or 65;
                return false;
            }

            canonicalPath = buffer.ToString();
            return true;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(Guid formatId, uint propertyId)
    {
        public Guid FormatId = formatId;
        public uint PropertyId = propertyId;
    }

    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint propertyCount);
        [PreserveSig] int GetAt(uint propertyIndex, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int SetValue(ref PropertyKey key, IntPtr value);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IPersistFile
    {
        [PreserveSig] int GetClassID(out Guid classId);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load([MarshalAs(UnmanagedType.LPWStr)] string fileName, uint mode);
        [PreserveSig] int Save([MarshalAs(UnmanagedType.LPWStr)] string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);
        [PreserveSig] int SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string fileName);
        [PreserveSig] int GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string fileName);
    }

    [ComImport]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int maxPath, IntPtr findData, uint flags);
        [PreserveSig] int GetIDList(out IntPtr itemIdList);
        [PreserveSig] int SetIDList(IntPtr itemIdList);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int maxName);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder directory, int maxPath);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string directory);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder arguments, int maxPath);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string arguments);
        [PreserveSig] int GetHotkey(out ushort hotkey);
        [PreserveSig] int SetHotkey(ushort hotkey);
        [PreserveSig] int GetShowCmd(out int showCommand);
        [PreserveSig] int SetShowCmd(int showCommand);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int maxPath, out int iconIndex);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, uint reserved);
        [PreserveSig] int Resolve(IntPtr window, uint flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string path);
    }

    private static class NativeMethods
    {
        public const int CoinitMultithreaded = 0x0;
        public const int RpcEChangedMode = unchecked((int)0x80010106);
        public const ushort VtLpwstr = 31;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("ole32.dll")]
        public static extern void CoTaskMemFree(IntPtr memory);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(IntPtr value);

        [DllImport("propsys.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int PropVariantToStringAlloc(IntPtr value, out IntPtr result);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetFinalPathNameByHandleW(
            SafeFileHandle file,
            [Out] StringBuilder filePath,
            uint filePathSize,
            uint flags);
    }
}
