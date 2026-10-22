using System.Runtime.InteropServices;
using System.Text.Json;
using GlassDock.App;
using GlassDock.Core;
using Microsoft.UI.Dispatching;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using WinRT.Interop;

namespace GlassDock.PoC.WinUI;

public sealed partial class MainWindow : Window, IDockWindowHost
{
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;
    private const ulong M0SendInputMarker = 0x53444F43;
    private static readonly nint HwndTopmost = new(-1);
    private readonly string _settingsPath = Path.Combine(GetStateRoot(), "winui-poc-settings.json");
    private readonly string _m0InputTracePath = Path.Combine(GetStateRoot(), "m0-input-events.jsonl");
    private readonly string _m0ScenarioMarkerPath = Path.Combine(GetStateRoot(), "m0-current-scenario.txt");
    private readonly nint _hwnd;
    private readonly AppWindow _appWindow;
    private readonly NativeTrayIcon _trayIcon;
    private DockEdge _edge;
    private int _itemCount;
    private double _requestedIconSize;
    private double _effectiveIconSize;

    public MainWindow()
    {
        InitializeComponent();
        LayoutRoot.PointerPressed += (_, e) => TraceM0Input("PointerDown", e, "window");
        LayoutRoot.PointerReleased += (_, e) => TraceM0Input("PointerUp", e, "window");
        LayoutRoot.PointerMoved += (_, e) =>
        {
            if (e.GetCurrentPoint(LayoutRoot).Properties.IsLeftButtonPressed)
                TraceM0Input("PointerMoveLeftHeld", e, "window");
        };
        _hwnd = WindowNative.GetWindowHandle(this);
        var windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        _itemCount = ReadItemCount();
        _requestedIconSize = ReadIconSize();
        _effectiveIconSize = _requestedIconSize;
        Activated += (_, _) => EnsureTopmostWithoutActivation();
        ConfigureWindow();
        _edge = LoadEdge();
        ApplyEdge(_edge);
        _trayIcon = new NativeTrayIcon(_hwnd, () => Activate(), RequestExit);
        Closed += (_, _) => DockLogger.Info("WinUI 3 spike window closed.");
        Closed += (_, _) => _trayIcon.Dispose();
    }

