using System.Runtime.InteropServices;
using GlassDock.Core;

namespace GlassDock.Windows.Shell;

/// <summary>Read-only lookup for the real Downloads Known Folder; it never creates or moves it.</summary>
public static class WindowsKnownFolderPathProvider
{
    private static readonly Guid DownloadsFolderId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static bool IsDownloadsIdentity(DockTargetIdentity identity)
    {
        if (identity.Scheme != DockIdentityScheme.WindowsKnownFolderId)
            return false;
        if (string.Equals(identity.ComparisonKey, "downloads", StringComparison.OrdinalIgnoreCase)
            || string.Equals(identity.ComparisonKey, "folderid_downloads", StringComparison.OrdinalIgnoreCase))
            return true;
        return Guid.TryParse(identity.ComparisonKey, out var parsed) && parsed == DownloadsFolderId;
    }

    public static string? GetDownloadsPath()
    {
        var comResult = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitMultithreaded);
        var initialized = comResult >= 0;
        if (!initialized && comResult != NativeMethods.RpcEChangedMode)
            return null;

        IntPtr path = IntPtr.Zero;
        try
        {
            var folderId = DownloadsFolderId;
            var result = NativeMethods.SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out path);
            return result < 0 ? null : Marshal.PtrToStringUni(path);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (path != IntPtr.Zero)
                Marshal.FreeCoTaskMem(path);
            if (initialized)
                NativeMethods.CoUninitialize();
        }
    }

    private static class NativeMethods
    {
        public const int CoinitMultithreaded = 0;
        public const int RpcEChangedMode = unchecked((int)0x80010106);

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHGetKnownFolderPath(
            ref Guid rfid,
            uint flags,
            IntPtr token,
            out IntPtr path);
    }
}
