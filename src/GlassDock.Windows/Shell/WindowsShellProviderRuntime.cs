using GlassDock.App;
using GlassDock.Core;

namespace GlassDock.Windows.Shell;

/// <summary>Production Windows implementation for Start Menu discovery, Shell icons, and commands.</summary>
public sealed class WindowsShellProvider : IWindowsShellProvider, IDockApplicationProvider
{
    private readonly IWindowsApplicationDiscovery _discovery;
    private readonly IWindowsShellIconExtractor _icons;
    private readonly WindowsShellCommandExecutor _commands;
    private readonly object _catalogLock = new();
    private Dictionary<DockTargetIdentity, string> _iconSources = [];

    public WindowsShellProvider()
        : this(
            new WindowsStartMenuApplicationDiscovery(),
            new WindowsShellIconExtractor(),
            new WindowsShellCommandExecutor())
    {
    }

    public WindowsShellProvider(
        IWindowsApplicationDiscovery discovery,
        IWindowsShellIconExtractor icons,
        WindowsShellCommandExecutor commands)
    {
        _discovery = discovery ?? throw new ArgumentNullException(nameof(discovery));
        _icons = icons ?? throw new ArgumentNullException(nameof(icons));
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public async ValueTask<WindowsShellCatalogResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = await _discovery.DiscoverApplicationsAsync(cancellationToken).ConfigureAwait(false);
        if (result.Status == WindowsShellCatalogStatus.Succeeded)
        {
            var sources = result.Applications.ToDictionary(
                application => application.TargetIdentity,
                application => application.IconSource);
            lock (_catalogLock)
                _iconSources = sources;
        }
        return result;
    }

    public async ValueTask<DockApplicationCatalogResult> DiscoverDockApplicationsAsync(CancellationToken cancellationToken = default)
    {
        var result = await DiscoverApplicationsAsync(cancellationToken).ConfigureAwait(false);
        if (result.Status != WindowsShellCatalogStatus.Succeeded)
        {
            var status = result.Status switch
            {
                WindowsShellCatalogStatus.AccessDenied => DockApplicationCatalogStatus.AccessDenied,
                WindowsShellCatalogStatus.Unsupported => DockApplicationCatalogStatus.Unsupported,
                _ => DockApplicationCatalogStatus.Failed
            };
            return DockApplicationCatalogResult.Failed(status, result.DiagnosticCode ?? "shell-discovery-failed");
        }

        return DockApplicationCatalogResult.Succeeded(
            result.Applications.Select(application => new DockApplicationOption(
                application.DisplayName,
                application.TargetIdentity)),
            result.DuplicateCount,
            result.Issues.Count);
    }

    public ValueTask<DockCommandResult> ExecuteAsync(DockCommand command, CancellationToken cancellationToken = default) =>
        _commands.ExecuteAsync(command, cancellationToken);

    public ValueTask<WindowsShellIconResult> ResolveIconAsync(
        DockTargetIdentity targetIdentity,
        CancellationToken cancellationToken = default)
    {
        if (!targetIdentity.IsValid)
            return ValueTask.FromResult(WindowsShellIconResult.Unavailable(
                WindowsShellIconStatus.Unsupported,
                "invalid-target-identity"));

        if (targetIdentity.Scheme == DockIdentityScheme.WindowsSystemRole)
        {
            if (!string.Equals(targetIdentity.ComparisonKey, "windows.file-manager", StringComparison.OrdinalIgnoreCase))
            {
                return ValueTask.FromResult(WindowsShellIconResult.Unavailable(
                    WindowsShellIconStatus.Unsupported,
                    "system-role-icon-unsupported"));
            }

            var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrWhiteSpace(windowsDirectory))
            {
                return ValueTask.FromResult(WindowsShellIconResult.Unavailable(
                    WindowsShellIconStatus.Failed,
                    "windows-directory-unavailable"));
            }

            return _icons.ResolveIconAsync(Path.Combine(windowsDirectory, "explorer.exe"), cancellationToken);
        }

        string source;
        lock (_catalogLock)
        {
            if (!_iconSources.TryGetValue(targetIdentity, out source!))
            {
                source = targetIdentity.Scheme == DockIdentityScheme.WindowsAppUserModelId
                    ? WindowsShellTargetLocator.IsShellLinkPath(targetIdentity)
                        ? targetIdentity.Value
                        : $"shell:AppsFolder\\{targetIdentity.ComparisonKey}"
                    : targetIdentity.Value;
            }
        }

        return _icons.ResolveIconAsync(source, cancellationToken);
    }

    public async ValueTask<DockApplicationIconResult> ResolveApplicationIconAsync(
        DockTargetIdentity targetIdentity,
        CancellationToken cancellationToken = default)
    {
        var result = await ResolveIconAsync(targetIdentity, cancellationToken).ConfigureAwait(false);
        if (result.Status == WindowsShellIconStatus.Found && result.Asset is not null)
            return DockApplicationIconResult.Found(new DockApplicationIconAsset(result.Asset.EncodedPng));

        var status = result.Status switch
        {
            WindowsShellIconStatus.NotFound => DockApplicationIconStatus.NotFound,
            WindowsShellIconStatus.AccessDenied => DockApplicationIconStatus.AccessDenied,
            WindowsShellIconStatus.Unsupported => DockApplicationIconStatus.Unsupported,
            _ => DockApplicationIconStatus.Failed
        };
        return DockApplicationIconResult.Unavailable(status, result.DiagnosticCode);
    }
}
