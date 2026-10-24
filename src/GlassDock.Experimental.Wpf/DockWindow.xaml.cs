using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Interop;
using GlassDock.App;
using GlassDock.Core;

namespace GlassDock.Experimental.Wpf;

/// <summary>
/// Replaceable WPF experiment. All product state changes go through DockApplicationSession;
/// this host has no verified blur, non-activation, full-screen, or multi-monitor behavior.
/// </summary>
public partial class DockWindow : Window
{
    private const double IconSize = 48;
    private readonly DockApplicationSession _session;
    private readonly DockIconResolver _iconResolver;
    private readonly string _loadDiagnostic;
    private readonly ExperimentalDockDiagnostics? _diagnostics;
    private readonly Stopwatch _startupTimer;
    private readonly Dictionary<DockItemId, ImageSource?> _iconCache = [];
    private readonly Dictionary<DockItemId, IconLoadState> _iconLoadStates = [];
    private readonly Dictionary<DockItemId, IconVisual> _iconVisuals = [];
    private DockVisualLayout? _visualLayout;
    private string _lastStatus = "Preview only · right-click for controls · Alt+F4 to exit";
    private bool _visualLayoutVerified;
    private CancellationTokenSource? _catalogLoadCancellation;
    private CancellationTokenSource? _iconLoadCancellation;
    private bool _vertical;
    private bool _layoutPrepared;
    private bool _visibleMilestoneRecorded;
    private bool _contentRenderedMilestoneRecorded;
    private bool _initialIconLoadStarted;
    private bool _iconRefreshRequested;

