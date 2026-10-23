using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;
using SharpDX;
using SharpDX.Direct3D;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using Device = SharpDX.Direct3D11.Device;
using Timer = System.Windows.Forms.Timer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            ExclusiveProbeForm.UsePhysicalDpiContext();
            if (args.Length > 0 && string.Equals(args[0], "--preflight-only", StringComparison.Ordinal))
                return ExclusiveProbeForm.RunPreflight(ProbeOptions.Parse(args.Skip(1).ToArray()));

            var options = ProbeOptions.Parse(args);
            ApplicationConfiguration.Initialize();
            using var form = new ExclusiveProbeForm(options);
            Application.Run(form);
            return form.ExitCode;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 2;
        }
    }
}

internal sealed record ProbeOptions(string OutputRoot, string DeviceName, int Width, int Height, int RefreshHz, int DurationSeconds)
{
    public static ProbeOptions Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index += 2)
        {
            if (index + 1 >= args.Length || !args[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Expected pairs of --name value arguments.");
            values[args[index]] = args[index + 1];
        }

        string Required(string name) => values.TryGetValue(name, out var value)
            ? value
            : throw new ArgumentException($"Missing {name}.");
        int Number(string name, int minimum, int maximum)
        {
            if (!int.TryParse(Required(name), out var parsed) || parsed < minimum || parsed > maximum)
                throw new ArgumentOutOfRangeException(name, $"Expected {minimum}..{maximum}.");
            return parsed;
        }

        var root = Path.GetFullPath(Required("--output-root"));
        if (!root.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Probe evidence is restricted to D:. ");

        return new ProbeOptions(root, Required("--device"), Number("--width", 320, 16384),
            Number("--height", 240, 16384), Number("--refresh", 24, 1000), Number("--duration", 3, 30));
    }
}

internal sealed class ExclusiveProbeForm : Form
{
    private const int EnumCurrentSettings = -1;
    private readonly ProbeOptions _options;
    private readonly Timer _timer = new() { Interval = 16 };
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;
    private readonly List<object> _events = [];
    private Factory1? _factory;
    private Adapter? _adapter;
    private Output? _output;
    private Device? _device;
    private SwapChain? _swapChain;
    private Texture2D? _backBuffer;
    private RenderTargetView? _renderTarget;
    private string _phase = "Starting";
    private string? _error;
    private string _exitReason = "DurationElapsed";
    private DateTimeOffset? _fullscreenConfirmedAtUtc;
    private int _framesSubmitted;
    private bool? _lastObservedFullscreen;
    private DateTimeOffset _stopAt;
    private DateTimeOffset _lastStatusAtUtc;
    private bool _shutdown;

    public int ExitCode { get; private set; }

    public static void UsePhysicalDpiContext()
    {
        if (SetThreadDpiAwarenessContext(new IntPtr(-4)) == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not set Per-Monitor-V2 context for physical-pixel bounds.");
    }

    private static System.Drawing.Rectangle GetPrimaryPhysicalBounds() =>
        new(0, 0, GetSystemMetrics(0), GetSystemMetrics(1));

    public static int RunPreflight(ProbeOptions options)
    {
        Directory.CreateDirectory(options.OutputRoot);
        object result;
        var exitCode = 0;
        try
        {
            var currentMode = GetCurrentMode(options.DeviceName);
            var desktopBounds = GetPrimaryPhysicalBounds();
            using var factory = new Factory1();
            var matchedOutput = false;
            var matchingMode = false;
            string? adapterDescription = null;
            for (var adapterIndex = 0; adapterIndex < factory.GetAdapterCount() && !matchedOutput; adapterIndex++)
            {
                using var adapter = factory.GetAdapter(adapterIndex);
                for (var outputIndex = 0; outputIndex < adapter.GetOutputCount(); outputIndex++)
                {
                    using var output = adapter.GetOutput(outputIndex);
                    var description = output.Description;
                    if (!description.IsAttachedToDesktop || !string.Equals(description.DeviceName, options.DeviceName, StringComparison.OrdinalIgnoreCase))
                        continue;
                    matchedOutput = true;
                    adapterDescription = adapter.Description.Description;
                    matchingMode = output.GetDisplayModeList(Format.R8G8B8A8_UNorm, (DisplayModeEnumerationFlags)0)
                        .Any(mode => mode.Width == options.Width && mode.Height == options.Height &&
                                     mode.RefreshRate.Denominator > 0 &&
                                     Math.Abs(mode.RefreshRate.Numerator / (double)mode.RefreshRate.Denominator - options.RefreshHz) < 0.5);
                    break;
                }
            }

            var currentMatches = currentMode.Width == options.Width && currentMode.Height == options.Height && currentMode.RefreshHz == options.RefreshHz;
            var boundsMatch = desktopBounds.Width == options.Width && desktopBounds.Height == options.Height;
            var passed = matchedOutput && matchingMode && currentMatches && boundsMatch;
            if (!passed)
                exitCode = 3;
            result = new
            {
                CapturedAtUtc = DateTimeOffset.UtcNow,
                Status = passed ? "PASS" : "BLOCKED",
                ChangesDisplaySettings = false,
                CreatesVisibleWindow = false,
                RequestsFullscreen = false,
                DeviceName = options.DeviceName,
                RequestedMode = new { Width = options.Width, Height = options.Height, RefreshHz = options.RefreshHz },
                CurrentMode = currentMode,
                DesktopBounds = new { Width = desktopBounds.Width, Height = desktopBounds.Height },
                DxgiOutputMatched = matchedOutput,
                AdapterDescription = adapterDescription,
                ExactModeEnumerated = matchingMode,
                CurrentModeMatches = currentMatches,
                BoundsMatch = boundsMatch
            };
        }
        catch (Exception exception)
        {
            exitCode = 3;
            result = new { CapturedAtUtc = DateTimeOffset.UtcNow, Status = "BLOCKED", ChangesDisplaySettings = false, CreatesVisibleWindow = false, RequestsFullscreen = false, Error = exception.ToString() };
        }

        var outputPath = Path.Combine(options.OutputRoot, "preflight.json");
        File.WriteAllText(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return exitCode;
    }

    public ExclusiveProbeForm(ProbeOptions options)
    {
        _options = options;
        Text = "GlassDock M0 DXGI exclusive fullscreen probe";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = GetPrimaryPhysicalBounds();
        ShowInTaskbar = true;
        KeyPreview = true;
        BackColor = System.Drawing.Color.FromArgb(13, 28, 48);
        _timer.Tick += OnTimerTick;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.KeyCode == Keys.Escape)
            {
                _exitReason = "EscapeKey";
                Close();
            }
        };
        FormClosing += (_, _) => ShutdownProbe();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            StartProbe();
            _timer.Start();
        }
        catch (Exception exception)
        {
            _phase = "FailedBeforeExclusiveEntry";
            _error = exception.ToString();
            ExitCode = 1;
            WriteStatus();
            _ = BeginInvoke(Close);
        }
    }

    private void StartProbe()
    {
        Directory.CreateDirectory(_options.OutputRoot);
        var initialMode = GetCurrentMode(_options.DeviceName);
        if (initialMode.Width != _options.Width || initialMode.Height != _options.Height || initialMode.RefreshHz != _options.RefreshHz)
            throw new InvalidOperationException($"Preflight mode changed before the probe: requested={_options.Width}x{_options.Height}@{_options.RefreshHz}, current={initialMode.Width}x{initialMode.Height}@{initialMode.RefreshHz}.");

        if (Bounds.Width != _options.Width || Bounds.Height != _options.Height)
            throw new InvalidOperationException($"Primary monitor bounds do not match requested current mode: bounds={Bounds.Width}x{Bounds.Height}, requested={_options.Width}x{_options.Height}.");

        _factory = new Factory1();
        FindAttachedOutput(_options.DeviceName);
        var matchingMode = _output!.GetDisplayModeList(Format.R8G8B8A8_UNorm, (DisplayModeEnumerationFlags)0)
            .Any(mode => mode.Width == _options.Width && mode.Height == _options.Height &&
                         mode.RefreshRate.Denominator > 0 &&
                         Math.Abs(mode.RefreshRate.Numerator / (double)mode.RefreshRate.Denominator - _options.RefreshHz) < 0.5);
        if (!matchingMode)
            throw new InvalidOperationException("DXGI did not enumerate the exact current width/height/refresh combination in R8G8B8A8_UNorm; exclusive entry was not attempted.");

        _device = new Device(_adapter!, DeviceCreationFlags.BgraSupport);
        var description = new SwapChainDescription
        {
            BufferCount = 2,
            ModeDescription = new ModeDescription(_options.Width, _options.Height,
                new Rational(_options.RefreshHz, 1), Format.R8G8B8A8_UNorm),
            SampleDescription = new SampleDescription(1, 0),
            Usage = Usage.RenderTargetOutput,
            OutputHandle = Handle,
            IsWindowed = true,
            SwapEffect = SwapEffect.Discard,
            Flags = SwapChainFlags.None
        };
        _swapChain = new SwapChain(_factory, _device, description);
        _backBuffer = Texture2D.FromSwapChain<Texture2D>(_swapChain, 0);
        _renderTarget = new RenderTargetView(_device, _backBuffer);
        _device.ImmediateContext.OutputMerger.SetTargets(_renderTarget);
        _device.ImmediateContext.ClearRenderTargetView(_renderTarget, new SharpDX.Mathematics.Interop.RawColor4(0.025f, 0.075f, 0.14f, 1.0f));
        _swapChain.Present(0, PresentFlags.None);

        _phase = "RequestingExclusiveFullscreen";
        WriteStatus();
        _swapChain.SetFullscreenState(true, _output!);
        if (!_swapChain.IsFullScreen)
            throw new InvalidOperationException("IDXGISwapChain.SetFullscreenState returned without GetFullscreenState confirming exclusive mode.");

        _fullscreenConfirmedAtUtc = DateTimeOffset.UtcNow;
        _stopAt = _fullscreenConfirmedAtUtc.Value.AddSeconds(_options.DurationSeconds);
        _phase = "FullscreenConfirmed";
        _lastObservedFullscreen = true;
        _events.Add(new { AtUtc = _fullscreenConfirmedAtUtc, Event = "GetFullscreenState=true", Output = _output.Description.DeviceName });
        WriteStatus();
    }

    private void FindAttachedOutput(string deviceName)
    {
        for (var adapterIndex = 0; adapterIndex < _factory!.GetAdapterCount(); adapterIndex++)
        {
            var candidateAdapter = _factory.GetAdapter(adapterIndex);
            for (var outputIndex = 0; outputIndex < candidateAdapter.GetOutputCount(); outputIndex++)
            {
                var candidateOutput = candidateAdapter.GetOutput(outputIndex);
                var description = candidateOutput.Description;
                if (description.IsAttachedToDesktop && string.Equals(description.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
                {
                    _adapter = candidateAdapter;
                    _output = candidateOutput;
                    return;
                }
                candidateOutput.Dispose();
            }
            candidateAdapter.Dispose();
        }
        throw new InvalidOperationException($"No attached DXGI output matched {deviceName}; exclusive entry was not attempted.");
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_shutdown || _swapChain is null || _renderTarget is null || _device is null)
            return;

        try
        {
            if (File.Exists(Path.Combine(_options.OutputRoot, "stop.json")))
            {
                _exitReason = "ControllerStopSignal";
                Close();
                return;
            }

            _device.ImmediateContext.ClearRenderTargetView(_renderTarget, new SharpDX.Mathematics.Interop.RawColor4(0.025f, 0.075f, 0.14f, 1.0f));
            _swapChain.Present(1, PresentFlags.None);
            _framesSubmitted++;
            var isFullscreen = _swapChain.IsFullScreen;
            _phase = isFullscreen ? "FullscreenConfirmed" : "ExclusiveLostWhileRunning";
            if (_lastObservedFullscreen != isFullscreen)
                _events.Add(new { AtUtc = DateTimeOffset.UtcNow, Event = isFullscreen ? "GetFullscreenState=true" : "GetFullscreenState=false" });
            _lastObservedFullscreen = isFullscreen;
            if ((DateTimeOffset.UtcNow - _lastStatusAtUtc).TotalMilliseconds >= 250)
                WriteStatus();

            if (DateTimeOffset.UtcNow >= _stopAt)
                Close();
        }
        catch (Exception exception)
        {
            _phase = "RenderOrPresentFailure";
            _error = exception.ToString();
            _exitReason = "RenderOrPresentFailure";
            ExitCode = 1;
            WriteStatus();
            Close();
        }
    }

    private void ShutdownProbe()
    {
        if (_shutdown)
            return;
        _shutdown = true;
        _timer.Stop();
        var wasFullscreen = false;
        try
        {
            if (_swapChain is not null)
            {
                wasFullscreen = _swapChain.IsFullScreen;
                _swapChain.SetFullscreenState(false, null);
            }
        }
        catch (Exception exception)
        {
            _error ??= "Exit fullscreen failed: " + exception;
            ExitCode = 1;
        }

        _renderTarget?.Dispose();
        _backBuffer?.Dispose();
        _swapChain?.Dispose();
        _device?.Dispose();
        _output?.Dispose();
        _adapter?.Dispose();
        _factory?.Dispose();
        _renderTarget = null;
        _backBuffer = null;
        _swapChain = null;
        _device = null;
        _output = null;
        _adapter = null;
        _factory = null;
        _phase = "Exited";
        _events.Add(new { AtUtc = DateTimeOffset.UtcNow, Event = "SetFullscreenState(false)", WasFullscreen = wasFullscreen });
        WriteStatus();
    }

    private void WriteStatus()
    {
        try
        {
            var currentMode = GetCurrentMode(_options.DeviceName);
            var status = new
            {
                ProcessId = Environment.ProcessId,
                StartedAtUtc = _startedAt,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                DeviceName = _options.DeviceName,
                RequestedMode = new { Width = _options.Width, Height = _options.Height, RefreshHz = _options.RefreshHz },
                CurrentMode = currentMode,
                Phase = _phase,
                Fullscreen = _swapChain?.IsFullScreen ?? false,
                FramesSubmitted = _framesSubmitted,
                ExclusiveConfirmedAtUtc = _fullscreenConfirmedAtUtc,
                ExitReason = _exitReason,
                Error = _error,
                Events = _events
            };
            var path = Path.Combine(_options.OutputRoot, "probe-status.json");
            var temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(status, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporaryPath, path, true);
            _lastStatusAtUtc = DateTimeOffset.UtcNow;
        }
        catch (Exception exception)
        {
            _error ??= "Could not write D: status evidence: " + exception.Message;
        }
    }

    private static CurrentDisplayMode GetCurrentMode(string deviceName)
    {
        var mode = new NativeDevMode { DeviceName = string.Empty, FormName = string.Empty };
        mode.Size = (short)Marshal.SizeOf<NativeDevMode>();
        if (!EnumDisplaySettings(deviceName, EnumCurrentSettings, ref mode, 0))
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"EnumDisplaySettings failed for {deviceName}.");
        return new CurrentDisplayMode(mode.PelsWidth, mode.PelsHeight, mode.DisplayFrequency, mode.BitsPerPixel);
    }

    [DllImport("user32.dll", EntryPoint = "EnumDisplaySettingsExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettings(string deviceName, int modeNumber, ref NativeDevMode mode, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NativeDevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        public short SpecVersion, DriverVersion, Size, DriverExtra;
        public int Fields;
        public int PositionX, PositionY, DisplayOrientation, DisplayFixedOutput;
        public short Color, Duplex, YResolution, TTOption, Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string FormName;
        public short LogPixels;
        public int BitsPerPixel, PelsWidth, PelsHeight, DisplayFlags, DisplayFrequency;
        public int IcmMethod, IcmIntent, MediaType, DitherType, Reserved1, Reserved2, PanningWidth, PanningHeight;
    }

    private sealed record CurrentDisplayMode(int Width, int Height, int RefreshHz, int BitsPerPixel);
}
