using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Forms;

namespace GlassDock.M0.InputProbe;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        string? outputRoot = null;
        try
        {
            if (args.Length > 0 && string.Equals(args[0], "--watchdog", StringComparison.OrdinalIgnoreCase))
                return M0SafetyWatchdog.Run(args[1..]);

            var options = ParseArguments(args);
            if (options is null)
            {
                Console.Error.WriteLine("Usage: GlassDock.M0.InputProbe.exe --theme Light|Dark --output-root <D-drive-path>");
                return 2;
            }

            outputRoot = options.OutputRoot;
            Directory.CreateDirectory(outputRoot);
            var awareness = GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext());
            if (awareness != 2)
            {
                Console.Error.WriteLine($"Manifest did not establish per-monitor DPI awareness; GetAwarenessFromDpiAwarenessContext returned {awareness}.");
                File.WriteAllText(Path.Combine(outputRoot, "input-probe-error.txt"), $"DPI awareness={awareness}");
                return 1;
            }

            ApplicationConfiguration.Initialize();
            using var form = new InputProbeForm(options.Theme, outputRoot);
            Application.Run(form);
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            if (outputRoot is not null)
                File.WriteAllText(Path.Combine(outputRoot, "input-probe-error.txt"), exception.ToString());
            return 1;
        }
    }

    private static ProbeOptions? ParseArguments(string[] args)
    {
        string? theme = null;
        string? outputRoot = null;
        for (var index = 0; index < args.Length; index++)
        {
            if (args[index] == "--theme" && index + 1 < args.Length)
                theme = args[++index];
            else if (args[index] == "--output-root" && index + 1 < args.Length)
                outputRoot = args[++index];
        }

        if (theme is not ("Light" or "Dark") || string.IsNullOrWhiteSpace(outputRoot))
            return null;
        var fullOutputRoot = Path.GetFullPath(outputRoot);
        if (!fullOutputRoot.StartsWith(Path.GetPathRoot(fullOutputRoot)!, StringComparison.OrdinalIgnoreCase) ||
            !fullOutputRoot.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The input probe is restricted to output paths on D:. ");

        return new ProbeOptions(theme, fullOutputRoot);
    }

    private sealed record ProbeOptions(string Theme, string OutputRoot);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetThreadDpiAwarenessContext();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetAwarenessFromDpiAwarenessContext(nint dpiContext);
}

internal sealed class InputProbeForm : Form
{
    private const int WmMouseMove = 0x0200;
    private const int WmLeftButtonDown = 0x0201;
    private const int WmLeftButtonUp = 0x0202;
    private const int WmLeftButtonDoubleClick = 0x0203;
    private const int MkLeftButton = 0x0001;
    private const ulong SendInputExtraInfo = 0x53444F43;

    private readonly string _theme;
    private readonly string _outputRoot;
    private readonly string _eventLogPath;
    private readonly string _scenarioMarkerPath;
    private long _inputCount;
    private string _lastEvent = "No input received";
    private string _lastPoint = "-";

    public InputProbeForm(string theme, string outputRoot)
    {
        _theme = theme;
        _outputRoot = outputRoot;
        _eventLogPath = Path.Combine(outputRoot, "lower-window-events.jsonl");
        _scenarioMarkerPath = Path.Combine(outputRoot, "current-scenario.txt");
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        Bounds = Screen.PrimaryScreen?.Bounds ?? throw new InvalidOperationException("Primary screen not found.");
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        BackColor = theme == "Light" ? Color.White : Color.FromArgb(20, 22, 27);
        Text = "GlassDock M0 controlled input sink";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x00000080;
            parameters.ClassStyle |= 0x0008;
            return parameters;
        }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        SetWindowPos(Handle, new nint(-1), Left, Top, Width, Height, 0x0040);
        Activate();
        SetForegroundWindow(Handle);
        var foregroundWindow = GetForegroundWindow();
        GetWindowThreadProcessId(foregroundWindow, out var foregroundProcessId);
        var screen = Screen.PrimaryScreen?.Bounds ?? Rectangle.Empty;
        var foregroundConfirmed = foregroundProcessId == Environment.ProcessId;
        var fullscreenCovered = Left <= screen.Left && Top <= screen.Top &&
            Right >= screen.Right && Bottom >= screen.Bottom && IsWindowVisible(Handle);
        var ready = new
        {
            ReadyAtUtc = DateTimeOffset.UtcNow,
            ProcessId = Environment.ProcessId,
            WindowHandle = $"0x{Handle.ToInt64():X}",
            ForegroundProcessId = foregroundProcessId,
            ForegroundConfirmed = foregroundConfirmed,
            FullscreenCovered = fullscreenCovered,
            Theme = _theme,
            Bounds = new { Left, Top, Width, Height },
            Dpi = DeviceDpi,
            EventLog = _eventLogPath
        };
        File.WriteAllText(Path.Combine(_outputRoot, "input-probe-ready.json"), JsonSerializer.Serialize(ready, new JsonSerializerOptions { WriteIndented = true }));
        if (!foregroundConfirmed || !fullscreenCovered)
        {
            File.WriteAllText(Path.Combine(_outputRoot, "input-probe-error.txt"),
                $"ForegroundConfirmed={foregroundConfirmed}; FullscreenCovered={fullscreenCovered}; bounds={Bounds}; screen={screen}");
            Close();
            return;
        }

