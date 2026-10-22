using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GlassDock.App;
using GlassDock.Core;
using WinForms = System.Windows.Forms;
using WpfButton = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfStackPanel = System.Windows.Controls.StackPanel;
using WpfTextBlock = System.Windows.Controls.TextBlock;
using MediaColor = System.Windows.Media.Color;

namespace GlassDock.PoC.Wpf;

public partial class MainWindow : System.Windows.Window, IDockWindowHost
{
    private const int WmNcHitTest = 0x0084;
    private const int HtTransparent = -1;
    private readonly string _settingsPath = Path.Combine(GetStateRoot(), "wpf-poc-settings.json");
    private readonly WinForms.NotifyIcon _trayIcon;
    private DockEdge _edge;
    private HwndSource? _source;
    private int _itemCount;
    private double _requestedIconSize;
    private double _effectiveIconSize;

    public MainWindow()
    {
        InitializeComponent();
        _edge = LoadEdge();
        _itemCount = ReadItemCount();
        _requestedIconSize = ReadIconSize();
        _effectiveIconSize = _requestedIconSize;
        BuildDockItems();
        _trayIcon = new WinForms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "GlassDock-Win M0 WPF spike",
            Visible = true,
            ContextMenuStrip = CreateTrayMenu()
        };
        _trayIcon.DoubleClick += (_, _) => ShowDock();
        Loaded += (_, _) =>
        {
            PlaceAtEdge();
            UpdateDiagnostics();
            DockLogger.Info($"WPF spike launched; edge={_edge}; dpi={GetDpiForWindow(_source!.Handle)}.");
        };
        Closed += (_, _) => _trayIcon.Dispose();
    }

    public ValueTask ShowAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ShowDock();
        return ValueTask.CompletedTask;
    }

    public void MoveTo(PixelRect bounds)
    {
        Left = bounds.X;
        Top = bounds.Y;
    }

    public void RequestExit() => System.Windows.Application.Current.Shutdown();

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _source = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
        _source.AddHook(WindowProc);
    }

    private void BuildDockItems()
    {
        ItemsStack.Children.Clear();
        foreach (var item in DemoDockItems.Create(_itemCount))
        {
            var color = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(item.AccentHex)!;
            var card = new System.Windows.Controls.Border
            {
                Width = _effectiveIconSize,
                Height = _effectiveIconSize,
                Margin = new System.Windows.Thickness(3, 0, 3, 0),
                CornerRadius = new System.Windows.CornerRadius(17),
                Background = new SolidColorBrush(color),
                BorderBrush = new SolidColorBrush(MediaColor.FromArgb(0x44, 255, 255, 255)),
                BorderThickness = new System.Windows.Thickness(1),
                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5),
                RenderTransform = new ScaleTransform(1, 1),
                Child = new WpfStackPanel
                {
                    VerticalAlignment = System.Windows.VerticalAlignment.Center,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                    Children =
                    {
                        new WpfTextBlock
                        {
                            Text = item.Glyph,
                            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Symbol"),
                            FontSize = _effectiveIconSize * 0.43,
                            Foreground = System.Windows.Media.Brushes.White,
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                        },
                        new WpfTextBlock
                        {
                            Text = item.Label,
                            FontFamily = new System.Windows.Media.FontFamily("Segoe UI"),
                            FontSize = _effectiveIconSize * 0.14,
                            Foreground = new SolidColorBrush(MediaColor.FromArgb(244, 255, 255, 255)),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Center
                        }
                    }
                }
            };

            var button = new WpfButton
            {
                Width = _effectiveIconSize + 6,
                Height = _effectiveIconSize + 6,
                Padding = new System.Windows.Thickness(0),
                Margin = _edge is DockEdge.Left or DockEdge.Right
                    ? new System.Windows.Thickness(0, 2, 0, 2)
                    : new System.Windows.Thickness(2, 0, 2, 0),
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new System.Windows.Thickness(0),
                Content = card,
                ToolTip = item.Label,
                Tag = item
            };
            button.MouseEnter += (_, _) => AnimateScale(card, 1.1);
            button.MouseLeave += (_, _) => AnimateScale(card, 1.0);
            button.Click += DockItem_Click;
            ItemsStack.Children.Add(button);
        }
    }

    private static void AnimateScale(System.Windows.FrameworkElement element, double target)
    {
        if (element.RenderTransform is not ScaleTransform scale)
            return;
        var duration = TimeSpan.FromMilliseconds(130);
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(target, duration));
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(target, duration));
    }

    private WinForms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add("Show Dock", null, (_, _) => ShowDock());
        menu.Items.Add(new WinForms.ToolStripSeparator());
        menu.Items.Add("Exit GlassDock", null, (_, _) => RequestExit());
        return menu;
    }

    private void ShowDock()
    {
        if (!IsVisible)
            Show();
        Activate();
    }

    private void PlaceAtEdge()
    {
        var vertical = _edge is DockEdge.Left or DockEdge.Right;
        var dpi = VisualTreeHelper.GetDpi(this);
        var area = SystemParameters.WorkArea;
        var availableLength = vertical ? area.Height : area.Width;
        _effectiveIconSize = Math.Min(_requestedIconSize,
            Math.Max(24, (availableLength - 52) / _itemCount - 10));
        var dockLength = _itemCount * (_effectiveIconSize + 10) + 52;
        Width = vertical ? 132 : dockLength;
        Height = vertical ? dockLength : 132;
        BuildDockItems();

        var physicalWorkArea = new PixelRect(area.Left * dpi.DpiScaleX, area.Top * dpi.DpiScaleY,
            area.Width * dpi.DpiScaleX, area.Height * dpi.DpiScaleY);
        var physicalSize = new PixelSize(Width * dpi.DpiScaleX, Height * dpi.DpiScaleY);
        var bounds = DockLayout.Place(physicalWorkArea, physicalSize, _edge, 12 * dpi.DpiScaleX);
        MoveTo(new PixelRect(bounds.X / dpi.DpiScaleX, bounds.Y / dpi.DpiScaleY,
            bounds.Width / dpi.DpiScaleX, bounds.Height / dpi.DpiScaleY));

        ItemsStack.Orientation = vertical ? WpfOrientation.Vertical : WpfOrientation.Horizontal;
        SaveEdge(_edge);
        UpdateDiagnostics();
    }

    private void UpdateDiagnostics()
    {
        var dpi = _source is null ? 96u : GetDpiForWindow(_source.Handle);
        StatusText.Text = $"{_itemCount} placeholders · {_edge} · {dpi / 96.0:P0} DPI · " +
            $"{_effectiveIconSize:0.#} DIP icons · WPF / DWM";
    }

    private DockEdge LoadEdge()
    {
        try
        {
            var text = File.ReadAllText(_settingsPath);
            var settings = JsonSerializer.Deserialize<DockSettings>(text);
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
            DockLogger.Error("Could not load WPF spike settings; using Bottom.", exception);
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
            DockLogger.Error("Could not save WPF spike settings.", exception);
        }
    }

    private void SetEdge(DockEdge edge)
    {
        _edge = edge;
        PlaceAtEdge();
    }

    private void SetBottom_Click(object sender, System.Windows.RoutedEventArgs e) => SetEdge(DockEdge.Bottom);
    private void SetLeft_Click(object sender, System.Windows.RoutedEventArgs e) => SetEdge(DockEdge.Left);
    private void SetRight_Click(object sender, System.Windows.RoutedEventArgs e) => SetEdge(DockEdge.Right);
    private void SetEight_Click(object sender, System.Windows.RoutedEventArgs e) => SetItemCount(8);
    private void SetTwentyFour_Click(object sender, System.Windows.RoutedEventArgs e) => SetItemCount(24);
    private void SetSmall_Click(object sender, System.Windows.RoutedEventArgs e) => SetIconSize(44);
    private void SetDefaultSize_Click(object sender, System.Windows.RoutedEventArgs e) => SetIconSize(58);
    private void SetLarge_Click(object sender, System.Windows.RoutedEventArgs e) => SetIconSize(72);
    private void Exit_Click(object sender, System.Windows.RoutedEventArgs e) => RequestExit();

    private void SetItemCount(int count)
    {
        _itemCount = count;
        PlaceAtEdge();
    }

    private void SetIconSize(double size)
    {
        _requestedIconSize = size;
        PlaceAtEdge();
    }

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

    private void DockItem_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is WpfButton { Tag: DockItem item })
        {
            StatusText.Text = $"Placeholder selected: {item.Label} · action not connected";
            DockLogger.Info($"Placeholder selected: {item.Id}.");
        }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmNcHitTest)
        {
            var packed = lParam.ToInt64();
            var screenPoint = new System.Windows.Point(unchecked((short)(packed & 0xFFFF)),
                unchecked((short)((packed >> 16) & 0xFFFF)));
            var point = PointFromScreen(screenPoint);
            if (!IsInsideRoundedPanel(point))
            {
                handled = true;
                return (nint)HtTransparent;
            }
        }
        return 0;
    }

    private bool IsInsideRoundedPanel(System.Windows.Point point)
    {
        var x = point.X - GlassPanel.Margin.Left;
        var y = point.Y - GlassPanel.Margin.Top;
        var width = GlassPanel.ActualWidth;
        var height = GlassPanel.ActualHeight;
        var radius = Math.Min(GlassPanel.CornerRadius.TopLeft, Math.Min(width, height) / 2);
        if (x < 0 || y < 0 || x > width || y > height)
            return false;
        var cx = x < radius ? radius : x > width - radius ? width - radius : x;
        var cy = y < radius ? radius : y > height - radius ? height - radius : y;
        return (x - cx) * (x - cx) + (y - cy) * (y - cy) <= radius * radius;
    }

    private sealed record DockSettings(string Edge);

    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint hWnd);
}
