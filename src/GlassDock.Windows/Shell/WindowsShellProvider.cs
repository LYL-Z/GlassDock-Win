using GlassDock.Core;

namespace GlassDock.Windows.Shell;

/// <summary>
/// Provider-resolved Windows application metadata. This value object performs no Shell lookup.
/// AUMIDs use Core's scheme-aware case-insensitive comparator. A Shell Link must supply a
/// provider-resolved comparison key; this adapter deliberately does not lowercase or resolve paths.
/// </summary>
public sealed record WindowsShellApplicationDescriptor
{
    public WindowsShellApplicationDescriptor(string displayName, DockTargetIdentity targetIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (displayName.Contains('\0'))
            throw new ArgumentException("Display name cannot contain a null character.", nameof(displayName));
        if (!targetIdentity.IsValid)
            throw new ArgumentException("A provider-resolved target identity is required.", nameof(targetIdentity));
        if (targetIdentity.Scheme is not (DockIdentityScheme.WindowsAppUserModelId
            or DockIdentityScheme.WindowsShellLinkPath))
        {
            throw new ArgumentException(
                "An application requires an AUMID or Shell Link identity; a package family name alone is not unique.",
                nameof(targetIdentity));
        }

        DisplayName = displayName.Trim();
        TargetIdentity = targetIdentity;
    }

    public string DisplayName { get; }

    public DockTargetIdentity TargetIdentity { get; }

    public static WindowsShellApplicationDescriptor FromAppUserModelId(string displayName, string appUserModelId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        var identity = DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, appUserModelId);
        return new WindowsShellApplicationDescriptor(displayName, identity);
    }

    public static WindowsShellApplicationDescriptor FromShellLink(
        string displayName,
        string shellLinkPath,
        string providerResolvedComparisonKey)
    {
        var identity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsShellLinkPath,
            shellLinkPath,
            providerResolvedComparisonKey);
        return new WindowsShellApplicationDescriptor(displayName, identity);
    }

    /// <summary>Creates a pinned Core item using the caller's persisted Dock item ID.</summary>
    public DockProjectItem CreatePinnedItem(DockItemId id) => new(
        id,
        DockProjectItemKind.Application,
        DockSection.PinnedApplications,
        DisplayName,
        TargetIdentity);
}

public enum WindowsShellCatalogStatus
{
    Succeeded = 1,
    AccessDenied = 2,
    Unsupported = 3,
    Failed = 4
}

/// <summary>An immutable catalog response. Empty success means discovery found no entries.</summary>
public sealed class WindowsShellCatalogResult
{
    private WindowsShellCatalogResult(
        WindowsShellCatalogStatus status,
        IEnumerable<WindowsShellApplicationDescriptor> applications,
        string? diagnosticCode)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        ArgumentNullException.ThrowIfNull(applications);

        var snapshot = applications.ToArray();
        if (snapshot.Any(application => application is null))
            throw new ArgumentException("Catalog entries cannot contain null values.", nameof(applications));
        if (status != WindowsShellCatalogStatus.Succeeded && snapshot.Length != 0)
            throw new ArgumentException("Failed discovery responses cannot carry partial catalog entries.", nameof(applications));
        if (status != WindowsShellCatalogStatus.Succeeded && string.IsNullOrWhiteSpace(diagnosticCode))
            throw new ArgumentException("Failed discovery responses require a diagnostic code.", nameof(diagnosticCode));

        Status = status;
        Applications = Array.AsReadOnly(snapshot);
        DiagnosticCode = diagnosticCode;
    }

    public WindowsShellCatalogStatus Status { get; }

    public IReadOnlyList<WindowsShellApplicationDescriptor> Applications { get; }

    public string? DiagnosticCode { get; }

    public static WindowsShellCatalogResult Succeeded(IEnumerable<WindowsShellApplicationDescriptor> applications) =>
        new(WindowsShellCatalogStatus.Succeeded, applications, null);

    public static WindowsShellCatalogResult Failed(WindowsShellCatalogStatus status, string diagnosticCode)
    {
        if (status == WindowsShellCatalogStatus.Succeeded)
            throw new ArgumentOutOfRangeException(nameof(status), "Use Succeeded for successful catalog results.");
        return new WindowsShellCatalogResult(status, [], diagnosticCode);
    }
}

public enum WindowsShellIconStatus
{
    Found = 1,
    NotFound = 2,
    AccessDenied = 3,
    Unsupported = 4,
    Failed = 5
}

/// <summary>UI-framework-neutral PNG bytes extracted from a local Windows Shell icon source.</summary>
public sealed class WindowsShellIconAsset
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private readonly byte[] _encodedPng;

    public WindowsShellIconAsset(ReadOnlyMemory<byte> encodedPng)
    {
        if (!encodedPng.Span.StartsWith(PngSignature))
            throw new ArgumentException("Icon data must contain a PNG signature.", nameof(encodedPng));
        _encodedPng = encodedPng.ToArray();
    }

    public ReadOnlyMemory<byte> EncodedPng => _encodedPng;
}

public sealed class WindowsShellIconResult
{
    private WindowsShellIconResult(WindowsShellIconStatus status, WindowsShellIconAsset? asset, string? diagnosticCode)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if ((status == WindowsShellIconStatus.Found) != (asset is not null))
            throw new ArgumentException("Only a found result may carry an icon asset.", nameof(asset));
        if ((status is WindowsShellIconStatus.AccessDenied or WindowsShellIconStatus.Failed)
            && string.IsNullOrWhiteSpace(diagnosticCode))
        {
            throw new ArgumentException("Access and failure results require a diagnostic code.", nameof(diagnosticCode));
        }

        Status = status;
        Asset = asset;
        DiagnosticCode = diagnosticCode;
    }

    public WindowsShellIconStatus Status { get; }

    public WindowsShellIconAsset? Asset { get; }

    public string? DiagnosticCode { get; }

    public static WindowsShellIconResult Found(WindowsShellIconAsset asset) =>
        new(WindowsShellIconStatus.Found, asset ?? throw new ArgumentNullException(nameof(asset)), null);

    public static WindowsShellIconResult Unavailable(WindowsShellIconStatus status, string? diagnosticCode = null)
    {
        if (status == WindowsShellIconStatus.Found)
            throw new ArgumentOutOfRangeException(nameof(status), "Use Found for successful icon results.");
        return new WindowsShellIconResult(status, null, diagnosticCode);
    }
}

/// <summary>
/// Boundary for future Windows Shell discovery, command dispatch, and icon extraction.
/// Implementations must report foreground activation denial instead of forcing activation.
/// This contract itself does not enumerate the system or perform operating-system actions.
/// </summary>
public interface IWindowsShellProvider
{
    ValueTask<WindowsShellCatalogResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default);

    ValueTask<DockCommandResult> ExecuteAsync(DockCommand command, CancellationToken cancellationToken = default);

    ValueTask<WindowsShellIconResult> ResolveIconAsync(
        DockTargetIdentity targetIdentity,
        CancellationToken cancellationToken = default);
}
