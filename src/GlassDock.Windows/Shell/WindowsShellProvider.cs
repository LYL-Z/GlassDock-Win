using System.Buffers.Binary;
using GlassDock.Core;

namespace GlassDock.Windows.Shell;

/// <summary>
/// Provider-resolved Windows application metadata. This value object performs no Shell lookup.
/// AUMIDs use Core's scheme-aware case-insensitive comparator. A Shell Link must supply a
/// provider-resolved comparison key; this adapter deliberately does not lowercase or resolve paths.
/// </summary>
public sealed record WindowsShellApplicationDescriptor
{
    public WindowsShellApplicationDescriptor(
        string displayName,
        DockTargetIdentity targetIdentity,
        string? iconSource = null)
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
        IconSource = string.IsNullOrWhiteSpace(iconSource) ? targetIdentity.Value : iconSource.Trim();
        if (IconSource.Contains('\0'))
            throw new ArgumentException("Icon source cannot contain a null character.", nameof(iconSource));
    }

    public string DisplayName { get; }

    public DockTargetIdentity TargetIdentity { get; }

    /// <summary>Windows Shell parsing name used to resolve the icon; it is not a user-data path.</summary>
    public string IconSource { get; }

    public static WindowsShellApplicationDescriptor FromAppUserModelId(
        string displayName,
        string appUserModelId,
        string? iconSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        var identity = DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, appUserModelId);
        return new WindowsShellApplicationDescriptor(displayName, identity, iconSource);
    }

    /// <summary>Uses the shortcut as the launch locator while retaining its explicit AUMID for identity.</summary>
    public static WindowsShellApplicationDescriptor FromAppUserModelIdShortcut(
        string displayName,
        string appUserModelId,
        string shellLinkPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(appUserModelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellLinkPath);
        var identity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsAppUserModelId,
            shellLinkPath,
            appUserModelId);
        return new WindowsShellApplicationDescriptor(displayName, identity, shellLinkPath);
    }

    public static WindowsShellApplicationDescriptor FromShellLink(
        string displayName,
        string shellLinkPath,
        string providerResolvedComparisonKey,
        string? iconSource = null)
    {
        var identity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsShellLinkPath,
            shellLinkPath,
            providerResolvedComparisonKey);
        return new WindowsShellApplicationDescriptor(displayName, identity, iconSource);
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

public enum WindowsShellDiscoveryIssueKind
{
    AccessDenied = 1,
    InvalidShortcut = 2,
    UnresolvedTarget = 3,
    UnsupportedTarget = 4,
    MetadataUnavailable = 5,
    EntryLimitReached = 6,
    EnumerationFailed = 7
}

/// <summary>A sanitized per-entry diagnostic; it omits application names and user-specific paths.</summary>
public sealed record WindowsShellDiscoveryIssue(WindowsShellDiscoveryIssueKind Kind, string DiagnosticCode);

/// <summary>An immutable catalog response. Empty success means discovery found no entries.</summary>
public sealed class WindowsShellCatalogResult
{
    private WindowsShellCatalogResult(
        WindowsShellCatalogStatus status,
        IEnumerable<WindowsShellApplicationDescriptor> applications,
        string? diagnosticCode,
        IEnumerable<WindowsShellDiscoveryIssue>? issues,
        int scannedShortcutCount,
        int scannedAumidEntryCount,
        int duplicateCount)
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
        if (scannedShortcutCount < 0)
            throw new ArgumentOutOfRangeException(nameof(scannedShortcutCount));
        if (scannedAumidEntryCount < 0)
            throw new ArgumentOutOfRangeException(nameof(scannedAumidEntryCount));
        if (duplicateCount < 0 || duplicateCount > scannedShortcutCount + scannedAumidEntryCount)
            throw new ArgumentOutOfRangeException(nameof(duplicateCount));
        var issueSnapshot = (issues ?? []).ToArray();
        if (issueSnapshot.Any(issue => issue is null || !Enum.IsDefined(issue.Kind)
            || string.IsNullOrWhiteSpace(issue.DiagnosticCode)))
        {
            throw new ArgumentException("Discovery issues must contain a valid kind and code.", nameof(issues));
        }

        Status = status;
        Applications = Array.AsReadOnly(snapshot);
        DiagnosticCode = diagnosticCode;
        Issues = Array.AsReadOnly(issueSnapshot);
        ScannedShortcutCount = scannedShortcutCount;
        ScannedAumidEntryCount = scannedAumidEntryCount;
        DuplicateCount = duplicateCount;
    }

    public WindowsShellCatalogStatus Status { get; }

    public IReadOnlyList<WindowsShellApplicationDescriptor> Applications { get; }

    public string? DiagnosticCode { get; }

    public IReadOnlyList<WindowsShellDiscoveryIssue> Issues { get; }

    public int ScannedShortcutCount { get; }

    public int ScannedAumidEntryCount { get; }

    public int DuplicateCount { get; }

    public static WindowsShellCatalogResult Succeeded(IEnumerable<WindowsShellApplicationDescriptor> applications) =>
        new(WindowsShellCatalogStatus.Succeeded, applications, null, null, 0, 0, 0);

    public static WindowsShellCatalogResult Succeeded(
        IEnumerable<WindowsShellApplicationDescriptor> applications,
        IEnumerable<WindowsShellDiscoveryIssue> issues,
        int scannedShortcutCount,
        int duplicateCount,
        int scannedAumidEntryCount = 0) =>
        new(WindowsShellCatalogStatus.Succeeded, applications, null, issues, scannedShortcutCount, scannedAumidEntryCount, duplicateCount);

    public static WindowsShellCatalogResult Failed(WindowsShellCatalogStatus status, string diagnosticCode)
    {
        if (status == WindowsShellCatalogStatus.Succeeded)
            throw new ArgumentOutOfRangeException(nameof(status), "Use Succeeded for successful catalog results.");
        return new WindowsShellCatalogResult(status, [], diagnosticCode, null, 0, 0, 0);
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

internal static class WindowsShellTargetLocator
{
    public static bool IsShellLinkPath(DockTargetIdentity identity) =>
        identity.Scheme is DockIdentityScheme.WindowsAppUserModelId or DockIdentityScheme.WindowsShellLinkPath
        && Path.IsPathFullyQualified(identity.Value)
        && string.Equals(Path.GetExtension(identity.Value), ".lnk", StringComparison.OrdinalIgnoreCase);
}

/// <summary>UI-framework-neutral PNG bytes extracted from a local Windows Shell icon source.</summary>
public sealed class WindowsShellIconAsset
{
    private static ReadOnlySpan<byte> PngSignature => [137, 80, 78, 71, 13, 10, 26, 10];
    private const int MaximumEncodedBytes = 4 * 1024 * 1024;
    private const uint MaximumDimension = 1024;
    private readonly byte[] _encodedPng;

    public WindowsShellIconAsset(ReadOnlyMemory<byte> encodedPng)
    {
        var bytes = encodedPng.Span;
        if (!bytes.StartsWith(PngSignature)
            || bytes.Length < 24
            || !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new ArgumentException("Icon data must contain a PNG signature and IHDR header.", nameof(encodedPng));
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (bytes.Length > MaximumEncodedBytes || width is 0 or > MaximumDimension
            || height is 0 or > MaximumDimension || (ulong)width * height > 1_048_576)
        {
            throw new ArgumentException("Icon PNG exceeds the supported byte or pixel limits.", nameof(encodedPng));
        }
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
