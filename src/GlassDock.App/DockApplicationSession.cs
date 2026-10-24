using GlassDock.Core;

namespace GlassDock.App;

public sealed class DockApplicationOption
{
    public DockApplicationOption(string displayName, DockTargetIdentity targetIdentity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!targetIdentity.IsValid || targetIdentity.Scheme is not (DockIdentityScheme.WindowsAppUserModelId
            or DockIdentityScheme.WindowsShellLinkPath))
            throw new ArgumentException("A stable Windows application identity is required.", nameof(targetIdentity));

        DisplayName = displayName.Trim();
        TargetIdentity = targetIdentity;
    }

    public string DisplayName { get; }

    public DockTargetIdentity TargetIdentity { get; }
}

public enum DockApplicationCatalogStatus
{
    Succeeded = 1,
    AccessDenied = 2,
    Unsupported = 3,
    Failed = 4
}

public sealed class DockApplicationCatalogResult
{
    private DockApplicationCatalogResult(
        DockApplicationCatalogStatus status,
        IEnumerable<DockApplicationOption> applications,
        int duplicateCount,
        int issueCount,
        string? diagnosticCode)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if (duplicateCount < 0 || issueCount < 0)
            throw new ArgumentOutOfRangeException(nameof(duplicateCount));
        ArgumentNullException.ThrowIfNull(applications);

        var snapshot = applications.ToArray();
        if (snapshot.Any(application => application is null))
            throw new ArgumentException("Application catalog cannot contain null entries.", nameof(applications));
        if (status != DockApplicationCatalogStatus.Succeeded && snapshot.Length != 0)
            throw new ArgumentException("Failed catalog results cannot contain partial applications.", nameof(applications));
        if (status != DockApplicationCatalogStatus.Succeeded && string.IsNullOrWhiteSpace(diagnosticCode))
            throw new ArgumentException("Failed catalog results require a diagnostic code.", nameof(diagnosticCode));

        Status = status;
        Applications = Array.AsReadOnly(snapshot);
        DuplicateCount = duplicateCount;
        IssueCount = issueCount;
        DiagnosticCode = diagnosticCode;
    }

    public DockApplicationCatalogStatus Status { get; }

    public IReadOnlyList<DockApplicationOption> Applications { get; }

    public int DuplicateCount { get; }

    public int IssueCount { get; }

    public string? DiagnosticCode { get; }

    public static DockApplicationCatalogResult Succeeded(
        IEnumerable<DockApplicationOption> applications,
        int duplicateCount = 0,
        int issueCount = 0) =>
        new(DockApplicationCatalogStatus.Succeeded, applications, duplicateCount, issueCount, null);

    public static DockApplicationCatalogResult Failed(DockApplicationCatalogStatus status, string diagnosticCode)
    {
        if (status == DockApplicationCatalogStatus.Succeeded)
            throw new ArgumentOutOfRangeException(nameof(status));
        return new DockApplicationCatalogResult(status, [], 0, 0, diagnosticCode);
    }
}

public enum DockApplicationIconStatus
{
    Found = 1,
    NotFound = 2,
    AccessDenied = 3,
    Unsupported = 4,
    Failed = 5
}

public sealed class DockApplicationIconAsset
{
    public const int MaximumEncodedBytes = 4 * 1024 * 1024;
    private readonly byte[] _png;

    public DockApplicationIconAsset(ReadOnlyMemory<byte> png)
    {
        if (png.Length is < 24 or > MaximumEncodedBytes
            || !png.Span.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }))
            throw new ArgumentException("Icon must be a bounded PNG asset.", nameof(png));
        _png = png.ToArray();
    }

    public ReadOnlyMemory<byte> Png => _png;
}

public sealed class DockApplicationIconResult
{
    private DockApplicationIconResult(DockApplicationIconStatus status, DockApplicationIconAsset? asset, string? diagnosticCode)
    {
        if (!Enum.IsDefined(status))
            throw new ArgumentOutOfRangeException(nameof(status));
        if ((status == DockApplicationIconStatus.Found) != (asset is not null))
            throw new ArgumentException("Only a found icon result may contain an asset.", nameof(asset));
        Status = status;
        Asset = asset;
        DiagnosticCode = diagnosticCode;
    }

    public DockApplicationIconStatus Status { get; }

    public DockApplicationIconAsset? Asset { get; }

    public string? DiagnosticCode { get; }

    public static DockApplicationIconResult Found(DockApplicationIconAsset asset) =>
        new(DockApplicationIconStatus.Found, asset ?? throw new ArgumentNullException(nameof(asset)), null);

    public static DockApplicationIconResult Unavailable(DockApplicationIconStatus status, string? diagnosticCode = null)
    {
        if (status == DockApplicationIconStatus.Found)
            throw new ArgumentOutOfRangeException(nameof(status));
        return new DockApplicationIconResult(status, null, diagnosticCode);
    }
}

public interface IDockApplicationProvider
{
    ValueTask<DockApplicationCatalogResult> DiscoverDockApplicationsAsync(CancellationToken cancellationToken = default);

    ValueTask<DockApplicationIconResult> ResolveApplicationIconAsync(
        DockTargetIdentity targetIdentity,
        CancellationToken cancellationToken = default);

    ValueTask<DockCommandResult> ExecuteAsync(DockCommand command, CancellationToken cancellationToken = default);
}