        Invalidate();
    }

    private void DrawValidationMarkers(Graphics graphics)
    {
        var size = Math.Max(24, (int)Math.Round(32 * DeviceDpi / 96f));
        var margin = Math.Max(24, (int)Math.Round(44 * DeviceDpi / 96f));
        using var magenta = new SolidBrush(Color.Magenta);
        using var cyan = new SolidBrush(Color.Cyan);
        graphics.FillRectangle(magenta, Width - margin - size * 2, margin, size, size);
        graphics.FillRectangle(cyan, Width - margin - size, margin, size, size);
        File.WriteAllText(Path.Combine(_outputRoot, "input-probe-markers.json"), JsonSerializer.Serialize(new
        {
            X1 = Width - margin - size * 2 + size / 2,
            X2 = Width - margin - size + size / 2,
            Y = margin + size / 2,
            Size = size,
            Magenta = "#FFFF00FF",
            Cyan = "#FF00FFFF"
        }, new JsonSerializerOptions { WriteIndented = true }));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        var light = _theme == "Light";
        var baseColor = light ? Color.FromArgb(242, 244, 247) : Color.FromArgb(19, 22, 28);
        var cellA = light ? Color.FromArgb(220, 225, 231) : Color.FromArgb(37, 43, 52);
        var cellB = light ? Color.FromArgb(251, 252, 254) : Color.FromArgb(51, 61, 74);
        using var baseBrush = new SolidBrush(baseColor);
        using var cellBrushA = new SolidBrush(cellA);
        using var cellBrushB = new SolidBrush(cellB);
        using var linePen = new Pen(light ? Color.FromArgb(96, 39, 49, 61) : Color.FromArgb(144, 231, 237, 245), 2);
        using var framePen = new Pen(light ? Color.FromArgb(190, 28, 45, 62) : Color.FromArgb(220, 241, 244, 250), 4);
        graphics.FillRectangle(baseBrush, ClientRectangle);

        var scale = DeviceDpi / 96f;
        var cell = Math.Max(24, (int)Math.Round(56 * scale));
        var halfCell = cell / 2;
        for (var y = 0; y < Height; y += cell)
        {
            for (var x = 0; x < Width; x += cell)
            {
                graphics.FillRectangle(((x / cell + y / cell) & 1) == 0 ? cellBrushA : cellBrushB,
                    x, y, Math.Min(cell, Width - x), Math.Min(cell, Height - y));
            }
        }

        var band = Math.Max(36, (int)Math.Round(78 * scale));
        for (var x = 0; x < Width; x += band)
        {
            graphics.DrawLine(linePen, x, 0, x, Height);
            graphics.DrawLine(linePen, 0, x % Math.Max(1, Height), Width, (x + Width / 3) % Math.Max(1, Height));
        }

        var labelScale = Math.Clamp(scale, 1.0f, 2.5f);
        using var titleFont = new Font("Segoe UI", 18 * labelScale, FontStyle.Bold, GraphicsUnit.Pixel);
        using var detailFont = new Font("Segoe UI", 12 * labelScale, FontStyle.Regular, GraphicsUnit.Pixel);
        var titleColor = light ? Color.FromArgb(242, 23, 31, 40) : Color.FromArgb(248, 255, 255, 255);
        using var titleBrush = new SolidBrush(titleColor);
        using var badgeBrush = new SolidBrush(light ? Color.FromArgb(232, 255, 255, 255) : Color.FromArgb(232, 14, 17, 22));
        var margin = (int)Math.Round(24 * scale);
        var badgeHeight = (int)Math.Round(102 * scale);
        var badgeWidth = Math.Min(Width - margin * 2, (int)Math.Round(1180 * scale));
        graphics.FillRectangle(badgeBrush, margin, margin, badgeWidth, badgeHeight);
        graphics.DrawRectangle(framePen, margin, margin, badgeWidth, badgeHeight);
        graphics.DrawString($"CONTROLLED BACKDROP · {_theme.ToUpperInvariant()} · {Width} × {Height} · {DeviceDpi} DPI", titleFont, titleBrush, margin + 12 * scale, margin + 10 * scale);
        graphics.DrawString($"Lower-window left click messages received: {_inputCount} · last: {_lastEvent} @ {_lastPoint}", detailFont, titleBrush, margin + 12 * scale, margin + 42 * scale);
        graphics.DrawString("Checkerboard + grid + diagonal edges are intentional contrast targets for real DWM composition capture.", detailFont, titleBrush, margin + 12 * scale, margin + 66 * scale);

        var rulerY = Height - Math.Max(50, (int)Math.Round(64 * scale));
        graphics.DrawLine(framePen, margin, rulerY, Width - margin, rulerY);
        for (var x = margin; x < Width - margin; x += Math.Max(40, (int)Math.Round(160 * scale)))
        {
            graphics.DrawLine(linePen, x, rulerY - 12 * scale, x, rulerY + 12 * scale);
            graphics.DrawString((x / scale).ToString("0"), detailFont, titleBrush, x + 2 * scale, rulerY + 12 * scale);
        }

        DrawValidationMarkers(graphics);
    }

    protected override void WndProc(ref Message m)
    {
        var isDragMove = m.Msg == WmMouseMove && (m.WParam.ToInt64() & MkLeftButton) != 0;
        if (m.Msg is WmLeftButtonDown or WmLeftButtonDoubleClick or WmLeftButtonUp || isDragMove)
            RecordInput(m, isDragMove);

        base.WndProc(ref m);
    }

    private void RecordInput(Message message, bool isDragMove)
    {
        if (message.Msg is WmLeftButtonDown or WmLeftButtonDoubleClick)
            _inputCount++;

        var localX = unchecked((short)(message.LParam.ToInt64() & 0xFFFF));
        var localY = unchecked((short)((message.LParam.ToInt64() >> 16) & 0xFFFF));
        var screenPoint = PointToScreen(new Point(localX, localY));
        var eventName = message.Msg switch
        {
            WmLeftButtonDown => "WM_LBUTTONDOWN",
            WmLeftButtonDoubleClick => "WM_LBUTTONDBLCLK",
            WmLeftButtonUp => "WM_LBUTTONUP",
            _ when isDragMove => "WM_MOUSEMOVE_LEFT_HELD",
            _ => $"WM_{message.Msg:X4}"
        };
        var extraInfo = unchecked((ulong)GetMessageExtraInfo().ToInt64());
        var entry = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Scenario = File.Exists(_scenarioMarkerPath) ? File.ReadAllText(_scenarioMarkerPath).Trim() : "unmarked",
            Event = eventName,
            ScreenX = screenPoint.X,
            ScreenY = screenPoint.Y,
            ClientX = localX,
            ClientY = localY,
            DockInputCount = _inputCount,
            ExtraInfo = $"0x{extraInfo:X16}",
            InputOrigin = extraInfo == SendInputExtraInfo ? "SendInput-synthetic" : "unclassified"
        };
        File.AppendAllText(_eventLogPath, JsonSerializer.Serialize(entry) + Environment.NewLine);
        _lastEvent = eventName;
        _lastPoint = $"{screenPoint.X},{screenPoint.Y}";
        Invalidate(new Rectangle(0, 0, Math.Min(Width, 1800), Math.Max(1, (int)Math.Round(160 * DeviceDpi / 96f))));
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PointNative
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint window, nint insertAfter, int x, int y, int width, int height, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();
}
