using System.Security.Cryptography;
using System.Text;

namespace GlassDock.Core;

public enum DockProjectItemKind
{
    Application = 1,
    File = 2,
    Folder = 3,
    SystemRole = 4
}

public enum DockSection
{
    PinnedApplications = 1,
    RecentApplications = 2,
    FilesAndFolders = 3
}

/// <summary>
/// Stable identity schemes supplied by a future Windows Shell adapter.
/// Process names are deliberately not an identity scheme.
/// </summary>
public enum DockIdentityScheme
{
    WindowsAppUserModelId = 1,
    WindowsPackageFamilyName = 2,
    WindowsShellLinkPath = 3,
    WindowsCanonicalPath = 4,
    WindowsKnownFolderId = 5,
    WindowsSystemRole = 6
}

public enum DockAppearancePreference
{
    FollowSystem = 1,
    Light = 2,
    Dark = 3
}

public readonly record struct DockItemId
{
    private DockItemId(Guid value)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("Dock item ID cannot be empty.", nameof(value));

        Value = value;
    }

    public Guid Value { get; }

    public static DockItemId New() => new(Guid.NewGuid());

    public static DockItemId FromGuid(Guid value) => new(value);

    public static DockItemId Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (!Guid.TryParseExact(value, "D", out var parsed))
            throw new FormatException("Dock item ID must be a canonical GUID in D format.");

        return new DockItemId(parsed);
    }

    internal static DockItemId ForRecentTarget(DockTargetIdentity identity)
    {
        var input = Encoding.UTF8.GetBytes($"{(int)identity.Scheme}\0{identity.Value}");
        var hash = SHA256.HashData(input);
        return new DockItemId(new Guid(hash.AsSpan(0, 16)));
    }

    public override string ToString() => Value.ToString("D");
}

public readonly record struct DockTargetIdentity
{
    private DockTargetIdentity(DockIdentityScheme scheme, string value)
    {
        Scheme = scheme;
        Value = value;
    }

    public DockIdentityScheme Scheme { get; }

    public string Value { get; }

    public bool IsValid => Enum.IsDefined(Scheme) && !string.IsNullOrWhiteSpace(Value);

    public static DockTargetIdentity Create(DockIdentityScheme scheme, string value)
    {
        if (!Enum.IsDefined(scheme))
            throw new ArgumentOutOfRangeException(nameof(scheme));
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim();
        if (normalized.Contains('\0'))
            throw new ArgumentException("Target identity cannot contain a null character.", nameof(value));

        return new DockTargetIdentity(scheme, normalized);
    }
}

public sealed record DockProjectItem
{
    public DockProjectItem(
        DockItemId id,
        DockProjectItemKind kind,
        DockSection section,
        string displayName,
        DockTargetIdentity targetIdentity,
        DateTimeOffset? lastUsedUtc = null)
    {
        if (id.Value == Guid.Empty)
            throw new ArgumentException("Dock item ID cannot be empty.", nameof(id));
        if (!Enum.IsDefined(kind))
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (!Enum.IsDefined(section))
            throw new ArgumentOutOfRangeException(nameof(section));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        if (!targetIdentity.IsValid)
            throw new ArgumentException("A valid stable target identity is required.", nameof(targetIdentity));
        if (!IdentitySupportsKind(kind, targetIdentity.Scheme))
            throw new ArgumentException("Target identity scheme does not match the project item kind.", nameof(targetIdentity));

        var normalizedName = displayName.Trim();
        if (normalizedName.Contains('\0'))
            throw new ArgumentException("Display name cannot contain a null character.", nameof(displayName));

        var normalizedLastUsed = lastUsedUtc?.ToUniversalTime();
        ValidateSection(kind, section, normalizedLastUsed);

        Id = id;
        Kind = kind;
        Section = section;
        DisplayName = normalizedName;
        TargetIdentity = targetIdentity;
        LastUsedUtc = normalizedLastUsed;
    }

    public DockItemId Id { get; }

    public DockProjectItemKind Kind { get; }

    public DockSection Section { get; }

    public string DisplayName { get; }

    public DockTargetIdentity TargetIdentity { get; }

