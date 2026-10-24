using System.Runtime.InteropServices;
using GlassDock.App;

namespace GlassDock.Windows.Shell;

public sealed record WindowsRecycleBinStatusQueryResult(bool Succeeded, long ItemCount, int HResult = 0);

public interface IWindowsRecycleBinStatusQuery
{
    WindowsRecycleBinStatusQueryResult QueryAllDrives();
}

/// <summary>Queries Windows Shell's actual Recycle Bin state without changing its contents.</summary>
public sealed class WindowsRecycleBinStatusQuery : IWindowsRecycleBinStatusQuery
{
    public WindowsRecycleBinStatusQueryResult QueryAllDrives()
    {
        try
        {
            var info = new NativeMethods.ShQueryRecycleBinInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.ShQueryRecycleBinInfo>()
            };
            var result = NativeMethods.SHQueryRecycleBinW(string.Empty, ref info);
            return new WindowsRecycleBinStatusQueryResult(result >= 0, checked((long)info.ItemCount), result);
        }
        catch (DllNotFoundException)
        {
            return new WindowsRecycleBinStatusQueryResult(false, 0, unchecked((int)0x8007007E));
        }
        catch (EntryPointNotFoundException)
        {
            return new WindowsRecycleBinStatusQueryResult(false, 0, unchecked((int)0x8007007F));
        }
        catch (OverflowException)
        {
            return new WindowsRecycleBinStatusQueryResult(false, -1, unchecked((int)0x80070216));
        }
    }

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct ShQueryRecycleBinInfo
        {
            public uint Size;
            public ulong Bytes;
            public ulong ItemCount;
        }

        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHQueryRecycleBinW(
            [MarshalAs(UnmanagedType.LPWStr)] string rootPath,
            ref ShQueryRecycleBinInfo info);
    }
}

/// <summary>Bridges the Shell query into icon variant selection. Unknown query results never pretend to be empty.</summary>
public sealed class WindowsDockIconVisualStateProvider : IDockIconVisualStateProvider
{
    private readonly IWindowsRecycleBinStatusQuery _query;

    public WindowsDockIconVisualStateProvider(IWindowsRecycleBinStatusQuery? query = null) =>
        _query = query ?? new WindowsRecycleBinStatusQuery();

    public ValueTask<DockIconVisualState> GetVisualStateAsync(
        DockIconRole role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (role != DockIconRole.Trash)
            return ValueTask.FromResult(DockIconVisualState.Default);

        return new ValueTask<DockIconVisualState>(Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = _query.QueryAllDrives();
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded || result.ItemCount < 0)
                return DockIconVisualState.Unknown;
            return result.ItemCount == 0 ? DockIconVisualState.Empty : DockIconVisualState.Full;
        }, cancellationToken));
    }
}
