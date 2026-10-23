using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly string _loadDiagnostic;
    private readonly Dictionary<DockTargetIdentity, ImageSource?> _iconCache = [];
    private CancellationTokenSource? _iconLoadCancellation;
    private bool _vertical;

    public DockWindow(DockApplicationSession session, string loadDiagnostic)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _loadDiagnostic = loadDiagnostic;
        ApplyEdgeLayout();
        RenderState();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        StatusText("Loading Windows application catalog…");
        try
        {
            var result = await _session.RefreshCatalogAsync();
            if (result.Status == DockApplicationCatalogStatus.Succeeded)
                StatusText($"{result.Applications.Count} applications discovered · none were pinned automatically");
            else
                StatusText($"Application discovery unavailable ({result.DiagnosticCode})");
        }
        catch (Exception exception)
        {
            StatusText($"Application discovery failed ({exception.GetType().Name})");
        }

        await LoadStateIconsAsync();
        RenderState();
        if (!string.IsNullOrWhiteSpace(_loadDiagnostic)
            && !string.Equals(_loadDiagnostic, "DefaultedMissing", StringComparison.Ordinal))
            StatusText($"Configuration load: {_loadDiagnostic}");
    }

    private void Window_Closed(object? sender, EventArgs e) => _iconLoadCancellation?.Cancel();

    private void AddAppsButton_Click(object sender, RoutedEventArgs e)
    {
        var picker = new ApplicationPickerWindow(_session) { Owner = this };
        _ = picker.ShowDialog();
        RenderState();
        _ = LoadStateIconsAsync();
    }

    private void PositionButton_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        AddEdgeMenuItem(menu, "Bottom", DockEdge.Bottom);
        AddEdgeMenuItem(menu, "Left", DockEdge.Left);
        AddEdgeMenuItem(menu, "Right", DockEdge.Right);
        menu.PlacementTarget = PositionButton;
        menu.Placement = PlacementMode.Bottom;
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
            ApplyEdgeLayout();
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
    }

    private void ApplyEdgeLayout()
    {
        var edge = _session.State.Preferences.Edge;
        _vertical = edge is DockEdge.Left or DockEdge.Right;
        var workArea = SystemParameters.WorkArea;
        if (_vertical)
        {
            MinWidth = 150;
            MinHeight = 300;
            Width = 174;
            Height = Math.Min(760, Math.Max(300, workArea.Height - 40));
            Left = edge == DockEdge.Left ? workArea.Left + 10 : workArea.Right - Width - 10;
            Top = workArea.Top + (workArea.Height - Height) / 2;
            SectionsHost.Orientation = Orientation.Vertical;
            SectionsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            SectionsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        }
        else
        {
            MinWidth = 420;
            MinHeight = 148;
            Width = Math.Min(1080, Math.Max(480, workArea.Width - 36));
            Height = 170;
            Left = workArea.Left + (workArea.Width - Width) / 2;
            Top = workArea.Bottom - Height - 8;
            SectionsHost.Orientation = Orientation.Horizontal;
            SectionsScrollViewer.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            SectionsScrollViewer.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        }
        PositionButton.Content = $"Position: {edge}";
    }

    private void RenderState()
    {
        SectionsHost.Children.Clear();
        AddSection("PINNED", DockSection.PinnedApplications, _session.State.PinnedApplications, "No pinned apps");

        if (_session.State.Preferences.ShowRecentApplications)
        {
            AddSection("RECENT", DockSection.RecentApplications, _session.State.VisibleRecentApplications,
                "No observed recent activity");
        }

        AddSection("FILES", DockSection.FilesAndFolders, _session.State.FilesAndFolders,
            "File pinning is a later work package");
    }

    private void AddSection(
        string label,
        DockSection section,
        IReadOnlyList<DockProjectItem> items,
        string emptyLabel)
    {
        if (SectionsHost.Children.Count > 0)
            SectionsHost.Children.Add(CreateSeparator());

        var group = new StackPanel
        {
            Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal,
            Margin = _vertical ? new Thickness(0, 7, 0, 7) : new Thickness(8, 0, 8, 0)
        };
        var caption = new TextBlock
        {
            Text = label,
            Foreground = new SolidColorBrush(Color.FromRgb(158, 171, 190)),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Margin = _vertical ? new Thickness(0, 4, 0, 7) : new Thickness(2, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        group.Children.Add(caption);

        if (items.Count == 0)
        {
            group.Children.Add(new TextBlock
            {
                Text = emptyLabel,
                Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 195)),
                FontSize = 10,
                MaxWidth = _vertical ? 128 : 160,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(5, 5, 8, 5),
            });
            AutomationProperties.SetName((FrameworkElement)group.Children[^1], emptyLabel);
        }
        else
        {
            var tiles = new StackPanel
            {
                Orientation = _vertical ? Orientation.Vertical : Orientation.Horizontal
            };
            foreach (var projectItem in items)
                tiles.Children.Add(CreateTile(projectItem, section, items));
            group.Children.Add(tiles);
        }

        SectionsHost.Children.Add(group);
    }

    private UIElement CreateSeparator() => _vertical
        ? new Border
        {
            Height = 1,
            Margin = new Thickness(16, 3, 16, 3),
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        }
        : new Border
        {
            Width = 1,
            Margin = new Thickness(4, 7, 4, 7),
            Background = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private Button CreateTile(DockProjectItem item, DockSection section, IReadOnlyList<DockProjectItem> sectionItems)
    {
        var image = new Image
        {
            Width = IconSize,
            Height = IconSize,
            Stretch = Stretch.Uniform,
            Source = _iconCache.GetValueOrDefault(item.TargetIdentity),
            Visibility = _iconCache.GetValueOrDefault(item.TargetIdentity) is null ? Visibility.Collapsed : Visibility.Visible
        };
        var fallback = new TextBlock
        {
            Text = MakeFallbackGlyph(item.DisplayName),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = image.Source is null ? Visibility.Visible : Visibility.Collapsed
        };
        var iconGrid = new Grid
        {
            Width = IconSize + 8,
            Height = IconSize + 8,
            Background = new SolidColorBrush(Color.FromArgb(38, 255, 255, 255)),
            ClipToBounds = true
        };
        iconGrid.Children.Add(image);
        iconGrid.Children.Add(fallback);
        var button = new Button
        {
            Width = _vertical ? 128 : 70,
            Height = _vertical ? 62 : 74,
            Margin = new Thickness(3),
            Padding = new Thickness(3),
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            BorderThickness = new Thickness(1),
            Content = iconGrid,
            Focusable = true,
            ToolTip = item.DisplayName,
            Cursor = Cursors.Hand,
            AllowDrop = section != DockSection.RecentApplications,
            Tag = new DockTileDragData(item.Id, section)
        };
        AutomationProperties.SetName(button, $"{item.DisplayName}, {section}");
        AutomationProperties.SetHelpText(button, "Enter or Space launches or opens. Delete removes this Dock reference. Use Ctrl+Arrow to reorder.");
        button.Click += async (_, _) => await ExecuteItemAsync(item);
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

    private async Task LoadStateIconsAsync()
    {
        _iconLoadCancellation?.Cancel();
        _iconLoadCancellation?.Dispose();
        _iconLoadCancellation = new CancellationTokenSource();
        var token = _iconLoadCancellation.Token;
        var items = _session.State.PinnedApplications
            .Concat(_session.State.VisibleRecentApplications)
            .Concat(_session.State.FilesAndFolders)
            .Where(item => !_iconCache.ContainsKey(item.TargetIdentity))
            .ToArray();

        foreach (var item in items)
        {
            if (token.IsCancellationRequested || !IsLoaded)
                return;
            try
            {
                var result = await _session.ResolveIconAsync(item.TargetIdentity, token);
                _iconCache[item.TargetIdentity] = result.Status == DockApplicationIconStatus.Found && result.Asset is not null
                    ? DecodePng(result.Asset.Png)
                    : null;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                _iconCache[item.TargetIdentity] = null;
            }
        }
    }

    private async Task ExecuteItemAsync(DockProjectItem item)
    {
        var result = item.Kind is DockProjectItemKind.File or DockProjectItemKind.Folder
            ? await _session.ExecuteAsync(item.Id, DockCommandKind.Open)
            : await _session.InvokeApplicationAsync(item.Id);
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
            button.Tag = ((DockTileDragData)button.Tag) with { DragStart = e.GetPosition(this) };
    }

    private void Tile_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || sender is not Button button || button.Tag is not DockTileDragData drag)
            return;
        var current = e.GetPosition(this);
        if (Math.Abs(current.X - drag.DragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(current.Y - drag.DragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;
        _ = DragDrop.DoDragDrop(button, drag, DragDropEffects.Move);
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

    private static ImageSource DecodePng(ReadOnlyMemory<byte> png)
    {
        using var stream = new MemoryStream(png.ToArray(), writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
        bitmap.DecodePixelWidth = 128;
        bitmap.DecodePixelHeight = 128;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
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

    private void StatusText(string message) =>
        StatusLabel.Text = message;

    private sealed record DockTileDragData(DockItemId ItemId, DockSection Section, Point DragStart = default);
}
