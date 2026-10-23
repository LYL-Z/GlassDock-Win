using GlassDock.Core;

namespace GlassDock.Windows.Shell;

/// <summary>Production Windows implementation for Start Menu discovery, Shell icons, and commands.</summary>
public sealed class WindowsShellProvider : IWindowsShellProvider
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
}
