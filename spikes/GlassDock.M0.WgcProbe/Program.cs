using System.Drawing;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;

internal static class Program
{
    private static readonly Guid GraphicsCaptureItemInteropId = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid GraphicsCaptureItemId = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid D3D11Texture2DId = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "capture-monitor")
            {
                if (!GraphicsCaptureSession.IsSupported())
                    throw new InvalidOperationException("Windows.Graphics.Capture reports unsupported.");

                var monitorOutputPath = Path.GetFullPath(args[1]);
                if (!monitorOutputPath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Capture output is restricted to D:.");
                Directory.CreateDirectory(Path.GetDirectoryName(monitorOutputPath)!);
                CaptureMonitorFrame(monitorOutputPath);
                return 0;
            }

            if (args.Length != 4 || args[0] != "capture")
            {
                Console.Error.WriteLine("Usage: WgcProbe capture <D:\\output.png> <input-probe-ready.json> <dock-pid> | capture-monitor <D:\\output.png>");
                return 2;
            }

            if (!GraphicsCaptureSession.IsSupported())
                throw new InvalidOperationException("Windows.Graphics.Capture reports unsupported.");

            var outputPath = Path.GetFullPath(args[1]);
            if (!outputPath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Capture output is restricted to D:.");
            var readinessPath = Path.GetFullPath(args[2]);
            if (!readinessPath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Readiness file is restricted to D:.");
            if (!int.TryParse(args[3], out var dockProcessId) || dockProcessId <= 0)
                throw new ArgumentException("A valid Dock process id is required.");

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            CapturePrimaryDisplay(outputPath, readinessPath, dockProcessId);
            Console.WriteLine($"Captured composed display frame: {outputPath}");
            return 0;
        }
        catch (Exception ex)
        {
            if (args.Length > 1)
            {
                try
                {
                    var candidatePath = Path.GetFullPath(args[1]);
                    if (candidatePath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(Path.ChangeExtension(candidatePath, ".capture-error.txt"), ex.ToString());
                }
                catch
                {
                    // A failed path cannot be used as a diagnostic destination.
                }
            }
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static void CapturePrimaryDisplay(string outputPath, string readinessPath, int dockProcessId)
    {
        using var readiness = JsonDocument.Parse(File.ReadAllText(readinessPath));
        var readyRoot = readiness.RootElement;
        if (!readyRoot.GetProperty("ForegroundConfirmed").GetBoolean() ||
            !readyRoot.GetProperty("ExpectedAreaCovered").GetBoolean())
            throw new InvalidOperationException("Input probe did not confirm foreground and full-monitor coverage; capture was not saved.");

        var sinkProcessId = readyRoot.GetProperty("ProcessId").GetInt32();
        var sinkHandleText = readyRoot.GetProperty("WindowHandle").GetString() ?? throw new InvalidOperationException("Missing input probe HWND.");
        var sinkHandle = new nint(Convert.ToInt64(sinkHandleText[2..], 16));
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary monitor was not found.");
        var knownSystemTopmostWindows = ValidateCaptureSurface(sinkHandle, sinkProcessId, dockProcessId, bounds);
        var monitor = MonitorFromPoint(new PointNative(bounds.Left + 1, bounds.Top + 1), 1);
        if (monitor == nint.Zero)
            throw new InvalidOperationException("MonitorFromPoint failed.");

        var item = CreateForMonitor(monitor);
        using var d3dDevice = new SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        using var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var devicePointer);
        Marshal.ThrowExceptionForHR(unchecked((int)hr));

        var winRtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(devicePointer);
        Marshal.Release(devicePointer);

        using (var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                       winRtDevice,
                       DirectXPixelFormat.B8G8R8A8UIntNormalized,
                       2,
                       item.Size))
            using (var session = framePool.CreateCaptureSession(item))
            using (var frameReady = new ManualResetEventSlim())
            {
                framePool.FrameArrived += (_, _) => frameReady.Set();
                session.StartCapture();

                if (!frameReady.Wait(TimeSpan.FromSeconds(8)))
                    throw new TimeoutException("No Windows.Graphics.Capture frame arrived within 8 seconds.");

                using var frame = framePool.TryGetNextFrame();
                if (frame is null)
                    throw new InvalidOperationException("FrameArrived fired, but TryGetNextFrame returned null.");

                using var texture = GetTexture(frame.Surface);
                using var bitmap = CopyTextureToBitmap(d3dDevice, texture);
                ValidateBackdropMarkers(bitmap, readyRoot, bounds);
                knownSystemTopmostWindows = ValidateCaptureSurface(sinkHandle, sinkProcessId, dockProcessId, bounds);
                bitmap.Save(outputPath, ImageFormat.Png);

                var metadata = new
                {
                    CapturedAtUtc = DateTimeOffset.UtcNow,
                    CaptureApi = "Windows.Graphics.Capture / Direct3D11CaptureFramePool",
                    CaptureScope = "DWM-composed primary monitor; not a scanout timestamp",
                    MonitorBounds = new { bounds.Left, bounds.Top, bounds.Width, bounds.Height },
                    FrameContentSize = new { frame.ContentSize.Width, frame.ContentSize.Height },
                    FrameSystemRelativeTime = frame.SystemRelativeTime.ToString(),
                    ForegroundProcessId = GetForegroundProcessId(),
                    AllowedDockProcessId = dockProcessId,
                    KnownSystemTopmostWindows = knownSystemTopmostWindows,
                    BackdropMarkersValidated = true,
                    OutputSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(outputPath)))
                };
                File.WriteAllText(Path.ChangeExtension(outputPath, ".json"),
                    System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static void CaptureMonitorFrame(string outputPath)
    {
        var bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary monitor was not found.");
        var monitor = MonitorFromPoint(new PointNative(bounds.Left + 1, bounds.Top + 1), 1);
        if (monitor == nint.Zero)
            throw new InvalidOperationException("MonitorFromPoint failed.");

        var item = CreateForMonitor(monitor);
        using var d3dDevice = new SharpDX.Direct3D11.Device(DriverType.Hardware, DeviceCreationFlags.BgraSupport);
        using var dxgiDevice = d3dDevice.QueryInterface<SharpDX.DXGI.Device>();
        var hr = CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice.NativePointer, out var devicePointer);
        Marshal.ThrowExceptionForHR(unchecked((int)hr));

        IDirect3DDevice winRtDevice;
        try
        {
            winRtDevice = WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(devicePointer);
        }
        finally
        {
            Marshal.Release(devicePointer);
        }

        using (var framePool = Direct3D11CaptureFramePool.CreateFreeThreaded(
                   winRtDevice,
                   DirectXPixelFormat.B8G8R8A8UIntNormalized,
                   2,
                   item.Size))
        using (var session = framePool.CreateCaptureSession(item))
        using (var frameReady = new ManualResetEventSlim())
        {
            framePool.FrameArrived += (_, _) => frameReady.Set();
            session.StartCapture();
            if (!frameReady.Wait(TimeSpan.FromSeconds(3)))
                throw new TimeoutException("No Windows.Graphics.Capture monitor frame arrived within 3 seconds.");

            using var frame = framePool.TryGetNextFrame();
            if (frame is null)
                throw new InvalidOperationException("FrameArrived fired, but TryGetNextFrame returned null.");
            using var texture = GetTexture(frame.Surface);
            using var bitmap = CopyTextureToBitmap(d3dDevice, texture);
            bitmap.Save(outputPath, ImageFormat.Png);

            var metadata = new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                CaptureApi = "Windows.Graphics.Capture / Direct3D11CaptureFramePool",
                CaptureScope = "Primary-monitor captured frame; this is a composed frame when the graphics stack exposes composition and is not a physical scanout measurement.",
                MonitorBounds = new { bounds.Left, bounds.Top, bounds.Width, bounds.Height },
                FrameContentSize = new { frame.ContentSize.Width, frame.ContentSize.Height },
                FrameSystemRelativeTime = frame.SystemRelativeTime.ToString(),
                ForegroundProcessId = GetForegroundProcessId(),
                OutputPath = outputPath,
                CaptureCanAffectExclusivePresentation = "Capture success/failure and the adjacent DXGI IsFullScreen readings must be interpreted together."
            };
            File.WriteAllText(Path.ChangeExtension(outputPath, ".capture.json"),
                System.Text.Json.JsonSerializer.Serialize(metadata, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
    }

    private static Windows.Graphics.Capture.GraphicsCaptureItem CreateForMonitor(nint monitor)
    {
        var hr = WindowsCreateString("Windows.Graphics.Capture.GraphicsCaptureItem",
            "Windows.Graphics.Capture.GraphicsCaptureItem".Length, out var className);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            hr = RoGetActivationFactory(className, ref UnsafeGuid(GraphicsCaptureItemInteropId), out var factoryPointer);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPointer);
                hr = interop.CreateForMonitor(monitor, ref UnsafeGuid(GraphicsCaptureItemId), out var itemPointer);
                Marshal.ThrowExceptionForHR(hr);
                try
                {
                    return WinRT.MarshalInterface<Windows.Graphics.Capture.GraphicsCaptureItem>.FromAbi(itemPointer);
                }
                finally
                {
                    Marshal.Release(itemPointer);
                }
            }
            finally
            {
                Marshal.Release(factoryPointer);
            }
        }
        finally
        {
            WindowsDeleteString(className);
        }
    }

    private static ref Guid UnsafeGuid(Guid value)
    {
        // The interop signatures take REFIID by reference; keep a stable local for each call.
        _guidStorage = value;
        return ref _guidStorage;
    }

    private static Guid _guidStorage;

    private static Texture2D GetTexture(IDirect3DSurface surface)
    {
        using var surfaceReference = WinRT.MarshalInterface<IDirect3DSurface>.CreateMarshaler(surface);
        var surfacePointer = WinRT.MarshalInterface<IDirect3DSurface>.GetAbi(surfaceReference);
        var accessIid = typeof(IDirect3DDxgiInterfaceAccess).GUID;
        var hr = Marshal.QueryInterface(surfacePointer, in accessIid, out var accessPointer);
        Marshal.ThrowExceptionForHR(hr);
        try
        {
            var access = (IDirect3DDxgiInterfaceAccess)Marshal.GetObjectForIUnknown(accessPointer);
            var texturePointer = access.GetInterface(ref UnsafeGuid(D3D11Texture2DId));
            if (texturePointer == nint.Zero)
                throw new InvalidOperationException("IDirect3DDxgiInterfaceAccess returned a null texture.");
            try
            {
                return new Texture2D(texturePointer);
            }
            finally
            {
                Marshal.Release(texturePointer);
            }
        }
        finally
        {
            Marshal.Release(accessPointer);
        }
    }

    private static Bitmap CopyTextureToBitmap(SharpDX.Direct3D11.Device device, Texture2D source)
    {
        var description = source.Description;
        var stagingDescription = new Texture2DDescription
        {
            Width = description.Width,
            Height = description.Height,
            MipLevels = 1,
            ArraySize = 1,
            Format = description.Format,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Staging,
            BindFlags = BindFlags.None,
            CpuAccessFlags = CpuAccessFlags.Read,
            OptionFlags = ResourceOptionFlags.None
        };

        using var staging = new Texture2D(device, stagingDescription);
        device.ImmediateContext.CopyResource(source, staging);
        var mapped = device.ImmediateContext.MapSubresource(staging, 0, MapMode.Read, SharpDX.Direct3D11.MapFlags.None);
        try
        {
            var bitmap = new Bitmap(description.Width, description.Height, PixelFormat.Format32bppArgb);
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bytesPerRow = checked(description.Width * 4);
                var row = new byte[bytesPerRow];
                for (var y = 0; y < description.Height; y++)
                {
                    Marshal.Copy(IntPtr.Add(mapped.DataPointer, y * mapped.RowPitch), row, 0, bytesPerRow);
                    Marshal.Copy(row, 0, IntPtr.Add(bitmapData.Scan0, y * bitmapData.Stride), bytesPerRow);
                }
            }
            finally
            {
                bitmap.UnlockBits(bitmapData);
            }

            return bitmap;
        }
        finally
        {
            device.ImmediateContext.UnmapSubresource(staging, 0);
        }
    }

    private static void ValidateBackdropMarkers(Bitmap bitmap, JsonElement ready, Rectangle monitorBounds)
    {
        var eventLog = ready.GetProperty("EventLog").GetString() ?? throw new InvalidOperationException("Missing probe event log path.");
        var markerPath = Path.Combine(Path.GetDirectoryName(eventLog)!, "input-probe-markers.json");
        using var markers = JsonDocument.Parse(File.ReadAllText(markerPath));
        var markerRoot = markers.RootElement;
        var bounds = ready.GetProperty("Bounds");
        var y = markerRoot.GetProperty("Y").GetInt32() + bounds.GetProperty("Top").GetInt32() - monitorBounds.Top;
        var x1 = markerRoot.GetProperty("X1").GetInt32() + bounds.GetProperty("Left").GetInt32() - monitorBounds.Left;
        var x2 = markerRoot.GetProperty("X2").GetInt32() + bounds.GetProperty("Left").GetInt32() - monitorBounds.Left;
        if (x1 < 0 || x1 >= bitmap.Width || x2 < 0 || x2 >= bitmap.Width || y < 0 || y >= bitmap.Height)
            throw new InvalidOperationException("Backdrop marker coordinates are outside the captured image; no screenshot was saved.");

        var magenta = bitmap.GetPixel(x1, y);
        var cyan = bitmap.GetPixel(x2, y);
        if (!(magenta.R > 230 && magenta.G < 40 && magenta.B > 230 && cyan.R < 40 && cyan.G > 230 && cyan.B > 230))
            throw new InvalidOperationException($"Controlled backdrop markers were not present at expected pixels (magenta={magenta}, cyan={cyan}); no screenshot was saved.");
    }

    private static List<string> ValidateCaptureSurface(nint sink, int sinkProcessId, int dockProcessId, Rectangle monitor)
    {
        var knownSystemTopmostWindows = new List<string>();
        var foreground = GetForegroundWindow();
        GetWindowThreadProcessId(foreground, out var foregroundProcessId);
        if (!IsWindowVisible(sink))
            throw new InvalidOperationException($"Controlled input sink is not visible (current foreground PID={foregroundProcessId}); no screenshot was saved.");
        if (!GetWindowRect(sink, out var sinkRect) || sinkRect.Left > monitor.Left || sinkRect.Top > monitor.Top ||
            sinkRect.Right < monitor.Right || sinkRect.Bottom < monitor.Bottom)
            throw new InvalidOperationException("Controlled backdrop does not cover the entire monitor; no screenshot was saved.");

        for (var above = GetWindow(sink, 3); above != nint.Zero; above = GetWindow(above, 3))
        {
            if (!IsWindowVisible(above))
                continue;
            var exStyle = GetWindowLongPtr(above, -20).ToInt64();
            if ((exStyle & 0x00000008) == 0)
                break;
            GetWindowThreadProcessId(above, out var processId);
            if (processId == sinkProcessId || processId == dockProcessId)
                continue;
            var className = new StringBuilder(256);
            GetClassName(above, className, className.Capacity);
            var classNameText = className.ToString();
            var isTaskbar = string.Equals(classNameText, "Shell_TrayWnd", StringComparison.Ordinal);
            var isExplorerThumbnailHelper = string.Equals(classNameText, "ThumbnailDeviceHelperWnd", StringComparison.Ordinal) &&
                Process.GetProcessById(unchecked((int)processId)).ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) &&
                GetWindowRect(above, out var helperRect) && helperRect.Right - helperRect.Left <= 2 && helperRect.Bottom - helperRect.Top <= 2;
            if (isTaskbar || isExplorerThumbnailHelper)
            {
                if (GetWindowRect(above, out var shellRect) && shellRect.Left < monitor.Right && shellRect.Right > monitor.Left &&
                    shellRect.Top < monitor.Bottom && shellRect.Bottom > monitor.Top)
                    knownSystemTopmostWindows.Add($"{classNameText}: PID={processId}; rect={shellRect.Left},{shellRect.Top},{shellRect.Right},{shellRect.Bottom}");
                continue;
            }
            if (GetWindowRect(above, out var rect) && rect.Left < monitor.Right && rect.Right > monitor.Left &&
                rect.Top < monitor.Bottom && rect.Bottom > monitor.Top)
                throw new InvalidOperationException($"Unexpected topmost window HWND=0x{above.ToInt64():X}, PID={processId}, class='{className}', rect={rect.Left},{rect.Top},{rect.Right},{rect.Bottom} is above the controlled backdrop; no screenshot was saved.");
        }
        return knownSystemTopmostWindows;
    }

    [ComImport, Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IGraphicsCaptureItemInterop
    {
        [PreserveSig] int CreateForWindow(nint window, ref Guid iid, out nint result);
        [PreserveSig] int CreateForMonitor(nint monitor, ref Guid iid, out nint result);
    }

    [ComImport, Guid("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDirect3DDxgiInterfaceAccess
    {
        nint GetInterface(ref Guid iid);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct PointNative(int X, int Y);

    [DllImport("combase.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int WindowsCreateString(string sourceString, int length, out nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int WindowsDeleteString(nint hstring);

    [DllImport("combase.dll", ExactSpelling = true)]
    private static extern int RoGetActivationFactory(nint activatableClassId, ref Guid iid, out nint factory);

    [DllImport("d3d11.dll", EntryPoint = "CreateDirect3D11DeviceFromDXGIDevice", ExactSpelling = true)]
    private static extern uint CreateDirect3D11DeviceFromDXGIDevice(nint dxgiDevice, out nint graphicsDevice);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint MonitorFromPoint(PointNative point, uint flags);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetForegroundWindow();

    private static int GetForegroundProcessId()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var processId);
        return unchecked((int)processId);
    }

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);

    [DllImport("user32.dll", ExactSpelling = true)]
    private static extern nint GetWindow(nint window, uint command);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", ExactSpelling = true)]
    private static extern nint GetWindowLongPtr(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "GetClassNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassName(nint window, StringBuilder className, int maxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

}
