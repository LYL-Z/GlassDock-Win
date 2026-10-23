using System.Buffers.Binary;
using System.IO.Compression;
using System.Runtime.InteropServices;
using GlassDock.Core;

namespace GlassDock.Windows.Shell;

public interface IWindowsShellIconExtractor
{
    ValueTask<WindowsShellIconResult> ResolveIconAsync(
        string shellParsingName,
        CancellationToken cancellationToken = default);
}

/// <summary>Extracts real Shell icons as bounded, alpha-preserving PNG assets.</summary>
public sealed class WindowsShellIconExtractor : IWindowsShellIconExtractor
{
    private const int RequestedPixels = 256;
    private const int MaximumPixels = 512;
    private static readonly Guid ShellItemImageFactoryId = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    public ValueTask<WindowsShellIconResult> ResolveIconAsync(
        string shellParsingName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shellParsingName);
        if (shellParsingName.Contains('\0'))
            throw new ArgumentException("Shell parsing name cannot contain a null character.", nameof(shellParsingName));

        return new ValueTask<WindowsShellIconResult>(Task.Run(
            () => Extract(shellParsingName, cancellationToken), cancellationToken));
    }

    private static WindowsShellIconResult Extract(string parsingName, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var coResult = NativeMethods.CoInitializeEx(IntPtr.Zero, NativeMethods.CoinitMultithreaded);
        var initialized = coResult >= 0;
        if (!initialized && coResult != NativeMethods.RpcEChangedMode)
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Unsupported, "com-initialization-failed");

        IShellItemImageFactory? factory = null;
        IntPtr bitmap = IntPtr.Zero;
        var stage = "create-shell-item";
        try
        {
            var iid = ShellItemImageFactoryId;
            var createResult = NativeMethods.SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out factory);
            if (createResult < 0)
                return Failure(createResult, stage);

            var size = new NativeSize(RequestedPixels, RequestedPixels);
            stage = "get-shell-icon";
            var imageResult = factory.GetImage(size, NativeMethods.SiigbfIconOnly | NativeMethods.SiigbfBiggersSizeOk, out bitmap);
            if (imageResult < 0 || bitmap == IntPtr.Zero)
                return Failure(imageResult, stage);

            cancellationToken.ThrowIfCancellationRequested();
            stage = "inspect-shell-bitmap";
            if (!NativeMethods.GetObjectW(bitmap, Marshal.SizeOf<NativeBitmap>(), out var nativeBitmap).Equals(Marshal.SizeOf<NativeBitmap>()))
                return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-bitmap-invalid");

            var width = Math.Abs(nativeBitmap.Width);
            var height = Math.Abs(nativeBitmap.Height);
            if (width is <= 0 or > MaximumPixels || height is <= 0 or > MaximumPixels
                || (long)width * height > MaximumPixels * MaximumPixels)
            {
                return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-dimensions-out-of-range");
            }

            var pixels = new byte[checked(width * height * 4)];
            var info = BitmapInfo.CreateTopDown(width, height);
            var dc = NativeMethods.GetDC(IntPtr.Zero);
            if (dc == IntPtr.Zero)
                return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-device-context-unavailable");

            int copiedLines;
            try
            {
                copiedLines = NativeMethods.GetDIBits(dc, bitmap, 0, (uint)height, pixels, ref info, NativeMethods.DibRgbColors);
            }
            finally
            {
                NativeMethods.ReleaseDC(IntPtr.Zero, dc);
            }

            if (copiedLines != height)
                return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-pixel-read-failed");

            var png = PngEncoder.EncodeBgra(pixels, width, height);
            return WindowsShellIconResult.Found(new WindowsShellIconAsset(png));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.AccessDenied, "shell-icon-access-denied");
        }
        catch (OutOfMemoryException)
        {
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-memory-limit");
        }
        catch (COMException exception)
        {
            return Failure(exception.HResult, stage);
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or OverflowException)
        {
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed, "shell-icon-decode-failed");
        }
        finally
        {
            if (bitmap != IntPtr.Zero)
                NativeMethods.DeleteObject(bitmap);
            if (factory is not null && Marshal.IsComObject(factory))
                Marshal.ReleaseComObject(factory);
            if (initialized)
                NativeMethods.CoUninitialize();
        }
    }

    private static WindowsShellIconResult Failure(int hresult, string stage)
    {
        var error = hresult & 0xFFFF;
        if (error == 5)
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.AccessDenied, "shell-icon-access-denied");
        if (error is 2 or 3 or 1168)
            return WindowsShellIconResult.Unavailable(WindowsShellIconStatus.NotFound, "shell-icon-source-not-found");
        return WindowsShellIconResult.Unavailable(
            WindowsShellIconStatus.Failed,
            $"{stage}-0x{unchecked((uint)hresult):X8}");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize(int width, int height)
    {
        public int Width = width;
        public int Height = height;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeBitmap
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RgbQuad
    {
        public byte Blue;
        public byte Green;
        public byte Red;
        public byte Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public RgbQuad Colors;

        public static BitmapInfo CreateTopDown(int width, int height) => new()
        {
            Header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = NativeMethods.BiRgb,
                SizeImage = checked((uint)(width * height * 4))
            }
        };
    }

    [ComImport]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(NativeSize size, uint flags, out IntPtr bitmap);
    }

    private static class NativeMethods
    {
        public const int CoinitMultithreaded = 0;
        public const int RpcEChangedMode = unchecked((int)0x80010106);
        public const uint SiigbfBiggersSizeOk = 0x1;
        public const uint SiigbfIconOnly = 0x4;
        public const uint DibRgbColors = 0;
        public const uint BiRgb = 0;

        [DllImport("ole32.dll")]
        public static extern int CoInitializeEx(IntPtr reserved, int coInit);

        [DllImport("ole32.dll")]
        public static extern void CoUninitialize();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string path,
            IntPtr bindContext,
            ref Guid interfaceId,
            [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory imageFactory);

        [DllImport("gdi32.dll", EntryPoint = "GetObjectW", SetLastError = true)]
        public static extern int GetObjectW(IntPtr handle, int size, out NativeBitmap bitmap);

        [DllImport("gdi32.dll", SetLastError = true)]
        public static extern int GetDIBits(IntPtr dc, IntPtr bitmap, uint startScan, uint scanLines,
            [Out] byte[] pixels, ref BitmapInfo bitmapInfo, uint usage);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr handle);

        [DllImport("user32.dll")]
        public static extern IntPtr GetDC(IntPtr window);

        [DllImport("user32.dll")]
        public static extern int ReleaseDC(IntPtr window, IntPtr dc);
    }

    private static class PngEncoder
    {
        private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];
        private static readonly uint[] CrcTable = CreateCrcTable();

        public static byte[] EncodeBgra(byte[] bgra, int width, int height)
        {
            var alphaExists = false;
            for (var i = 3; i < bgra.Length; i += 4)
            {
                if (bgra[i] != 0)
                {
                    alphaExists = true;
                    break;
                }
            }

            using var rawStream = new MemoryStream(checked(height * (width * 4 + 1)));
            for (var y = 0; y < height; y++)
            {
                rawStream.WriteByte(0);
                var rowStart = y * width * 4;
                for (var x = 0; x < width; x++)
                {
                    var offset = rowStart + x * 4;
                    rawStream.WriteByte(bgra[offset + 2]);
                    rawStream.WriteByte(bgra[offset + 1]);
                    rawStream.WriteByte(bgra[offset]);
                    rawStream.WriteByte(alphaExists ? bgra[offset + 3] : (byte)255);
                }
            }

            rawStream.Position = 0;
            using var compressed = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
                rawStream.CopyTo(zlib);

            using var output = new MemoryStream();
            output.Write(Signature);
            Span<byte> header = stackalloc byte[13];
            BinaryPrimitives.WriteUInt32BigEndian(header[0..4], (uint)width);
            BinaryPrimitives.WriteUInt32BigEndian(header[4..8], (uint)height);
            header[8] = 8;
            header[9] = 6;
            WriteChunk(output, "IHDR"u8, header);
            WriteChunk(output, "IDAT"u8, compressed.ToArray());
            WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
            return output.ToArray();
        }

        private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            Span<byte> length = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
            stream.Write(length);
            stream.Write(type);
            stream.Write(data);

            var crc = 0xFFFFFFFFu;
            foreach (var value in type)
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            foreach (var value in data)
                crc = CrcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
            Span<byte> crcBytes = stackalloc byte[4];
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, ~crc);
            stream.Write(crcBytes);
        }

        private static uint[] CreateCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < table.Length; i++)
            {
                var value = i;
                for (var bit = 0; bit < 8; bit++)
                    value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
                table[i] = value;
            }
            return table;
        }
    }
}