    public DateTimeOffset? LastUsedUtc { get; }

    internal DockProjectItem AsPinnedApplication() => new(
        Id,
        Kind,
        DockSection.PinnedApplications,
        DisplayName,
        TargetIdentity);

    private static bool IdentitySupportsKind(DockProjectItemKind kind, DockIdentityScheme scheme) => kind switch
    {
        DockProjectItemKind.Application => scheme is DockIdentityScheme.WindowsAppUserModelId
            or DockIdentityScheme.WindowsPackageFamilyName
            or DockIdentityScheme.WindowsShellLinkPath,
        DockProjectItemKind.File => scheme == DockIdentityScheme.WindowsCanonicalPath,
        DockProjectItemKind.Folder => scheme is DockIdentityScheme.WindowsCanonicalPath or DockIdentityScheme.WindowsKnownFolderId,
        DockProjectItemKind.SystemRole => scheme == DockIdentityScheme.WindowsSystemRole,
        _ => false
    };

    private static void ValidateSection(DockProjectItemKind kind, DockSection section, DateTimeOffset? lastUsedUtc)
    {
        var valid = section switch
        {
            DockSection.PinnedApplications => kind is DockProjectItemKind.Application or DockProjectItemKind.SystemRole
                && lastUsedUtc is null,
            DockSection.RecentApplications => kind == DockProjectItemKind.Application && lastUsedUtc is not null,
            DockSection.FilesAndFolders => kind is DockProjectItemKind.File or DockProjectItemKind.Folder or DockProjectItemKind.SystemRole
                && lastUsedUtc is null,
            _ => false
        };

        if (!valid)
            throw new ArgumentException("Item kind, section, and last-used timestamp are inconsistent.");
    }
}

public sealed record DockPreferences
{
    public DockPreferences(
        DockEdge edge = DockEdge.Bottom,
        bool showRecentApplications = true,
        DockAppearancePreference appearance = DockAppearancePreference.FollowSystem)
    {
        if (!Enum.IsDefined(edge))
            throw new ArgumentOutOfRangeException(nameof(edge));
        if (!Enum.IsDefined(appearance))
            throw new ArgumentOutOfRangeException(nameof(appearance));

        Edge = edge;
        ShowRecentApplications = showRecentApplications;
        Appearance = appearance;
    }

    public DockEdge Edge { get; }

    public bool ShowRecentApplications { get; }

    public DockAppearancePreference Appearance { get; }

    public DockPreferences WithEdge(DockEdge edge) => new(edge, ShowRecentApplications, Appearance);

    public DockPreferences WithRecentApplications(bool enabled) => new(Edge, enabled, Appearance);

    public DockPreferences WithAppearance(DockAppearancePreference appearance) => new(Edge, ShowRecentApplications, appearance);
}

public sealed record DockRecentActivity
{
    public DockRecentActivity(DockTargetIdentity targetIdentity, string displayName, DateTimeOffset observedAt)
    {
        if (!targetIdentity.IsValid || targetIdentity.Scheme is not (DockIdentityScheme.WindowsAppUserModelId
            or DockIdentityScheme.WindowsPackageFamilyName or DockIdentityScheme.WindowsShellLinkPath))
            throw new ArgumentException("Recent activity requires a stable application identity.", nameof(targetIdentity));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        TargetIdentity = targetIdentity;
        DisplayName = displayName.Trim();
        ObservedAtUtc = observedAt.ToUniversalTime();
    }

    public DockTargetIdentity TargetIdentity { get; }

    public string DisplayName { get; }

    public DateTimeOffset ObservedAtUtc { get; }
}

public enum DockStateChange
{
    Added,
    Removed,
    Reordered,
    RecordedRecentActivity,
    ClearedRecentApplications,
    PreferenceChanged,
    AlreadyPresent,
    AlreadyPinned,
    NotFound,
    InvalidItem,
    InvalidPosition,
    RecentApplicationsDisabled,
    RecentActivityWasStale
}

public readonly record struct DockStateMutation(DockState State, DockStateChange Change);

public sealed class DockState
{
    public const int MaximumRecentApplications = 3;

