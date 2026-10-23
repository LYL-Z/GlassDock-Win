using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Core.Tests;

[TestClass]
public sealed class DockProjectStateTests
{
    private static readonly DateTimeOffset Epoch = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [TestMethod]
    public void DefaultStateUsesApprovedPoliciesWithoutInventingRecentItems()
    {
        var state = DockState.CreateDefault();

        Assert.AreEqual(DockEdge.Bottom, state.Preferences.Edge);
        Assert.IsTrue(state.Preferences.ShowRecentApplications);
        Assert.AreEqual(DockAppearancePreference.FollowSystem, state.Preferences.Appearance);
        Assert.AreEqual(1, state.PinnedApplications.Count);
        Assert.AreEqual("File Explorer", state.PinnedApplications[0].DisplayName);
        Assert.AreEqual("windows.file-manager", state.PinnedApplications[0].TargetIdentity.Value);
        Assert.AreEqual(0, state.RecentApplications.Count);
        Assert.AreEqual(0, state.FilesAndFolders.Count);
    }

    [TestMethod]
    public void ApplicationIdentityUsesStableProviderKeysRatherThanProcessNames()
    {
        var packageIdentity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsPackageFamilyName,
            "Contoso.Editor_12345abcde");

        Assert.AreEqual(DockIdentityScheme.WindowsPackageFamilyName, packageIdentity.Scheme);
        Assert.AreEqual(packageIdentity, DockTargetIdentity.Create(
            DockIdentityScheme.WindowsPackageFamilyName, "CONTOSO.EDITOR_12345ABCDE"));
        Assert.ThrowsExactly<ArgumentException>(() => Application(Guid.NewGuid(), packageIdentity, "Package family"));
        Assert.IsFalse(Enum.GetNames<DockIdentityScheme>().Contains("ProcessName", StringComparer.Ordinal));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockTargetIdentity.Create((DockIdentityScheme)99, "editor.exe"));
    }

    [TestMethod]
    public void IdentityComparisonIsSchemeAwareAndUsesProviderCanonicalKeysForAliases()
    {
        var appLower = DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor", "Contoso.Editor");
        var appUpper = DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "CONTOSO.EDITOR", "CONTOSO.EDITOR");
        Assert.AreEqual(appLower, appUpper);
        Assert.AreEqual(appLower.GetHashCode(), appUpper.GetHashCode());

        var pathOne = DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath,
            "D:\\Apps\\Editor.exe", "volume-1:file-42");
        var pathAlias = DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath,
            "D:\\Links\\Editor.exe", "volume-1:file-42");
        var pathDifferentKey = DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath,
            "d:\\apps\\editor.exe", "D:\\Apps\\Editor.exe");
        Assert.AreEqual(pathOne, pathAlias, "A provider may collapse aliases through one canonical comparison key.");
        Assert.AreNotEqual(pathOne, pathDifferentKey, "Core must not lowercase arbitrary path identities.");

        var state = DockState.CreateDefault().AddFileOrFolderReference(new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.File, DockSection.FilesAndFolders, "Editor", pathOne)).State;
        Assert.AreEqual(DockStateChange.AlreadyPresent, state.AddFileOrFolderReference(new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.File, DockSection.FilesAndFolders, "Editor alias", pathAlias)).Change);
    }

    [TestMethod]
    public void PackageFamilyNameCannotStandAloneAsAnApplicationIdentity()
    {
        var familyName = DockTargetIdentity.Create(DockIdentityScheme.WindowsPackageFamilyName, "Contoso_abc123");
        Assert.ThrowsExactly<ArgumentException>(() => Application(Guid.NewGuid(), familyName, "Package family"));
    }

    [TestMethod]
    public void ProjectItemRejectsMismatchedKindSectionAndTargetIdentity()
    {
        var pathIdentity = DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath, "D:\\Apps\\Editor.exe");

        Assert.ThrowsExactly<ArgumentException>(() => new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.Application, DockSection.PinnedApplications,
            "Editor", pathIdentity));
        Assert.ThrowsExactly<ArgumentException>(() => new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.File, DockSection.RecentApplications,
            "Document", pathIdentity, Epoch));
    }

    [TestMethod]
    public void PinningDeduplicatesByTargetAndPromotesRecentEntry()
    {
        var state = DockState.CreateDefault();
        var target = AppIdentity("Contoso.Editor");
        var recent = state.RecordRecentActivity(new DockRecentActivity(target, "Editor", Epoch));
        var recentItem = recent.State.RecentApplications.Single();

        var pinned = recent.State.AddPinnedApplication(recentItem);
        var duplicate = pinned.State.AddPinnedApplication(Application(Guid.NewGuid(), AppIdentity("CONTOSO.EDITOR"), "Editor duplicate"));

        Assert.AreEqual(DockStateChange.Added, pinned.Change);
        Assert.AreEqual(recentItem.Id, pinned.State.PinnedApplications[^1].Id);
        Assert.AreEqual(0, pinned.State.RecentApplications.Count);
        Assert.AreEqual(DockStateChange.AlreadyPresent, duplicate.Change);
        Assert.AreSame(pinned.State, duplicate.State);
    }

    [TestMethod]
    public void PromotionKeepsRecentStableIdEvenWhenCallerProvidesANewId()
    {
        var state = DockState.CreateDefault().RecordRecentActivity(new DockRecentActivity(
            AppIdentity("Contoso.Editor"), "Editor", Epoch)).State;
        var recentId = state.RecentApplications.Single().Id;
        var promoted = state.AddPinnedApplication(Application(Guid.NewGuid(), AppIdentity("contoso.editor"), "Editor"));

        Assert.AreEqual(DockStateChange.Added, promoted.Change);
        Assert.AreEqual(recentId, promoted.State.PinnedApplications[^1].Id);
        Assert.AreEqual(0, promoted.State.RecentApplications.Count);
        Assert.AreEqual(1, state.RecentApplications.Count, "A transition must not mutate the prior snapshot.");
    }

    [TestMethod]
    public void RecentApplicationsAreObservedDeduplicatedCappedAndOrderedNewestFirst()
    {
        var state = DockState.CreateDefault();
        for (var index = 0; index < 4; index++)
        {
            var activity = new DockRecentActivity(
                AppIdentity($"Contoso.App{index}"), $"App {index}", Epoch.AddMinutes(index));
            state = state.RecordRecentActivity(activity).State;
        }

        Assert.AreEqual(DockState.MaximumRecentApplications, state.RecentApplications.Count);
        CollectionAssert.AreEqual(new[] { "App 3", "App 2", "App 1" },
            state.RecentApplications.Select(item => item.DisplayName).ToArray());

        state = state.RecordRecentActivity(new DockRecentActivity(
            AppIdentity("Contoso.App1"), "App 1", Epoch.AddMinutes(5))).State;

        CollectionAssert.AreEqual(new[] { "App 1", "App 3", "App 2" },
            state.RecentApplications.Select(item => item.DisplayName).ToArray());
        Assert.AreEqual(3, state.RecentApplications.Select(item => item.TargetIdentity).Distinct().Count());
    }

    [TestMethod]
    public void EqualRecentTimestampsPreserveOrderAndStaleObservationsAreIgnored()
    {
        var tiedAt = Epoch.AddMinutes(1);
        var state = DockState.CreateDefault()
            .RecordRecentActivity(new DockRecentActivity(AppIdentity("Contoso.One"), "One", tiedAt)).State
            .RecordRecentActivity(new DockRecentActivity(AppIdentity("Contoso.Two"), "Two", tiedAt)).State;

        CollectionAssert.AreEqual(new[] { "One", "Two" }, state.RecentApplications.Select(item => item.DisplayName).ToArray());
        var repeated = state.RecordRecentActivity(new DockRecentActivity(AppIdentity("Contoso.One"), "One renamed", tiedAt));
        CollectionAssert.AreEqual(new[] { "One renamed", "Two" }, repeated.State.RecentApplications.Select(item => item.DisplayName).ToArray());

        var stale = repeated.State.RecordRecentActivity(new DockRecentActivity(
            AppIdentity("Contoso.One"), "Stale name", tiedAt.AddTicks(-1)));
        Assert.AreEqual(DockStateChange.RecentActivityWasStale, stale.Change);
        Assert.AreSame(repeated.State, stale.State);
        Assert.AreEqual("One renamed", stale.State.RecentApplications[0].DisplayName);
    }

    [TestMethod]
    public void FixedApplicationsAreExcludedFromRecentProjectionAndRecentCanBeCleared()
    {
        var state = DockState.CreateDefault();
        var target = AppIdentity("Contoso.Editor");
        state = state.AddPinnedApplication(Application(Guid.NewGuid(), target, "Editor")).State;

        var pinnedActivity = state.RecordRecentActivity(new DockRecentActivity(target, "Editor", Epoch));
        Assert.AreEqual(DockStateChange.AlreadyPinned, pinnedActivity.Change);
        Assert.AreEqual(0, pinnedActivity.State.RecentApplications.Count);

        var other = pinnedActivity.State.RecordRecentActivity(new DockRecentActivity(
            AppIdentity("Contoso.Terminal"), "Terminal", Epoch.AddMinutes(1))).State;
        var disabled = other.SetRecentApplicationsEnabled(false).State;
        Assert.AreEqual(1, disabled.RecentApplications.Count);
        Assert.AreEqual(0, disabled.VisibleRecentApplications.Count);
        Assert.AreEqual(DockStateChange.RecentApplicationsDisabled,
            disabled.RecordRecentActivity(new DockRecentActivity(
                AppIdentity("Contoso.Browser"), "Browser", Epoch.AddMinutes(2))).Change);

        var cleared = disabled.ClearRecentApplications();
        Assert.AreEqual(DockStateChange.ClearedRecentApplications, cleared.Change);
        Assert.AreEqual(0, cleared.State.RecentApplications.Count);
        Assert.AreEqual(2, cleared.State.PinnedApplications.Count);
    }

    [TestMethod]
    public void FileReferencesAndOrderingAreStateOnlyAndStable()
    {
        var fileA = File(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"), "D:\\Docs\\a.txt", "a.txt");
        var fileB = File(Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"), "D:\\Docs\\b.txt", "b.txt");
        var first = DockState.CreateDefault().AddFileOrFolderReference(fileA).State;
        first = first.AddFileOrFolderReference(fileB).State;

        var reordered = first.Reorder(DockSection.FilesAndFolders, fileB.Id, 0);
        var removed = reordered.State.RemoveReference(fileB.Id);

        Assert.AreEqual(DockStateChange.Reordered, reordered.Change);
        Assert.AreEqual(fileB.Id, reordered.State.FilesAndFolders[0].Id);
        Assert.AreEqual(DockStateChange.Removed, removed.Change);
        Assert.AreEqual(fileA.Id, removed.State.FilesAndFolders.Single().Id);
        Assert.AreEqual(2, first.FilesAndFolders.Count, "State transitions must not mutate prior snapshots.");
        Assert.AreEqual(DockStateChange.InvalidPosition,
            first.Reorder(DockSection.FilesAndFolders, fileA.Id, 2).Change);
        Assert.AreEqual(DockStateChange.InvalidPosition,
            first.Reorder(DockSection.FilesAndFolders, fileA.Id, -1).Change);
    }

    [TestMethod]
    public void RestoreRejectsDuplicateIdsTargetsAndUnsortedRecentEntries()
    {
        var firstId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var secondId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var first = Application(firstId, AppIdentity("Contoso.One"), "One");
        var second = Application(firstId, AppIdentity("Contoso.Two"), "Two");
        Assert.ThrowsExactly<ArgumentException>(() => DockState.Restore(
            [first, second], [], [], new DockPreferences()));

        var duplicateTarget = Application(secondId, AppIdentity("Contoso.One"), "One duplicate");
        Assert.ThrowsExactly<ArgumentException>(() => DockState.Restore(
            [first, duplicateTarget], [], [], new DockPreferences()));

        var older = Recent(Guid.NewGuid(), "Contoso.Older", "Older", Epoch);
        var newer = Recent(Guid.NewGuid(), "Contoso.Newer", "Newer", Epoch.AddMinutes(1));
        Assert.ThrowsExactly<ArgumentException>(() => DockState.Restore(
            [], [older, newer], [], new DockPreferences()));
    }

    private static DockTargetIdentity AppIdentity(string value) =>
        DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, value);

    private static DockProjectItem Application(Guid id, DockTargetIdentity identity, string name) => new(
        DockItemId.FromGuid(id), DockProjectItemKind.Application, DockSection.PinnedApplications,
        name, identity);

    private static DockProjectItem Recent(Guid id, string identity, string name, DateTimeOffset lastUsed) => new(
        DockItemId.FromGuid(id), DockProjectItemKind.Application, DockSection.RecentApplications,
        name, AppIdentity(identity), lastUsed);

    private static DockProjectItem File(Guid id, string path, string name) => new(
        DockItemId.FromGuid(id), DockProjectItemKind.File, DockSection.FilesAndFolders,
        name, DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath, path));
}