    public DockWindow(
        DockApplicationSession session,
        string loadDiagnostic,
        ExperimentalDockDiagnostics? diagnostics,
        Stopwatch startupTimer,
        DockIconResolver iconResolver)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loadDiagnostic = loadDiagnostic;
        _diagnostics = diagnostics;
        _startupTimer = startupTimer ?? throw new ArgumentNullException(nameof(startupTimer));
        _iconResolver = iconResolver ?? throw new ArgumentNullException(nameof(iconResolver));
        SourceInitialized += Window_SourceInitialized;
        IsVisibleChanged += Window_IsVisibleChanged;
        LayoutUpdated += Window_LayoutUpdated;
        ContentRendered += Window_ContentRendered;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Close, (_, _) => Close()));
        InputBindings.Add(new KeyBinding(ApplicationCommands.Close, new KeyGesture(Key.F4, ModifierKeys.Alt)));
        SectionsScrollViewer.PreviewMouseWheel += SectionsScrollViewer_PreviewMouseWheel;
        RenderState();
    }

    private void Window_SourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        _diagnostics?.Record("window-hwnd-created", _startupTimer.Elapsed, new
        {
            Hwnd = ToHwndString(handle),
            ProcessId = Environment.ProcessId,
            HandleNonZero = handle != IntPtr.Zero
        });
    }

    private void Window_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible && !_visibleMilestoneRecorded)
        {
            _visibleMilestoneRecorded = true;
            _diagnostics?.Record("window-visible", _startupTimer.Elapsed, GetWindowMilestoneDetails());
        }
    }

    private void Window_LayoutUpdated(object? sender, EventArgs e)
    {
        TryRecordLayoutPrepared();
    }

    private void Window_ContentRendered(object? sender, EventArgs e)
    {
        TryRecordLayoutPrepared();
        if (!_contentRenderedMilestoneRecorded)
        {
            _contentRenderedMilestoneRecorded = true;
            _diagnostics?.Record("window-content-rendered", _startupTimer.Elapsed, GetWindowMilestoneDetails());
        }

        if (_initialIconLoadStarted || !IsLoaded)
            return;
        _initialIconLoadStarted = true;
        StartCatalogLoad();
        StartStateIconLoading();
        if (!string.IsNullOrWhiteSpace(_loadDiagnostic)
            && !string.Equals(_loadDiagnostic, "DefaultedMissing", StringComparison.Ordinal))
            StatusText($"Configuration load: {_loadDiagnostic}");
    }

    private void TryRecordLayoutPrepared()
    {
        if (_layoutPrepared || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        _layoutPrepared = true;
        _visualLayoutVerified = _visualLayout is not null
            && Math.Abs(ActualWidth - _visualLayout.WindowWidthDip) <= 1
            && Math.Abs(ActualHeight - _visualLayout.WindowHeightDip) <= 1
            && Math.Abs(Backplate.ActualWidth - _visualLayout.BackplateBounds.Width) <= 1
            && Math.Abs(Backplate.ActualHeight - _visualLayout.BackplateBounds.Height) <= 1
            && Math.Abs(SectionsScrollViewer.ActualWidth - _visualLayout.ViewportSize.Width) <= 1
            && Math.Abs(SectionsScrollViewer.ActualHeight - _visualLayout.ViewportSize.Height) <= 1;
        LayoutUpdated -= Window_LayoutUpdated;
        _diagnostics?.Record("window-layout-prepared", _startupTimer.Elapsed, GetWindowMilestoneDetails());
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _diagnostics?.Record("window-wpf-loaded-event", _startupTimer.Elapsed, GetWindowMilestoneDetails());
    }

    private object GetWindowMilestoneDetails()
    {
        var handle = new WindowInteropHelper(this).Handle;
        var dpi = VisualTreeHelper.GetDpi(this);
        return new
        {
            Hwnd = ToHwndString(handle),
            ProcessId = Environment.ProcessId,
            Visible = IsVisible,
            LayoutPrepared = _layoutPrepared,
            ContentRendered = _contentRenderedMilestoneRecorded,
            LeftDip = Left,
            TopDip = Top,
            WidthDip = ActualWidth,
            HeightDip = ActualHeight,
            VisualLayoutVerified = _visualLayoutVerified,
            BackplateWidthDip = Backplate.ActualWidth,
            BackplateHeightDip = Backplate.ActualHeight,
            ViewportWidthDip = SectionsScrollViewer.ActualWidth,
            ViewportHeightDip = SectionsScrollViewer.ActualHeight,
            DpiScaleX = dpi.DpiScaleX,
            DpiScaleY = dpi.DpiScaleY,
            IconSizeDip = IconSize,
            IconPixelsAtWindowDpiX = IconSize * dpi.DpiScaleX,
            IconPixelsAtWindowDpiY = IconSize * dpi.DpiScaleY
        };
    }

    private static string ToHwndString(IntPtr handle) => $"0x{handle.ToInt64():X}";

    private void StartCatalogLoad()
    {
        if (_catalogLoadCancellation is not null || !IsLoaded)
            return;

        var cancellation = new CancellationTokenSource();
        _catalogLoadCancellation = cancellation;
        _ = LoadCatalogAsync(cancellation);
    }

    private async Task LoadCatalogAsync(CancellationTokenSource cancellation)
    {
        var timer = Stopwatch.StartNew();
        StatusText("Loading Windows application catalog…");
        try
        {
            var result = await _session.RefreshCatalogAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested || !IsLoaded)
                return;
            if (result.Status == DockApplicationCatalogStatus.Succeeded)
                StatusText($"{result.Applications.Count} applications discovered · none were pinned automatically");
            else
                StatusText($"Application discovery unavailable ({result.DiagnosticCode})");
            _diagnostics?.Record("catalog-load-completed", timer.Elapsed, new
            {
                ProcessId = Environment.ProcessId,
                Status = result.Status.ToString(),
                Count = result.Applications.Count,
                result.DuplicateCount,
                result.IssueCount,
                result.DiagnosticCode
            });
        }
        catch (OperationCanceledException)
        {
            _diagnostics?.Record("catalog-load-cancelled", timer.Elapsed);
        }
        catch (Exception exception)
        {
            if (IsLoaded)
                StatusText($"Application discovery failed ({exception.GetType().Name})");
            _diagnostics?.Record("catalog-load-failed", timer.Elapsed, new { Error = exception.GetType().Name });
        }
        finally
        {
            if (ReferenceEquals(_catalogLoadCancellation, cancellation))
                _catalogLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _catalogLoadCancellation?.Cancel();
        _iconLoadCancellation?.Cancel();
        _iconCache.Clear();
        _iconLoadStates.Clear();
        _iconVisuals.Clear();
        SectionsHost.Children.Clear();
    }

    private void ExitPreview() => Close();

    private void AddAppsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow(_session, _diagnostics) { Owner = this };
        _ = picker.ShowDialog();
        RenderState();
        StartStateIconLoading();
    }

    private void Backplate_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        var menu = new ContextMenu();
        var mode = new MenuItem
        {
            Header = _session.ExecutionMode == DockExecutionMode.PreviewOnly
                ? "Preview only · app and file actions disabled"
                : "Experimental shell commands enabled",
            IsEnabled = false
        };
        menu.Items.Add(mode);
        var addApps = new MenuItem { Header = "Add Applications…", InputGestureText = "Ctrl+O" };
        addApps.Click += AddAppsButton_Click;
        menu.Items.Add(addApps);
        var position = new MenuItem { Header = "Position" };
        AddEdgeMenuItem(position, "Bottom", DockEdge.Bottom);
        AddEdgeMenuItem(position, "Left", DockEdge.Left);
        AddEdgeMenuItem(position, "Right", DockEdge.Right);
        menu.Items.Add(position);
        if (!string.IsNullOrWhiteSpace(_lastStatus))
            menu.Items.Add(new MenuItem { Header = _lastStatus, IsEnabled = false, MaxWidth = 420 });
        menu.Items.Add(new Separator());
        var exit = new MenuItem { Header = "Exit Preview", InputGestureText = "Alt+F4" };
        exit.Click += (_, _) => ExitPreview();
        menu.Items.Add(exit);
        menu.PlacementTarget = Backplate;
        menu.Placement = PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void AddEdgeMenuItem(ItemsControl menu, string label, DockEdge edge)
    {
        var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = _session.State.Preferences.Edge == edge };
        item.Click += (_, _) =>
        {
            var result = _session.SetEdge(edge);
            if (!result.Succeeded)
            {
                StatusText($"Position was not saved ({result.DiagnosticCode})");
                return;
            }
            RenderState();
            StatusText($"Dock position: {edge}");
        };
        menu.Items.Add(item);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.O)
        {
            e.Handled = true;
            AddAppsButton_Click(sender, new RoutedEventArgs());
        }
        else if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.Q)
        {
            e.Handled = true;
            ExitPreview();
        }
    }

    private void SectionsScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = -e.Delta;
        if (_vertical)
            SectionsScrollViewer.ScrollToVerticalOffset(SectionsScrollViewer.VerticalOffset + delta);
        else
            SectionsScrollViewer.ScrollToHorizontalOffset(SectionsScrollViewer.HorizontalOffset + delta);
        e.Handled = true;
    }

    private void ApplyEdgeLayout()
    {
        var edge = _session.State.Preferences.Edge;
        _vertical = edge is DockEdge.Left or DockEdge.Right;
        var workArea = SystemParameters.WorkArea;
        var availableAxis = _vertical ? workArea.Height - 16 : workArea.Width - 16;
        var layout = DockVisualGeometry.Measure(
            edge,
            _session.State.PinnedApplications.Count,
            _session.State.Preferences.ShowRecentApplications ? _session.State.VisibleRecentApplications.Count : 0,
            _session.State.FilesAndFolders.Count,
            IconSize,
            availableAxis);
        _visualLayout = layout;
        Width = layout.WindowWidthDip;
        Height = layout.WindowHeightDip;
        MinWidth = 100;
        MinHeight = 100;
        MaxWidth = workArea.Width;
        MaxHeight = workArea.Height;
        Backplate.Width = layout.BackplateBounds.Width;
        Backplate.Height = layout.BackplateBounds.Height;
        Backplate.CornerRadius = new CornerRadius(Math.Min(38, layout.BackplateBounds.Height / 2));
        SectionsScrollViewer.Width = layout.ViewportSize.Width;
        SectionsScrollViewer.Height = layout.ViewportSize.Height;
        SectionsHost.Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal;
        if (_vertical)
        {
            SectionsScrollViewer.PanningMode = PanningMode.VerticalOnly;
            Left = edge == DockEdge.Left ? workArea.Left + 8 : workArea.Right - Width - 8;
            Top = workArea.Top + (workArea.Height - Height) / 2;
        }
        else
        {
            SectionsScrollViewer.PanningMode = PanningMode.HorizontalOnly;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Bottom - Height - 8;
        }
        _diagnostics?.Record("dock-visual-geometry", TimeSpan.Zero, new
        {
            Edge = edge.ToString(),
            IconSizeDip = IconSize,
            layout.WindowWidthDip,
            layout.WindowHeightDip,
            BackplateWidthDip = layout.BackplateBounds.Width,
            BackplateHeightDip = layout.BackplateBounds.Height,
            BackplateBounds = layout.BackplateBounds,
            layout.ViewportSize,
            layout.ContentExtent,
            layout.VisibleSectionCount,
            IconCount = layout.Items.Count,
            SeparatorCount = layout.Separators.Count,
            layout.IsScrollable,
            IconBounds = layout.Items.Select(item => item.IconBounds).ToArray()
        });
    }

    private void RenderState()
    {
        _vertical = _session.State.Preferences.Edge is DockEdge.Left or DockEdge.Right;
        SectionsHost.Children.Clear();
        _iconVisuals.Clear();
        AddSection(DockSection.PinnedApplications, _session.State.PinnedApplications);

        if (_session.State.Preferences.ShowRecentApplications)
            AddSection(DockSection.RecentApplications, _session.State.VisibleRecentApplications);

        AddSection(DockSection.FilesAndFolders, _session.State.FilesAndFolders);
        ApplyEdgeLayout();
    }

    private void AddSection(
        DockSection section,
        IReadOnlyList<DockProjectItem> items)
    {
        if (items.Count == 0)
            return;
        if (SectionsHost.Children.Count > 0)
            SectionsHost.Children.Add(CreateSeparator());

        var group = new StackPanel
        {
            Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
            Margin = _vertical ? new Thickness(0, 3, 0, 3) : new Thickness(3, 0, 3, 0)
        };
        var tiles = new StackPanel
        {
            Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal
        };
        foreach (var projectItem in items)
            tiles.Children.Add(CreateTile(projectItem, section, items));
        group.Children.Add(tiles);

        SectionsHost.Children.Add(group);
    }

    private UIElement CreateSeparator() => _vertical
        ? new Border
        {
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4),
            Width = IconSize * 0.72,
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        }
        : new Border
        {
            Width = 1,
            Height = IconSize * 0.72,
            Margin = new Thickness(4, 0, 4, 0),
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };

    private Button CreateTile(DockProjectItem item, DockSection section, IReadOnlyList<DockProjectItem> sectionItems)
    {
        var image = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Stretch = Stretch.Uniform,
            Source = _iconCache.GetValueOrDefault(item.Id),
            Visibility = _iconCache.GetValueOrDefault(item.Id) is null ? Visibility.Collapsed : Visibility.Visible
        };
        var fallback = new TextBlock
        {
            Text = _iconLoadStates.GetValueOrDefault(item.Id) == IconLoadState.Loading
                ? "…"
                : MakeFallbackGlyph(item.DisplayName),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = image.Source is null ? Visibility.Visible : Visibility.Collapsed
        };
        var iconGrid = new Grid
        {
            Width = IconSize + 12,
            Height = IconSize + 12
        };
        iconGrid.Children.Add(image);
        iconGrid.Children.Add(fallback);
        _iconVisuals[item.Id] = new IconVisual(image, fallback, item.DisplayName);
        var button = new Button
        {
            Style = (Style)FindResource("DockTileButton"),
            Width = IconSize + 12,
            Height = IconSize + 12,
            Margin = new Thickness(2),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Content = iconGrid,
            Focusable = true,
            ToolTip = $"{item.DisplayName} · {section} · {(_session.ExecutionMode == DockExecutionMode.PreviewOnly ? "Preview only; commands are blocked" : "Experimental command mode")}",
            Cursor = Cursors.Hand,
            AllowDrop = section != DockSection.RecentApplications,
            Tag = new DockTileDragData(item.Id, section)
        };
        AutomationProperties.SetName(button, $"{item.DisplayName}, {section}");
        AutomationProperties.SetHelpText(button, "Enter or Space launches or opens. Delete removes this Dock reference. Use Ctrl+Arrow to reorder.");
        button.Click += async (_, _) =>
        {
            if (button.Tag is DockTileDragData { DragStarted: true } drag)
            {
                button.Tag = drag with { DragStarted = false };
                return;
            }
            await ExecuteItemAsync(item);
        };
        button.KeyDown += (_, args) => HandleTileKeyDown(item, section, sectionItems, args);
        button.MouseRightButtonUp += (_, args) =>
        {
            args.Handled = true;
            var menu = new ContextMenu();
            var remove = new MenuItem { Header = "Remove from Dock" };
            remove.Click += (_, _) => RemoveReference(item.Id);
            menu.Items.Add(remove);
            menu.PlacementTarget = button;
            menu.Placement = PlacementMode.MousePoint;
            menu.IsOpen = true;
        };
        button.PreviewMouseLeftButtonDown += Tile_PreviewMouseLeftButtonDown;
        button.PreviewMouseMove += Tile_PreviewMouseMove;
        button.DragOver += Tile_DragOver;
        button.Drop += Tile_Drop;
        return button;
    }

    private void StartStateIconLoading()
    {
        if (!IsLoaded)
            return;
        if (_iconLoadCancellation is not null)
        {
            _iconRefreshRequested = true;
            return;
        }

        var cancellation = new CancellationTokenSource();
        _iconLoadCancellation = cancellation;
        _ = LoadStateIconsAsync(cancellation);
    }

    private async Task LoadStateIconsAsync(CancellationTokenSource cancellation)
    {
        var token = cancellation.Token;
        var items = _session.State.PinnedApplications
            .Concat(_session.State.VisibleRecentApplications)
            .Concat(_session.State.FilesAndFolders)
            .Where(item => !_iconCache.ContainsKey(item.Id))
            .ToArray();

        var timer = Stopwatch.StartNew();
        _diagnostics?.Record("state-icon-load-started", timer.Elapsed, new { Requested = items.Length, MaxConcurrentLoads = 2 });
        foreach (var item in items)
        {
            _iconLoadStates[item.Id] = IconLoadState.Loading;
            UpdateIconVisual(item.Id, null, IconLoadState.Loading);
        }

        var processed = 0;
        try
        {
            await DockIconLoadBatch.RunAsync(
                items,
                maxConcurrency: 2,
                async (item, loadToken) =>
                {
                    await LoadStateIconAsync(item, loadToken);
                    Interlocked.Increment(ref processed);
                },
                token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            _diagnostics?.Record("state-icon-load-cancelled", timer.Elapsed, new { Requested = items.Length, Processed = processed });
        }
        catch (Exception exception)
        {
            _diagnostics?.Record("state-icon-load-failed", timer.Elapsed, new
            {
                Requested = items.Length,
                Processed = processed,
                Error = exception.GetType().Name
            });
        }
        finally
        {
            if (ReferenceEquals(_iconLoadCancellation, cancellation))
                _iconLoadCancellation = null;
            cancellation.Dispose();
            if (IsLoaded && _iconRefreshRequested)
            {
                _iconRefreshRequested = false;
                StartStateIconLoading();
            }
            _diagnostics?.Record("state-icon-load-completed", timer.Elapsed, new
            {
                Requested = items.Length,
                Processed = processed,
                Failed = items.Count(item => _iconLoadStates.GetValueOrDefault(item.Id) == IconLoadState.Failed)
            });
        }
    }

    private async Task LoadStateIconAsync(DockProjectItem item, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested || !IsLoaded)
            return;

        var timer = Stopwatch.StartNew();
        _diagnostics?.Record("dock-icon-resolution-started", timer.Elapsed, new
        {
            ItemId = item.Id.ToString(),
            item.DisplayName,
            ProcessId = Environment.ProcessId
        });
        DockIconResolutionResult? result = null;
        var finalSource = (DockIconResolutionSource?)null;
        var finalStatus = DockApplicationIconStatus.Failed;
        var finalVisualState = (DockIconVisualState?)null;
        string? diagnosticCode = null;
        string? decodeFailure = null;
        ImageSource? image = null;
        try
        {
            result = await _iconResolver.ResolveAsync(item, GetIconTheme(), cancellationToken);
            if (cancellationToken.IsCancellationRequested || !IsLoaded)
                return;
            finalSource = result.Source;
            finalStatus = result.Status;
            finalVisualState = result.VisualState;
            diagnosticCode = result.DiagnosticCode;
            if (result.Status == DockApplicationIconStatus.Found && result.Asset is not null)
            {
                try
                {
                    image = DecodeIconAsset(result.Asset);
                }
                catch (Exception) when (result.Source != DockIconResolutionSource.WindowsShell)
                {
                    var fallback = await _iconResolver.ResolveWindowsShellFallbackAsync(item, cancellationToken);
                    finalSource = fallback.Source;
                    finalStatus = fallback.Status;
                    finalVisualState ??= fallback.VisualState;
                    diagnosticCode = fallback.DiagnosticCode;
                    if (fallback.Status == DockApplicationIconStatus.Found && fallback.Asset is not null)
                        image = DecodeIconAsset(fallback.Asset);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || !IsLoaded)
        {
            return;
        }
        catch (Exception)
        {
            finalStatus = DockApplicationIconStatus.Failed;
            decodeFailure = "icon-load-exception";
        }

        if (!IsLoaded)
            return;
        _iconCache[item.Id] = image;
        var loadState = image is null ? IconLoadState.Failed : IconLoadState.Loaded;
        _iconLoadStates[item.Id] = loadState;
        UpdateIconVisual(item.Id, image, loadState);
        _diagnostics?.Record("dock-icon-resolved", timer.Elapsed, new
        {
            ItemId = item.Id.ToString(),
            item.DisplayName,
            ProcessId = Environment.ProcessId,
            Role = result?.Role?.ToString(),
            VisualState = finalVisualState?.ToString(),
            Source = finalSource?.ToString(),
            Status = finalStatus.ToString(),
            LoadState = loadState.ToString(),
            Decoded = image is not null,
            DiagnosticCode = diagnosticCode,
            DecodeFailure = decodeFailure
        });
    }

    private void UpdateIconVisual(DockItemId id, ImageSource? imageSource, IconLoadState state)
    {
        if (!_iconVisuals.TryGetValue(id, out var visual))
            return;

        visual.Image.Source = imageSource;
        visual.Image.Visibility = imageSource is null ? Visibility.Collapsed : Visibility.Visible;
        visual.Fallback.Text = state == IconLoadState.Loading
            ? "…"
            : MakeFallbackGlyph(visual.DisplayName);
        visual.Fallback.Visibility = imageSource is null ? Visibility.Visible : Visibility.Collapsed;
        visual.Fallback.ToolTip = state switch
        {
            IconLoadState.Loading => "Loading icon…",
            IconLoadState.Failed => "Icon unavailable; showing a safe fallback",
            _ => null
        };
    }

    private async Task ExecuteItemAsync(DockProjectItem item)
    {
        var result = item.Kind is DockProjectItemKind.File or DockProjectItemKind.Folder
            ? await _session.ExecuteAsync(item.Id, DockCommandKind.Open)
            : await _session.InvokeApplicationAsync(item.Id);
        if (result.Outcome == DockCommandOutcome.BlockedByPolicy)
        {
            StatusText("Preview only: Launch, Activate, and Open are disabled at the Shell command boundary.");
            return;
        }

        StatusText(result.Outcome == DockCommandOutcome.Completed
            ? $"{item.DisplayName}: command submitted"
            : $"{item.DisplayName}: {result.Outcome} ({result.DiagnosticCode ?? "no-detail"})");
    }

    private void HandleTileKeyDown(
        DockProjectItem item,
        DockSection section,
        IReadOnlyList<DockProjectItem> items,
        KeyEventArgs args)
    {
        if (args.Key == Key.Delete)
        {
            RemoveReference(item.Id);
            args.Handled = true;
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;
        var direction = (_vertical, args.Key) switch
        {
            (false, Key.Left) or (true, Key.Up) => -1,
            (false, Key.Right) or (true, Key.Down) => 1,
            _ => 0
        };
        if (direction == 0)
            return;

        var currentIndex = IndexOf(items, item.Id);
        if (currentIndex >= 0)
            ApplyMutation(_session.Reorder(section, item.Id, currentIndex + direction), "Reordered by keyboard");
        args.Handled = true;
    }

    private void Tile_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is Button button)
            button.Tag = ((DockTileDragData)button.Tag) with { DragStart = e.GetPosition(this), DragStarted = false };
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button button
            || button.Tag is not DockTileDragData drag || drag.DragStarted)
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - drag.DragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - drag.DragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        var startedDrag = drag with { DragStarted = true };
        button.Tag = startedDrag;
        _ = DragDrop.DoDragDrop(button, startedDrag, DragDropEffects.Move);
    }

    private static void Tile_DragOver(object sender, DragEventArgs e) =>
        e.Effects = e.Data.GetDataPresent(typeof(DockTileDragData)) ? DragDropEffects.Move : DragDropEffects.None;

    private void Tile_Drop(object sender, DragEventArgs e)
    {
        if (sender is not Button targetButton || targetButton.Tag is not DockTileDragData target
            || !e.Data.GetDataPresent(typeof(DockTileDragData))
            || e.Data.GetData(typeof(DockTileDragData)) is not DockTileDragData source
            || source.Section != target.Section)
            return;

        var collection = GetSectionItems(source.Section);
        var sourceIndex = IndexOf(collection, source.ItemId);
        var targetIndex = IndexOf(collection, target.ItemId);
        if (sourceIndex < 0 || targetIndex < 0)
            return;

        var point = e.GetPosition(targetButton);
        var afterTarget = _vertical ? point.Y >= targetButton.ActualHeight / 2 : point.X >= targetButton.ActualWidth / 2;
        var insertion = targetIndex + (afterTarget ? 1 : 0);
        var destination = sourceIndex < insertion ? insertion - 1 : insertion;
        destination = Math.Clamp(destination, 0, collection.Count - 1);
        ApplyMutation(_session.Reorder(source.Section, source.ItemId, destination), "Dock order saved");
    }

    private void RemoveReference(DockItemId id) =>
        ApplyMutation(_session.RemoveReference(id), "Dock reference removed");

    private void ApplyMutation(DockSessionMutationResult result, string successMessage)
    {
        _diagnostics?.Record("dock-state-mutation", TimeSpan.Zero, new
        {
            result.Succeeded,
            result.Changed,
            Change = result.Change.ToString(),
            result.DiagnosticCode,
            PinnedCount = _session.State.PinnedApplications.Count,
            PinnedOrderSha256 = ExperimentalDockDiagnostics.GetPinnedOrderSha256(_session.State)
        });
        if (!result.Succeeded)
        {
            StatusText($"Change not saved ({result.DiagnosticCode ?? result.Change.ToString()})");
            return;
        }
        RenderState();
        StatusText(result.Changed ? successMessage : "No change");
    }

    private IReadOnlyList<DockProjectItem> GetSectionItems(DockSection section) => section switch
    {
        DockSection.PinnedApplications => _session.State.PinnedApplications,
        DockSection.RecentApplications => _session.State.VisibleRecentApplications,
        DockSection.FilesAndFolders => _session.State.FilesAndFolders,
        _ => Array.Empty<DockProjectItem>()
    };

    private static int IndexOf(IReadOnlyList<DockProjectItem> items, DockItemId id)
    {
        for (var index = 0; index < items.Count; index++)
            if (items[index].Id == id)
                return index;
        return -1;
    }

    private DockIconTheme GetIconTheme() => _session.State.Preferences.Appearance switch
    {
        DockAppearancePreference.Light => DockIconTheme.Light,
        DockAppearancePreference.Dark => DockIconTheme.Dark,
        _ => DockIconTheme.Any
    };

    private ImageSource DecodeIconAsset(DockIconAsset asset)
    {
        using var stream = new MemoryStream(asset.EncodedBytes.ToArray(), writable: false);
        BitmapDecoder decoder = asset.Format switch
        {
            DockIconFormat.Png => new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
            DockIconFormat.Ico => new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad),
            _ => throw new ArgumentOutOfRangeException(nameof(asset))
        };
        if (decoder.Frames.Count == 0)
            throw new InvalidDataException("Icon decoder returned no frames.");

        var dpi = VisualTreeHelper.GetDpi(this);
        var targetPixels = (int)Math.Ceiling(IconSize * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY));
        var frame = decoder.Frames
            .OrderBy(candidate => Math.Max(candidate.PixelWidth, candidate.PixelHeight) >= targetPixels ? 0 : 1)
            .ThenBy(candidate => Math.Abs(Math.Max(candidate.PixelWidth, candidate.PixelHeight) - targetPixels))
            .ThenByDescending(candidate => Math.Max(candidate.PixelWidth, candidate.PixelHeight))
            .First();
        frame.Freeze();
        return frame;
    }

    private static string MakeFallbackGlyph(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length switch
        {
            0 => "?",
            1 => parts[0][..Math.Min(2, parts[0].Length)].ToUpperInvariant(),
            _ => string.Concat(parts[0][0], parts[1][0]).ToUpperInvariant()
        };
    }

    private void StatusText(string message)
    {
        _lastStatus = message;
        Backplate.ToolTip = message;
        _diagnostics?.Record("dock-status", TimeSpan.Zero, new { Message = message });
    }

    private enum IconLoadState
    {
        Loading = 1,
        Loaded = 2,
        Failed = 3
    }

    private sealed record IconVisual(Image Image, TextBlock Fallback, string DisplayName);

    private sealed record DockTileDragData(
        DockItemId ItemId,
        DockSection Section,
        Point DragStart = default,
        bool DragStarted = false);
}