public sealed record DockStateStoreSaveResult(bool Succeeded, string? DiagnosticCode = null);

public interface IDockStateStore
{
    DockState LoadState();

    DockStateStoreSaveResult SaveState(DockState state);
}

public sealed record DockSessionMutationResult(
    bool Succeeded,
    bool Changed,
    DockStateChange Change,
    string? DiagnosticCode = null);

public enum DockExecutionMode
{
    PreviewOnly = 1,
    AllowShellCommands = 2
}

/// <summary>
/// UI-framework-neutral orchestration for catalog refresh, Core state changes, persistence, and
/// Shell commands. It never promotes observed apps to Recent without an actual activity event.
/// </summary>
public sealed class DockApplicationSession
{
    private readonly IDockStateStore _stateStore;
    private readonly IDockApplicationProvider _applications;

    public DockApplicationSession(
        DockState initialState,
        IDockStateStore stateStore,
        IDockApplicationProvider applications,
        DockExecutionMode executionMode = DockExecutionMode.PreviewOnly)
    {
        if (!Enum.IsDefined(executionMode))
            throw new ArgumentOutOfRangeException(nameof(executionMode));
        State = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _applications = applications ?? throw new ArgumentNullException(nameof(applications));
        ExecutionMode = executionMode;
        Catalog = DockApplicationCatalogResult.Succeeded([]);
    }

    public DockState State { get; private set; }

    public DockApplicationCatalogResult Catalog { get; private set; }

    public DockExecutionMode ExecutionMode { get; }

    public async ValueTask<DockApplicationCatalogResult> RefreshCatalogAsync(CancellationToken cancellationToken = default)
    {
        Catalog = await _applications.DiscoverDockApplicationsAsync(cancellationToken).ConfigureAwait(false);
        return Catalog;
    }

    public DockSessionMutationResult PinApplication(DockApplicationOption application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var item = new DockProjectItem(
            DockItemId.New(),
            DockProjectItemKind.Application,
            DockSection.PinnedApplications,
            application.DisplayName,
            application.TargetIdentity);
        return Commit(State.AddPinnedApplication(item));
    }

    public DockSessionMutationResult RemoveReference(DockItemId id) =>
        Commit(State.RemoveReference(id));

    public DockSessionMutationResult Reorder(DockSection section, DockItemId id, int destinationIndex) =>
        Commit(State.Reorder(section, id, destinationIndex));

    public DockSessionMutationResult SetEdge(DockEdge edge) => Commit(State.SetEdge(edge));

    public DockSessionMutationResult SetAppearance(DockAppearancePreference appearance) =>
        Commit(State.SetAppearance(appearance));

    public ValueTask<DockApplicationIconResult> ResolveIconAsync(
        DockTargetIdentity targetIdentity,
        CancellationToken cancellationToken = default) =>
        _applications.ResolveApplicationIconAsync(targetIdentity, cancellationToken);

    public ValueTask<DockCommandResult> ExecuteAsync(
        DockItemId itemId,
        DockCommandKind kind,
        CancellationToken cancellationToken = default)
    {
        if (!State.TryFind(itemId, out var item) || item is null)
            return ValueTask.FromResult(new DockCommandResult(DockCommandOutcome.TargetNotFound, "dock-item-not-found"));

        // This is the Shell execution boundary. UI affordances must never be the only protection.
        if (ExecutionMode == DockExecutionMode.PreviewOnly)
            return ValueTask.FromResult(new DockCommandResult(DockCommandOutcome.BlockedByPolicy, "preview-only-mode"));

        return _applications.ExecuteAsync(DockCommand.For(kind, item), cancellationToken);
    }

    /// <summary>
    /// Invokes an application by first trying the provider's normal, policy-respecting activation
    /// path. It launches only when no matching window is found (or the provider cannot safely map
    /// an argument-specific shortcut to a window). Foreground denial is returned unchanged.
    /// </summary>
    public async ValueTask<DockCommandResult> InvokeApplicationAsync(
        DockItemId itemId,
        CancellationToken cancellationToken = default)
    {
        var activation = await ExecuteAsync(itemId, DockCommandKind.Activate, cancellationToken).ConfigureAwait(false);
        var shouldLaunch = activation.Outcome == DockCommandOutcome.TargetNotFound
            || (activation.Outcome == DockCommandOutcome.Unsupported
                && string.Equals(activation.DiagnosticCode,
                    "argument-specific-window-identity-unavailable", StringComparison.Ordinal));
        return shouldLaunch
            ? await ExecuteAsync(itemId, DockCommandKind.Launch, cancellationToken).ConfigureAwait(false)
            : activation;
    }

    private DockSessionMutationResult Commit(DockStateMutation mutation)
    {
        if (ReferenceEquals(mutation.State, State))
        {
            var acceptedNoOp = mutation.Change is DockStateChange.AlreadyPresent or DockStateChange.AlreadyPinned;
            return new DockSessionMutationResult(acceptedNoOp, false, mutation.Change,
                acceptedNoOp ? null : "dock-state-change-rejected");
        }

        var save = _stateStore.SaveState(mutation.State);
        if (!save.Succeeded)
            return new DockSessionMutationResult(false, false, mutation.Change, save.DiagnosticCode ?? "dock-state-save-failed");

        State = mutation.State;
        return new DockSessionMutationResult(true, true, mutation.Change);
    }
}
