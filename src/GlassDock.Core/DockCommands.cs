namespace GlassDock.Core;

public enum DockCommandKind
{
    Launch = 1,
    Activate = 2,
    Open = 3
}

public enum DockCommandOutcome
{
    Completed = 1,
    TargetNotFound = 2,
    Unsupported = 3,
    AccessDenied = 4,
    ForegroundActivationDenied = 5,
    Failed = 6
}

/// <summary>
/// A UI- and Shell-independent command request. It describes intent only and performs no OS action.
/// </summary>
public sealed record DockCommand
{
    public DockCommand(DockCommandKind kind, DockItemId itemId, DockTargetIdentity targetIdentity)
    {
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (itemId.Value == Guid.Empty)
            throw new ArgumentException("A stable Dock item ID is required.", nameof(itemId));
        if (!targetIdentity.IsValid)
            throw new ArgumentException("A stable target identity is required.", nameof(targetIdentity));

        Kind = kind;
        ItemId = itemId;
        TargetIdentity = targetIdentity;
    }

    public DockCommandKind Kind { get; }

    public DockItemId ItemId { get; }

    public DockTargetIdentity TargetIdentity { get; }

    public static DockCommand For(DockCommandKind kind, DockProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        return new DockCommand(kind, item.Id, item.TargetIdentity);
    }
}

/// <summary>
/// Result returned by a future command adapter. Core does not launch, activate, or open targets.
/// </summary>
public sealed record DockCommandResult(DockCommandOutcome Outcome, string? DiagnosticCode = null)
{
    public bool IsCompleted => Outcome == DockCommandOutcome.Completed;
}
