using GlassDock.App;
using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.App.Tests;

[TestClass]
public sealed class DockNativeRolePreviewSeedTests
{
    [TestMethod]
    public void SeedsFourVisualRolesWithoutChangingTheirWindowsTargetsOrProductDefaults()
    {
        var original = DockState.CreateDefault();
        var originalFinder = original.PinnedApplications.Single();

        var result = DockNativeRolePreviewSeed.Apply(original);

        Assert.IsTrue(result.Changed);
        Assert.AreEqual(1, original.PinnedApplications.Count, "The seed must not mutate the caller's immutable state.");
        Assert.AreEqual("File Explorer", originalFinder.DisplayName, "The standard Core default remains Windows Explorer.");
        Assert.AreEqual(2, result.State.PinnedApplications.Count);
        Assert.AreEqual(2, result.State.FilesAndFolders.Count);

        var finder = result.State.PinnedApplications[0];
        var settings = result.State.PinnedApplications[1];
        var downloads = result.State.FilesAndFolders[0];
        var trash = result.State.FilesAndFolders[1];

        Assert.AreEqual(originalFinder.Id, finder.Id);
        Assert.AreEqual("Finder", finder.DisplayName);
        Assert.AreEqual(DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.file-manager"), finder.TargetIdentity);
        Assert.AreEqual("System Settings", settings.DisplayName);
        Assert.AreEqual(DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.settings"), settings.TargetIdentity);
        Assert.AreEqual(DockProjectItemKind.Folder, downloads.Kind);
        Assert.AreEqual(DockIdentityScheme.WindowsKnownFolderId, downloads.TargetIdentity.Scheme);
        Assert.AreEqual("downloads", downloads.TargetIdentity.ComparisonKey);
        Assert.AreEqual(DockProjectItemKind.SystemRole, trash.Kind);
        Assert.AreEqual(DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.recycle-bin"), trash.TargetIdentity);
    }

    [TestMethod]
    public void ReapplyingPreviewSeedIsIdempotentAndConfigurationRoundTripPreservesIdsAndOrder()
    {
        var first = DockNativeRolePreviewSeed.Apply(DockState.CreateDefault()).State;
        var originalIds = first.PinnedApplications.Concat(first.FilesAndFolders).Select(item => item.Id).ToArray();
        var second = DockNativeRolePreviewSeed.Apply(first);
        var serialized = DockConfigurationJson.Serialize(second.State);
        var restored = DockConfigurationJson.Deserialize(serialized);

        Assert.IsFalse(second.Changed);
        Assert.AreSame(first, second.State);
        Assert.AreEqual(DockConfigurationLoadStatus.LoadedCurrentVersion, restored.Status);
        CollectionAssert.AreEqual(originalIds, restored.State.PinnedApplications.Concat(restored.State.FilesAndFolders).Select(item => item.Id).ToArray());
        CollectionAssert.AreEqual(
            first.PinnedApplications.Concat(first.FilesAndFolders).Select(item => item.TargetIdentity).ToArray(),
            restored.State.PinnedApplications.Concat(restored.State.FilesAndFolders).Select(item => item.TargetIdentity).ToArray());
        CollectionAssert.AreEqual(
            first.PinnedApplications.Concat(first.FilesAndFolders).Select(item => item.DisplayName).ToArray(),
            restored.State.PinnedApplications.Concat(restored.State.FilesAndFolders).Select(item => item.DisplayName).ToArray());
    }

    [TestMethod]
    public void PreviewSeedPreservesExistingPinnedOrderAndDoesNotPromoteRolesToDefaultState()
    {
        var defaultState = DockState.CreateDefault();
        var other = new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.Application, DockSection.PinnedApplications,
            "Editor", DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "Contoso!Editor"));
        var withUserPin = defaultState.AddPinnedApplication(other).State;
        var result = DockNativeRolePreviewSeed.Apply(withUserPin);

        Assert.AreEqual(other.Id, result.State.PinnedApplications[1].Id);
        Assert.AreEqual("Editor", result.State.PinnedApplications[1].DisplayName);
        Assert.AreEqual(3, result.State.PinnedApplications.Count);
        Assert.AreEqual(1, defaultState.PinnedApplications.Count, "The Core default state is not changed by the experimental seed.");
    }
}