    private static readonly Guid ExplorerItemGuid = Guid.Parse("1c2c3a04-49e6-4e5c-a456-0987dd6d8028");

    private readonly DockProjectItem[] _pinnedApplications;
    private readonly DockProjectItem[] _recentApplications;
    private readonly DockProjectItem[] _filesAndFolders;
    private readonly IReadOnlyList<DockProjectItem> _pinnedApplicationsView;
    private readonly IReadOnlyList<DockProjectItem> _recentApplicationsView;
    private readonly IReadOnlyList<DockProjectItem> _visibleRecentApplicationsView;
    private readonly IReadOnlyList<DockProjectItem> _filesAndFoldersView;

    private DockState(
        IEnumerable<DockProjectItem> pinnedApplications,
        IEnumerable<DockProjectItem> recentApplications,
        IEnumerable<DockProjectItem> filesAndFolders,
        DockPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        _pinnedApplications = pinnedApplications.ToArray();
        _recentApplications = recentApplications.ToArray();
        _filesAndFolders = filesAndFolders.ToArray();
        Preferences = preferences;
        ValidateInvariants();

        _pinnedApplicationsView = Array.AsReadOnly(_pinnedApplications);
        _recentApplicationsView = Array.AsReadOnly(_recentApplications);
        _visibleRecentApplicationsView = preferences.ShowRecentApplications
            ? _recentApplicationsView
            : Array.Empty<DockProjectItem>();
        _filesAndFoldersView = Array.AsReadOnly(_filesAndFolders);
    }

    public IReadOnlyList<DockProjectItem> PinnedApplications => _pinnedApplicationsView;

    public IReadOnlyList<DockProjectItem> RecentApplications => _recentApplicationsView;

    public IReadOnlyList<DockProjectItem> VisibleRecentApplications => _visibleRecentApplicationsView;

    public IReadOnlyList<DockProjectItem> FilesAndFolders => _filesAndFoldersView;

    public DockPreferences Preferences { get; }

    public static DockState CreateDefault()
    {
        var explorer = new DockProjectItem(
            DockItemId.FromGuid(ExplorerItemGuid),
            DockProjectItemKind.SystemRole,
            DockSection.PinnedApplications,
            "File Explorer",
            DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.file-manager"));

        return new DockState([explorer], [], [], new DockPreferences());
    }

    public static DockState Restore(
        IEnumerable<DockProjectItem> pinnedApplications,
        IEnumerable<DockProjectItem> recentApplications,
        IEnumerable<DockProjectItem> filesAndFolders,
        DockPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(pinnedApplications);
        ArgumentNullException.ThrowIfNull(recentApplications);
        ArgumentNullException.ThrowIfNull(filesAndFolders);
        return new DockState(pinnedApplications, recentApplications, filesAndFolders, preferences);
    }

    public DockStateMutation AddPinnedApplication(DockProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Section is not (DockSection.PinnedApplications or DockSection.RecentApplications)
            || item.Kind is not (DockProjectItemKind.Application or DockProjectItemKind.SystemRole))
            return new DockStateMutation(this, DockStateChange.InvalidItem);

        if (_pinnedApplications.Any(existing => existing.TargetIdentity == item.TargetIdentity))
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);
        var recentMatch = _recentApplications.FirstOrDefault(existing => existing.TargetIdentity == item.TargetIdentity);
        if (ContainsId(item.Id) && recentMatch?.Id != item.Id)
            return new DockStateMutation(this, DockStateChange.InvalidItem);

