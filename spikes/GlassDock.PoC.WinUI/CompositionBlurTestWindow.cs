using System.Text.Json;
using GlassDock.App;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace GlassDock.PoC.WinUI;

internal sealed class CompositionBlurTestWindow : Window
{
    private readonly string _role;
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;

    public CompositionBlurTestWindow(bool includeBlurSurface, string role)
    {
        _role = role;
        Title = $"GlassDock M0 Composition probe · {role}";
        var root = new Canvas { Background = new SolidColorBrush(Colors.Black) };
        Content = root;

        _hwnd = WindowNative.GetWindowHandle(this);
        _appWindow = AppWindow.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_hwnd));
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            // This controlled, opaque pattern surface masks unrelated desktop content during the authorized WGC run.
            presenter.IsAlwaysOnTop = true;
        }

        var display = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary);
        var bounds = display.OuterBounds;
        var scale = GetDpiForWindow(_hwnd) / 96.0;
        var monitorWidthDip = bounds.Width / scale;
        var monitorHeightDip = bounds.Height / scale;
        var localizedBackground = string.Equals(_role, "controlled-background", StringComparison.Ordinal);
        var widthDip = localizedBackground ? Math.Min(1000, monitorWidthDip) : monitorWidthDip;
        var heightDip = localizedBackground ? Math.Min(400, monitorHeightDip) : monitorHeightDip;
        var widthPx = (int)Math.Round(widthDip * scale);
        var heightPx = (int)Math.Round(heightDip * scale);
        var windowBounds = new RectInt32(
            bounds.X + (bounds.Width - widthPx) / 2,
            bounds.Y + bounds.Height - heightPx,
            widthPx,
            heightPx);
        _appWindow.Resize(new SizeInt32(windowBounds.Width, windowBounds.Height));
        _appWindow.Move(new PointInt32(windowBounds.X, windowBounds.Y));
        root.Width = widthDip;
        root.Height = heightDip;

        AddHighContrastPattern(root, widthDip, heightDip);
        CaptureBounds? blurBoundsPx = null;
        if (includeBlurSurface)
            blurBoundsPx = AddBlurControl(root, widthDip, heightDip, bounds, scale);

        WriteWindowStatus(new
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Role = _role,
            Hwnd = $"0x{_hwnd.ToInt64():X}",
            BoundsPx = new { windowBounds.X, windowBounds.Y, windowBounds.Width, windowBounds.Height },
            Dpi = GetDpiForWindow(_hwnd),
            UsesLayeredWindow = false,
            AlwaysOnTop = true,
            HasIndependentDesktopWindowHost = true,
            IncludesBlurControl = includeBlurSurface,
            BackgroundCoverage = localizedBackground ? "Localized 1000x400 DIP maximum region at the bottom center; not full-screen" : "Monitor bounds",
            BlurControlBoundsPx = blurBoundsPx,
            Pattern = "Alternating 20 DIP black/white vertical stripes"
        }, $"{_role}-window.json");

        DockLogger.Info($"Composition probe window ready; role={_role}; hwnd=0x{_hwnd.ToInt64():X}; " +
                        $"bounds={windowBounds.X},{windowBounds.Y},{windowBounds.Width},{windowBounds.Height}; dpi={GetDpiForWindow(_hwnd)}; " +
                        $"layered=false; blurControl={includeBlurSurface}.");
    }

    private void AddHighContrastPattern(Canvas root, double widthDip, double heightDip)
    {
        const double stripeWidthDip = 20;
        var stripeCount = (int)Math.Ceiling(widthDip / stripeWidthDip);
        for (var index = 0; index < stripeCount; index++)
        {
            var left = index * stripeWidthDip;
            var stripe = new Rectangle
            {
                Width = Math.Min(stripeWidthDip, widthDip - left),
                Height = heightDip,
                Fill = new SolidColorBrush(index % 2 == 0 ? Colors.White : Colors.Black)
            };
            Canvas.SetLeft(stripe, left);
            root.Children.Add(stripe);
        }
    }

    private CaptureBounds AddBlurControl(Canvas root, double widthDip, double heightDip,
        Windows.Graphics.RectInt32 monitorBounds, double scale)
    {
        var panelWidth = Math.Min(560, widthDip - 80);
        var panelHeight = Math.Min(280, heightDip - 80);
        var leftDip = (widthDip - panelWidth) / 2;
        var topDip = (heightDip - panelHeight) / 2;
        var panel = new Border
        {
            Width = panelWidth,
            Height = panelHeight,
            CornerRadius = new CornerRadius(48),
            BorderThickness = new Thickness(2),
            BorderBrush = new SolidColorBrush(Color.FromArgb(230, 255, 255, 255)),
            Background = new CompositionBackdropBlurBrush(28, _role),
            Child = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Spacing = 12,
                Children =
                {
                    new TextBlock
                    {
                        Text = "COMPOSITION BACKDROP BLUR CONTROL",
                        FontSize = 24,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(Colors.Red),
                        HorizontalAlignment = HorizontalAlignment.Center
                    },
                    new TextBlock
                    {
                        Text = "Compare sharp 20 DIP stripes outside and softened stripes inside this rounded surface.",
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Colors.Red),
                        TextWrapping = TextWrapping.Wrap,
                        TextAlignment = TextAlignment.Center,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                }
            }
        };
        Canvas.SetLeft(panel, leftDip);
        Canvas.SetTop(panel, topDip);
        root.Children.Add(panel);
        return new CaptureBounds(
            monitorBounds.X + (int)Math.Round(leftDip * scale),
            monitorBounds.Y + (int)Math.Round(topDip * scale),
            (int)Math.Round(panelWidth * scale),
            (int)Math.Round(panelHeight * scale));
    }

    private static void WriteWindowStatus(object value, string fileName)
    {
        var root = Environment.GetEnvironmentVariable("GLASSDOCK_M0_COMPOSITION_STATUS_ROOT");
        if (string.IsNullOrWhiteSpace(root) || !System.IO.Path.IsPathFullyQualified(root) || !root.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
            return;

        Directory.CreateDirectory(root);
        File.WriteAllText(System.IO.Path.Combine(root, fileName), JsonSerializer.Serialize(value,
            new JsonSerializerOptions { WriteIndented = true }));
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hwnd);

    private sealed record CaptureBounds(int Left, int Top, int Width, int Height);
}