    public ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Activate();
        return ValueTask.CompletedTask;
    }

    public void MoveTo(PixelRect bounds)
    {
        _appWindow.Move(new PointInt32((int)Math.Round(bounds.X), (int)Math.Round(bounds.Y)));
    }

    public void RequestExit() => Close();

    public void QueueTopmostReassertion()
    {
        if (!DispatcherQueue.GetForCurrentThread().TryEnqueue(EnsureTopmostWithoutActivation))
            EnsureTopmostWithoutActivation();
    }

    private void ConfigureWindow()
    {
        var scale = GetDpiForWindow(_hwnd) / 96.0;
        _appWindow.Resize(new SizeInt32((int)Math.Round(580 * scale), (int)Math.Round(132 * scale)));
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            presenter.IsAlwaysOnTop = true;
        }
        EnsureTopmostWithoutActivation();
    }

    private void BuildDockItems()
    {
        ItemsStack.Children.Clear();
        foreach (var item in DemoDockItems.Create(_itemCount))
        {
            var color = ColorHelper.FromArgb(255,
                Convert.ToByte(item.AccentHex.Substring(1, 2), 16),
                Convert.ToByte(item.AccentHex.Substring(3, 2), 16),
                Convert.ToByte(item.AccentHex.Substring(5, 2), 16));
            var card = new Border
            {
                Width = _effectiveIconSize,
                Height = _effectiveIconSize,
                CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(ColorHelper.FromArgb(0x44, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Child = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = item.Glyph,
                            FontFamily = new FontFamily("Segoe UI Symbol"),
                            FontSize = _effectiveIconSize * 0.43,
                            Foreground = new SolidColorBrush(Colors.White),
                            HorizontalAlignment = HorizontalAlignment.Center
                        },
                        new TextBlock
                        {
                            Text = item.Label,
                            FontSize = _effectiveIconSize * 0.14,
                            Foreground = new SolidColorBrush(ColorHelper.FromArgb(244, 255, 255, 255)),
                            HorizontalAlignment = HorizontalAlignment.Center
                        }
                    }
                }
            };

            var button = new Button
            {
                Width = _effectiveIconSize + 6,
                Height = _effectiveIconSize + 6,
                Margin = _edge is DockEdge.Left or DockEdge.Right
                    ? new Thickness(0, 2, 0, 2)
                    : new Thickness(2, 0, 2, 0),
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = card,
                Tag = item
            };
            ToolTipService.SetToolTip(button, item.Label);
            button.PointerEntered += (_, _) => card.RenderTransform = new ScaleTransform { ScaleX = 1.08, ScaleY = 1.08 };
            button.PointerExited += (_, _) => card.RenderTransform = new ScaleTransform { ScaleX = 1, ScaleY = 1 };
            button.Click += DockItem_Click;
            ItemsStack.Children.Add(button);
        }
    }

    private void ApplyEdge(DockEdge edge)
    {
        _edge = edge;
        var vertical = edge is DockEdge.Left or DockEdge.Right;
        var scale = GetDpiForWindow(_hwnd) / 96.0;
        var area = DisplayArea.GetFromWindowId(_appWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        var availableLength = (vertical ? area.Height : area.Width) / scale;
        _effectiveIconSize = Math.Min(_requestedIconSize,
            Math.Max(24, (availableLength - 52) / _itemCount - 10));
        var dockLength = _itemCount * (_effectiveIconSize + 10) + 52;
        var size = vertical
            ? new SizeInt32((int)Math.Round(132 * scale), (int)Math.Round(dockLength * scale))
            : new SizeInt32((int)Math.Round(dockLength * scale), (int)Math.Round(132 * scale));
        _appWindow.Resize(size);
        ItemsStack.Orientation = vertical ? Orientation.Vertical : Orientation.Horizontal;
        BuildDockItems();

        var workArea = new PixelRect(area.X, area.Y, area.Width, area.Height);
        var bounds = DockLayout.Place(workArea, new PixelSize(size.Width, size.Height), edge, 12);
        MoveTo(bounds);
        EnsureTopmostWithoutActivation();
        SaveEdge(edge);
        var dpi = GetDpiForWindow(_hwnd);
        StatusText.Text = $"{_itemCount} placeholders · {edge} · {dpi / 96.0:P0} DPI · " +
            $"{_effectiveIconSize:0.#} DIP icons · WinUI Composition";
        DockLogger.Info($"WinUI 3 spike positioned; edge={edge}; dpi={dpi}.");
    }

    private DockEdge LoadEdge()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<DockSettings>(File.ReadAllText(_settingsPath));
            return Enum.TryParse(settings?.Edge, ignoreCase: true, out DockEdge edge) ? edge : DockEdge.Bottom;
        }
        catch (FileNotFoundException)
        {
            return DockEdge.Bottom;
        }
        catch (DirectoryNotFoundException)
        {
            return DockEdge.Bottom;
        }
        catch (Exception exception)
        {
            DockLogger.Error("Could not load WinUI 3 spike settings; using Bottom.", exception);
            return DockEdge.Bottom;
        }
    }

    private void SaveEdge(DockEdge edge)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
            File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new DockSettings(edge.ToString())));
        }
        catch (Exception exception)
        {
            DockLogger.Error("Could not save WinUI 3 spike settings.", exception);
        }
    }

    private void DockItem_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (sender is Button { Tag: DockItem item })
        {
            StatusText.Text = $"Placeholder selected: {item.Label} · action not connected";
            var scenario = File.Exists(_m0ScenarioMarkerPath) ? File.ReadAllText(_m0ScenarioMarkerPath).Trim() : "unmarked";
            DockLogger.Info($"Placeholder selected: {item.Id}; M0 scenario={scenario}.");
        }
    }

    private void TraceM0Input(string eventName, PointerRoutedEventArgs args, string itemLabel)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GLASSDOCK_M0_INPUT_TRACE"), "1", StringComparison.Ordinal))
            return;

        var point = args.GetCurrentPoint(LayoutRoot).Position;
        var rect = new NativeRect();
        if (!GetWindowRect(_hwnd, out rect))
            return;
        var dpiScale = GetDpiForWindow(_hwnd) / 96.0;
        var extraInfo = unchecked((ulong)GetMessageExtraInfo().ToInt64());
        var entry = new
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Candidate = "WinUI 3",
            Edge = _edge.ToString(),
            Scenario = File.Exists(_m0ScenarioMarkerPath) ? File.ReadAllText(_m0ScenarioMarkerPath).Trim() : "unmarked",
            Event = eventName,
            Item = itemLabel,
            ScreenX = rect.Left + point.X * dpiScale,
            ScreenY = rect.Top + point.Y * dpiScale,
            ExtraInfo = $"0x{extraInfo:X16}",
            InputOrigin = extraInfo == M0SendInputMarker ? "SendInput-synthetic" : "unclassified"
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_m0InputTracePath)!);
        File.AppendAllText(_m0InputTracePath, JsonSerializer.Serialize(entry) + Environment.NewLine);
    }

    private void LayoutRoot_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        var menu = new MenuFlyout();
        var profiles = new MenuFlyoutSubItem { Text = "Sample profile" };
        AddProfileItem(profiles, "8 placeholders", 8);
        AddProfileItem(profiles, "24 placeholders", 24);
        menu.Items.Add(profiles);
        var sizes = new MenuFlyoutSubItem { Text = "Icon size" };
        AddIconSizeItem(sizes, "Small · 44 DIP", 44);
        AddIconSizeItem(sizes, "Default · 58 DIP", 58);
        AddIconSizeItem(sizes, "Large · 72 DIP", 72);
        menu.Items.Add(sizes);
        menu.Items.Add(new MenuFlyoutSeparator());
        AddEdgeItem(menu, "Bottom edge", DockEdge.Bottom);
        AddEdgeItem(menu, "Left edge", DockEdge.Left);
        AddEdgeItem(menu, "Right edge", DockEdge.Right);
        menu.Items.Add(new MenuFlyoutSeparator());
        var exit = new MenuFlyoutItem { Text = "Exit GlassDock" };
        exit.Click += (_, _) => RequestExit();
        menu.Items.Add(exit);
        menu.ShowAt(GlassPanel, e.GetPosition(GlassPanel));
        e.Handled = true;
    }

    private void AddEdgeItem(MenuFlyout menu, string text, DockEdge edge)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) => ApplyEdge(edge);
        menu.Items.Add(item);
    }

    private void AddProfileItem(MenuFlyoutSubItem menu, string text, int count)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            _itemCount = count;
            ApplyEdge(_edge);
        };
        menu.Items.Add(item);
    }

    private void AddIconSizeItem(MenuFlyoutSubItem menu, string text, double size)
    {
        var item = new MenuFlyoutItem { Text = text };
        item.Click += (_, _) =>
        {
            _requestedIconSize = size;
            ApplyEdge(_edge);
        };
        menu.Items.Add(item);
    }

    private sealed record DockSettings(string Edge);

    private static int ReadItemCount() => int.TryParse(
        Environment.GetEnvironmentVariable("GLASSDOCK_POC_ITEM_COUNT"), out var count) && count is 8 or 24
            ? count
            : 8;

    private static double ReadIconSize() => double.TryParse(
        Environment.GetEnvironmentVariable("GLASSDOCK_POC_ICON_SIZE"),
        System.Globalization.NumberStyles.Float,
        System.Globalization.CultureInfo.InvariantCulture,
        out var size) && size is >= 36 and <= 80
            ? size
            : 58;

    private static string GetStateRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GLASSDOCK_POC_STATE_ROOT");
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDock-Win-M0-PoC")
            : configuredRoot;
    }

    private void EnsureTopmostWithoutActivation()
    {
        var result = SetWindowPos(_hwnd, HwndTopmost, 0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpFrameChanged);
        var extendedStyle = GetWindowLongPtr(_hwnd, -20).ToInt64();
        DockLogger.Info($"Topmost update: hwnd=0x{_hwnd.ToInt64():X}; insertAfter={HwndTopmost.ToInt64()}; " +
            $"success={result}; exStyle=0x{extendedStyle:X}; lastError={(result ? 0 : Marshal.GetLastWin32Error())}.");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y,
        int cx, int cy, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hWnd, int index);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetMessageExtraInfo();

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