        var pinned = item.AsPinnedApplication();
        var recent = _recentApplications.Where(existing => existing.TargetIdentity != item.TargetIdentity).ToArray();
        return new DockStateMutation(
            new DockState(_pinnedApplications.Append(pinned), recent, _filesAndFolders, Preferences),
            DockStateChange.Added);
    }

    public DockStateMutation AddFileOrFolderReference(DockProjectItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (item.Section != DockSection.FilesAndFolders
            || item.Kind is not (DockProjectItemKind.File or DockProjectItemKind.Folder or DockProjectItemKind.SystemRole))
            return new DockStateMutation(this, DockStateChange.InvalidItem);
        if (_filesAndFolders.Any(existing => existing.TargetIdentity == item.TargetIdentity))
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);
        if (ContainsId(item.Id))
            return new DockStateMutation(this, DockStateChange.InvalidItem);

        return new DockStateMutation(
            new DockState(_pinnedApplications, _recentApplications, _filesAndFolders.Append(item), Preferences),
            DockStateChange.Added);
    }

    public DockStateMutation RemoveReference(DockItemId id)
    {
        var pinned = _pinnedApplications.Where(item => item.Id != id).ToArray();
        var recent = _recentApplications.Where(item => item.Id != id).ToArray();
        var files = _filesAndFolders.Where(item => item.Id != id).ToArray();
        if (pinned.Length == _pinnedApplications.Length
            && recent.Length == _recentApplications.Length
            && files.Length == _filesAndFolders.Length)
            return new DockStateMutation(this, DockStateChange.NotFound);

        return new DockStateMutation(new DockState(pinned, recent, files, Preferences), DockStateChange.Removed);
    }

    public DockStateMutation Reorder(DockSection section, DockItemId id, int destinationIndex)
    {
        if (section == DockSection.RecentApplications)
            return new DockStateMutation(this, DockStateChange.InvalidItem);

        var source = section switch
        {
            DockSection.PinnedApplications => _pinnedApplications,
            DockSection.FilesAndFolders => _filesAndFolders,
            _ => null
        };
        if (source is null)
            return new DockStateMutation(this, DockStateChange.InvalidItem);

        var sourceIndex = Array.FindIndex(source, item => item.Id == id);
        if (sourceIndex < 0)
            return new DockStateMutation(this, DockStateChange.NotFound);
        if ((uint)destinationIndex >= (uint)source.Length)
            return new DockStateMutation(this, DockStateChange.InvalidPosition);
        if (sourceIndex == destinationIndex)
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);

        var reordered = source.ToList();
        var item = reordered[sourceIndex];
        reordered.RemoveAt(sourceIndex);
        reordered.Insert(destinationIndex, item);

        var state = section == DockSection.PinnedApplications
            ? new DockState(reordered, _recentApplications, _filesAndFolders, Preferences)
            : new DockState(_pinnedApplications, _recentApplications, reordered, Preferences);
        return new DockStateMutation(state, DockStateChange.Reordered);
    }

    public DockStateMutation SetRecentApplicationsEnabled(bool enabled)
    {
        if (Preferences.ShowRecentApplications == enabled)
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);

        return new DockStateMutation(
            new DockState(_pinnedApplications, _recentApplications, _filesAndFolders,
                Preferences.WithRecentApplications(enabled)),
            DockStateChange.PreferenceChanged);
    }

    public DockStateMutation SetAppearance(DockAppearancePreference appearance)
    {
        var preferences = Preferences.WithAppearance(appearance);
        if (preferences == Preferences)
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);

        return new DockStateMutation(
            new DockState(_pinnedApplications, _recentApplications, _filesAndFolders, preferences),
            DockStateChange.PreferenceChanged);
    }

    public DockStateMutation SetEdge(DockEdge edge)
    {
        var preferences = Preferences.WithEdge(edge);
        if (preferences == Preferences)
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);

        return new DockStateMutation(
            new DockState(_pinnedApplications, _recentApplications, _filesAndFolders, preferences),
            DockStateChange.PreferenceChanged);
    }

    public DockStateMutation ClearRecentApplications()
    {
        if (_recentApplications.Length == 0)
            return new DockStateMutation(this, DockStateChange.AlreadyPresent);

        return new DockStateMutation(
            new DockState(_pinnedApplications, [], _filesAndFolders, Preferences),
            DockStateChange.ClearedRecentApplications);
    }

    /// <summary>
    /// Adds an item only when a caller supplies an actual observed local activity event.
    /// This method does not discover or launch applications by itself.
    /// </summary>
    public DockStateMutation RecordRecentActivity(DockRecentActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        if (!Preferences.ShowRecentApplications)
            return new DockStateMutation(this, DockStateChange.RecentApplicationsDisabled);
        if (_pinnedApplications.Any(item => item.TargetIdentity == activity.TargetIdentity))
            return new DockStateMutation(this, DockStateChange.AlreadyPinned);

        var existingIndex = Array.FindIndex(_recentApplications,
            item => item.TargetIdentity == activity.TargetIdentity);
        var existing = existingIndex >= 0 ? _recentApplications[existingIndex] : null;
        if (existing is not null && activity.ObservedAtUtc < existing.LastUsedUtc!.Value)
            return new DockStateMutation(this, DockStateChange.RecentActivityWasStale);

        var recentItem = new DockProjectItem(
            existing?.Id ?? DockItemId.ForRecentTarget(activity.TargetIdentity),
            DockProjectItemKind.Application,
            DockSection.RecentApplications,
            activity.DisplayName,
            activity.TargetIdentity,
            activity.ObservedAtUtc);

        var recents = _recentApplications
            .Where(item => item.TargetIdentity != activity.TargetIdentity)
            .ToList();
        var insertionIndex = recents.FindIndex(item => item.LastUsedUtc!.Value <= activity.ObservedAtUtc);
        if (insertionIndex < 0)
            recents.Add(recentItem);
        else
            recents.Insert(insertionIndex, recentItem);

        var boundedRecents = recents
            .Take(MaximumRecentApplications)
            .ToArray();

        return new DockStateMutation(
            new DockState(_pinnedApplications, boundedRecents, _filesAndFolders, Preferences),
            DockStateChange.RecordedRecentActivity);
    }

    public bool TryFind(DockItemId id, out DockProjectItem? item)
    {
        item = _pinnedApplications.Concat(_recentApplications).Concat(_filesAndFolders)
            .FirstOrDefault(candidate => candidate.Id == id);
        return item is not null;
    }

    private bool ContainsId(DockItemId id) =>
        _pinnedApplications.Any(item => item.Id == id)
        || _recentApplications.Any(item => item.Id == id)
        || _filesAndFolders.Any(item => item.Id == id);

    private void ValidateInvariants()
    {
        if (_recentApplications.Length > MaximumRecentApplications)
            throw new ArgumentException($"At most {MaximumRecentApplications} recent applications may be stored.");

        var all = _pinnedApplications.Concat(_recentApplications).Concat(_filesAndFolders).ToArray();
        if (all.Any(item => item is null))
            throw new ArgumentException("Dock state cannot contain null items.");
        if (all.Select(item => item.Id).Distinct().Count() != all.Length)
            throw new ArgumentException("Dock item IDs must be unique across all sections.");

        EnsureSection(_pinnedApplications, DockSection.PinnedApplications);
        EnsureSection(_recentApplications, DockSection.RecentApplications);
        EnsureSection(_filesAndFolders, DockSection.FilesAndFolders);

        var pinnedTargets = _pinnedApplications.Select(item => item.TargetIdentity).ToHashSet();
        if (_pinnedApplications.Length != pinnedTargets.Count)
            throw new ArgumentException("Pinned targets must be unique.");
        if (_recentApplications.Any(item => pinnedTargets.Contains(item.TargetIdentity)))
            throw new ArgumentException("Pinned applications cannot also appear in recent applications.");
        if (_recentApplications.Select(item => item.TargetIdentity).Distinct().Count() != _recentApplications.Length)
            throw new ArgumentException("Recent application targets must be unique.");
        for (var index = 1; index < _recentApplications.Length; index++)
        {
            if (_recentApplications[index - 1].LastUsedUtc!.Value < _recentApplications[index].LastUsedUtc!.Value)
                throw new ArgumentException("Recent applications must be ordered from newest to oldest.");
        }
        if (_filesAndFolders.Select(item => item.TargetIdentity).Distinct().Count() != _filesAndFolders.Length)
            throw new ArgumentException("File and folder references must be unique by target identity.");
    }

    private static void EnsureSection(IEnumerable<DockProjectItem> items, DockSection expected)
    {
        if (items.Any(item => item is null || item.Section != expected))
            throw new ArgumentException($"Every item in the {expected} section must declare that section.");
    }
}
