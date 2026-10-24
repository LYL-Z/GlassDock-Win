using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GlassDock.App;
using GlassDock.Core;

namespace GlassDock.Experimental.Wpf;

public partial class ApplicationPickerWindow : Window
{
    private const int MaximumVisibleEntries = 100;
    private const int MaximumIconsPerRefresh = 30;
    private readonly DockApplicationSession _session;
    private readonly ExperimentalDockDiagnostics? _diagnostics;
    private readonly ObservableCollection<ApplicationEntry> _entries = [];
    private readonly Dictionary<DockTargetIdentity, ImageSource?> _iconCache = [];
    private CancellationTokenSource? _catalogLoadCancellation;
    private CancellationTokenSource? _iconLoadCancellation;
    private IReadOnlyList<DockApplicationOption> _catalog = [];

    public ApplicationPickerWindow(DockApplicationSession session, ExperimentalDockDiagnostics? diagnostics)
    {
        InitializeComponent();
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _diagnostics = diagnostics;
        ApplicationsList.ItemsSource = _entries;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var cancellation = new CancellationTokenSource();
        _catalogLoadCancellation = cancellation;
        StatusLabel.Text = "Discovering Windows applications…";
        try
        {
            var result = await _session.RefreshCatalogAsync(cancellation.Token);
            if (cancellation.IsCancellationRequested || !IsLoaded)
                return;
            if (result.Status != DockApplicationCatalogStatus.Succeeded)
            {
                StatusLabel.Text = $"Catalog unavailable ({result.DiagnosticCode})";
                return;
            }

            _catalog = result.Applications
                .OrderBy(application => application.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            ApplyFilter();
            StatusLabel.Text = $"{_catalog.Count} discovered · {result.DuplicateCount} duplicates removed · {result.IssueCount} entries skipped";
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception exception)
        {
            StatusLabel.Text = $"Discovery failed ({exception.GetType().Name})";
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
        _entries.Clear();
        ApplicationsList.ItemsSource = null;
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        var matches = _catalog.Where(application => query.Length == 0
                || application.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Take(MaximumVisibleEntries)
            .ToArray();

        _entries.Clear();
        foreach (var application in matches)
        {
            var alreadyPinned = _session.State.PinnedApplications.Any(item => item.TargetIdentity == application.TargetIdentity);
            _entries.Add(new ApplicationEntry(application, alreadyPinned, _iconCache.GetValueOrDefault(application.TargetIdentity)));
        }

        StatusLabel.Text = matches.Length == 0
            ? query.Length == 0 ? "No applications were discovered." : "No matching applications."
            : $"Showing {matches.Length}{(matches.Length == MaximumVisibleEntries ? "+" : string.Empty)} results";
        _ = LoadVisibleIconsAsync(matches);
    }

    private async Task LoadVisibleIconsAsync(IReadOnlyList<DockApplicationOption> matches)
    {
        var cancellation = new CancellationTokenSource();
        var previous = _iconLoadCancellation;
        _iconLoadCancellation = cancellation;
        previous?.Cancel();
        var token = cancellation.Token;
        try
        {
            foreach (var application in matches.Take(MaximumIconsPerRefresh))
            {
                if (token.IsCancellationRequested || !IsLoaded)
                    return;
                if (!_iconCache.TryGetValue(application.TargetIdentity, out var icon))
                {
                    try
                    {
                        var result = await _session.ResolveIconAsync(application.TargetIdentity, token);
                        if (token.IsCancellationRequested || !IsLoaded)
                            return;
                        icon = result.Status == DockApplicationIconStatus.Found && result.Asset is not null
                            ? DecodePng(result.Asset.Png)
                            : null;
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }
                    catch (Exception)
                    {
                        icon = null;
                    }
                    _iconCache[application.TargetIdentity] = icon;
                }

                var entry = _entries.FirstOrDefault(candidate => candidate.Application.TargetIdentity == application.TargetIdentity);
                if (entry is not null)
                    entry.Icon = icon;
            }
        }
        finally
        {
            if (ReferenceEquals(_iconLoadCancellation, cancellation))
                _iconLoadCancellation = null;
            cancellation.Dispose();
        }
    }

    private void PinButton_Click(object sender, RoutedEventArgs e) => PinSelected();

    private void ApplicationsList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => PinSelected();

    private void PinSelected()
    {
        if (ApplicationsList.SelectedItem is not ApplicationEntry entry)
        {
            StatusLabel.Text = "Select an application first.";
            return;
        }

        var result = _session.PinApplication(entry.Application);
        _diagnostics?.Record("dock-state-mutation", TimeSpan.Zero, new
        {
            Operation = "PinApplication",
            result.Succeeded,
            result.Changed,
            Change = result.Change.ToString(),
            result.DiagnosticCode,
            PinnedCount = _session.State.PinnedApplications.Count,
            PinnedOrderSha256 = ExperimentalDockDiagnostics.GetPinnedOrderSha256(_session.State)
        });
        if (!result.Succeeded)
        {
            StatusLabel.Text = $"Could not pin application ({result.DiagnosticCode ?? result.Change.ToString()})";
            return;
        }

        entry.IsPinned = true;
        StatusLabel.Text = result.Changed ? $"Pinned {entry.DisplayName}. The Dock state was saved." : "This application is already pinned.";
    }

    private static ImageSource DecodePng(ReadOnlyMemory<byte> png)
    {
        using var stream = new System.IO.MemoryStream(png.ToArray(), writable: false);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.DecodePixelWidth = 64;
        bitmap.DecodePixelHeight = 64;
        bitmap.StreamSource = stream;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    private static string MakeFallback(string name)
    {
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return words.Length switch
        {
            0 => "?",
            1 => words[0][..Math.Min(2, words[0].Length)].ToUpperInvariant(),
            _ => string.Concat(words[0][0], words[1][0]).ToUpperInvariant()
        };
    }

    private sealed class ApplicationEntry : System.ComponentModel.INotifyPropertyChanged
    {
        private ImageSource? _icon;
        private bool _isPinned;

        public ApplicationEntry(DockApplicationOption application, bool isPinned, ImageSource? icon)
        {
            Application = application;
            _isPinned = isPinned;
            _icon = icon;
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        public DockApplicationOption Application { get; }
        public string DisplayName => Application.DisplayName;
        public string IdentitySummary => Application.TargetIdentity.Scheme == DockIdentityScheme.WindowsAppUserModelId
            ? "Windows application identity"
            : "Start Menu shortcut";
        public string Fallback => MakeFallback(DisplayName);

        public ImageSource? Icon
        {
            get => _icon;
            set
            {
                if (ReferenceEquals(_icon, value)) return;
                _icon = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(Icon)));
            }
        }

        public bool IsPinned
        {
            get => _isPinned;
            set
            {
                if (_isPinned == value) return;
                _isPinned = value;
                PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(IsPinned)));
            }
        }
    }
}
