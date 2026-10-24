using System.Security.Cryptography;
using System.Text;
using GlassDock.Core;

namespace GlassDock.App;

public sealed record DockNativeRolePreviewSeedResult(DockState State, bool Changed);

/// <summary>
/// Creates an opt-in, isolated preview layout for the four macOS icon roles. This is not a
/// product default and never changes a role's Windows target identity.
/// </summary>
public static class DockNativeRolePreviewSeed
{
    private const string FileManagerKey = "windows.file-manager";
    private const string SettingsKey = "windows.settings";
    private const string DownloadsKey = "downloads";
    private const string RecycleBinKey = "windows.recycle-bin";

    public static DockNativeRolePreviewSeedResult Apply(DockState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var pinned = state.PinnedApplications.ToList();
        var recent = state.RecentApplications.ToList();
        var files = state.FilesAndFolders.ToList();
        var usedIds = pinned.Concat(recent).Concat(files).Select(item => item.Id.Value).ToHashSet();
        var changed = false;

        var fileManager = Identity(DockIdentityScheme.WindowsSystemRole, FileManagerKey);
        var finderIndex = pinned.FindIndex(item => item.TargetIdentity == fileManager);
        if (finderIndex >= 0)
        {
            var current = pinned[finderIndex];
            if (!string.Equals(current.DisplayName, "Finder", StringComparison.Ordinal))
            {
                pinned[finderIndex] = CopyWithName(current, "Finder");
                changed = true;
            }
        }
        else if (!ContainsTarget(pinned, recent, files, fileManager))
        {
            pinned.Add(new DockProjectItem(
                NextId("finder", fileManager, usedIds), DockProjectItemKind.SystemRole,
                DockSection.PinnedApplications, "Finder", fileManager));
            changed = true;
        }

        AddPinnedRole("System Settings", SettingsKey);
        AddFileRole("Downloads", DockProjectItemKind.Folder, DockIdentityScheme.WindowsKnownFolderId, DownloadsKey);
        AddFileRole("Trash", DockProjectItemKind.SystemRole, DockIdentityScheme.WindowsSystemRole, RecycleBinKey);

        if (!changed)
            return new DockNativeRolePreviewSeedResult(state, false);

        return new DockNativeRolePreviewSeedResult(
            DockState.Restore(pinned, recent, files, state.Preferences), true);

        void AddPinnedRole(string name, string key)
        {
            var target = Identity(DockIdentityScheme.WindowsSystemRole, key);
            if (ContainsTarget(pinned, recent, files, target))
                return;

            pinned.Add(new DockProjectItem(
                NextId(key, target, usedIds), DockProjectItemKind.SystemRole,
                DockSection.PinnedApplications, name, target));
            changed = true;
        }

        void AddFileRole(string name, DockProjectItemKind kind, DockIdentityScheme scheme, string key)
        {
            var target = Identity(scheme, key);
            if (ContainsTarget(pinned, recent, files, target))
                return;

            files.Add(new DockProjectItem(
                NextId(key, target, usedIds), kind, DockSection.FilesAndFolders, name, target));
            changed = true;
        }
    }

    private static DockProjectItem CopyWithName(DockProjectItem item, string name) => new(
        item.Id, item.Kind, item.Section, name, item.TargetIdentity, item.LastUsedUtc);

    private static DockTargetIdentity Identity(DockIdentityScheme scheme, string key) =>
        DockTargetIdentity.Create(scheme, key);

    private static bool ContainsTarget(
        IEnumerable<DockProjectItem> pinned,
        IEnumerable<DockProjectItem> recent,
        IEnumerable<DockProjectItem> files,
        DockTargetIdentity target) => pinned.Concat(recent).Concat(files).Any(item => item.TargetIdentity == target);

    private static DockItemId NextId(string roleKey, DockTargetIdentity target, HashSet<Guid> usedIds)
    {
        for (var attempt = 0; ; attempt++)
        {
            var bytes = Encoding.UTF8.GetBytes($"glassdock-native-role-preview/v1\0{roleKey}\0{(int)target.Scheme}\0{target.ComparisonKey}\0{attempt}");
            var hash = SHA256.HashData(bytes);
            var id = new Guid(hash.AsSpan(0, 16));
            if (id != Guid.Empty && usedIds.Add(id))
                return DockItemId.FromGuid(id);
        }
    }
}
