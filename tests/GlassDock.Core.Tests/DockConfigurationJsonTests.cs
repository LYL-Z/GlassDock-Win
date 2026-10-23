using System.Text.Json;
using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Core.Tests;

[TestClass]
public sealed class DockConfigurationJsonTests
{
    private const string Version1Configuration = """
        {
          "schemaVersion": 1,
          "preferences": { "edge": "left", "showRecentApplications": false },
          "pinnedApplications": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "kind": "application",
              "displayName": "Editor",
              "identityScheme": "windowsAppUserModelId",
              "identityValue": "Contoso.Editor",
              "lastUsedUtc": null
            }
          ],
          "recentApplications": [],
          "filesAndFolders": []
        }
        """;

    private const string Version2Configuration = """
        {
          "schemaVersion": 2,
          "preferences": { "edge": "left", "showRecentApplications": false, "appearance": "dark" },
          "pinnedApplications": [
            {
              "id": "11111111-1111-1111-1111-111111111111",
              "kind": "application",
              "displayName": "Editor",
              "identityScheme": "windowsAppUserModelId",
              "identityValue": "Contoso.Editor",
              "lastUsedUtc": null
            }
          ],
          "recentApplications": [],
          "filesAndFolders": []
        }
        """;

    [TestMethod]
    public void SerializeRoundTripsSectionsStableIdsOrderAndPreferences()
    {
        var state = DockState.CreateDefault();
        var terminal = Application(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Contoso.Terminal", "Terminal");
        var editor = Application(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Contoso.Editor", "Editor");
        state = state.AddPinnedApplication(terminal).State;
        state = state.AddPinnedApplication(editor).State;
        state = state.Reorder(DockSection.PinnedApplications, editor.Id, 1).State;
        state = state.RecordRecentActivity(new DockRecentActivity(
            AppIdentity("Contoso.Browser"), "Browser", DateTimeOffset.Parse("2026-10-23T12:00:00Z"))).State;
        state = state.AddFileOrFolderReference(new DockProjectItem(
            DockItemId.FromGuid(Guid.Parse("55555555-5555-5555-5555-555555555555")),
            DockProjectItemKind.Folder,
            DockSection.FilesAndFolders,
            "Downloads",
            DockTargetIdentity.Create(DockIdentityScheme.WindowsKnownFolderId, "Downloads", "FOLDERID_Downloads"))).State;
        state = state.SetAppearance(DockAppearancePreference.Dark).State;
        state = state.SetEdge(DockEdge.Right).State;

        var json = DockConfigurationJson.Serialize(state);
        using var document = JsonDocument.Parse(json);
        var result = DockConfigurationJson.Deserialize(json);

        Assert.AreEqual(DockConfigurationJson.CurrentSchemaVersion,
            document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.AreEqual(DockConfigurationLoadStatus.LoadedCurrentVersion, result.Status);
        Assert.AreEqual(DockEdge.Right, result.State.Preferences.Edge);
        Assert.AreEqual(DockAppearancePreference.Dark, result.State.Preferences.Appearance);
        Assert.IsTrue(result.State.Preferences.ShowRecentApplications);
        CollectionAssert.AreEqual(state.PinnedApplications.Select(item => item.Id.ToString()).ToArray(),
            result.State.PinnedApplications.Select(item => item.Id.ToString()).ToArray());
        CollectionAssert.AreEqual(state.RecentApplications.Select(item => item.Id.ToString()).ToArray(),
            result.State.RecentApplications.Select(item => item.Id.ToString()).ToArray());
        CollectionAssert.AreEqual(state.FilesAndFolders.Select(item => item.Id.ToString()).ToArray(),
            result.State.FilesAndFolders.Select(item => item.Id.ToString()).ToArray());
        Assert.AreEqual(state.FilesAndFolders.Single().TargetIdentity.ComparisonKey,
            result.State.FilesAndFolders.Single().TargetIdentity.ComparisonKey);
    }

    [TestMethod]
    public void Version1MigratesToVersion3AndAddsFollowSystemAppearance()
    {
        var result = DockConfigurationJson.Deserialize(Version1Configuration);

        Assert.AreEqual(DockConfigurationLoadStatus.MigratedFromVersion1, result.Status);
        Assert.AreEqual(DockEdge.Left, result.State.Preferences.Edge);
        Assert.IsFalse(result.State.Preferences.ShowRecentApplications);
        Assert.AreEqual(DockAppearancePreference.FollowSystem, result.State.Preferences.Appearance);
        Assert.AreEqual("Editor", result.State.PinnedApplications.Single().DisplayName);
        Assert.AreEqual("Contoso.Editor", result.State.PinnedApplications.Single().TargetIdentity.ComparisonKey);

        using var migratedDocument = JsonDocument.Parse(DockConfigurationJson.Serialize(result.State));
        Assert.AreEqual(3, migratedDocument.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [TestMethod]
    public void Version2MigratesAndPreservesAppearanceWithValueAsComparisonKey()
    {
        var result = DockConfigurationJson.Deserialize(Version2Configuration);

        Assert.AreEqual(DockConfigurationLoadStatus.MigratedFromVersion2, result.Status);
        Assert.AreEqual(DockAppearancePreference.Dark, result.State.Preferences.Appearance);
        Assert.AreEqual("Contoso.Editor", result.State.PinnedApplications.Single().TargetIdentity.ComparisonKey);
        Assert.AreEqual(DockConfigurationLoadStatus.LoadedCurrentVersion,
            DockConfigurationJson.Deserialize(DockConfigurationJson.Serialize(result.State)).Status);
    }

    [TestMethod]
    public void MissingConfigurationReturnsApprovedDefaults()
    {
        var result = DockConfigurationJson.Deserialize(null);

        Assert.AreEqual(DockConfigurationLoadStatus.DefaultedMissing, result.Status);
        Assert.IsFalse(result.RequiresRecovery);
        Assert.AreEqual(DockEdge.Bottom, result.State.Preferences.Edge);
        Assert.IsTrue(result.State.Preferences.ShowRecentApplications);
        Assert.AreEqual("windows.file-manager", result.State.PinnedApplications.Single().TargetIdentity.Value);
        Assert.AreEqual(0, result.State.RecentApplications.Count);
    }

    [TestMethod]
    public void InvalidJsonRecoversToDefaultsWithDiagnostic()
    {
        const string invalid = "{ not-json";
        var result = DockConfigurationJson.Deserialize(invalid);

        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData, result.Status);
        Assert.AreEqual("InvalidJson", result.DiagnosticCode);
        Assert.IsTrue(result.RequiresRecovery);
        Assert.AreEqual(1, result.State.PinnedApplications.Count);
    }

    [TestMethod]
    public void InvalidModelDataIsRejectedAsAWholeRatherThanPartiallyLoaded()
    {
        var invalid = """
            {
              "schemaVersion": 2,
              "preferences": { "edge": "bottom", "showRecentApplications": true, "appearance": "followSystem" },
              "pinnedApplications": [
                { "id": "11111111-1111-1111-1111-111111111111", "kind": "application", "displayName": "One", "identityScheme": "windowsAppUserModelId", "identityValue": "Contoso.One", "lastUsedUtc": null },
                { "id": "11111111-1111-1111-1111-111111111111", "kind": "application", "displayName": "Two", "identityScheme": "windowsAppUserModelId", "identityValue": "Contoso.Two", "lastUsedUtc": null }
              ],
              "recentApplications": [],
              "filesAndFolders": []
            }
            """;

        var result = DockConfigurationJson.Deserialize(invalid);

        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData, result.Status);
        Assert.AreEqual("InvalidConfigurationData", result.DiagnosticCode);
        Assert.AreEqual(1, result.State.PinnedApplications.Count);
        Assert.AreEqual("windows.file-manager", result.State.PinnedApplications[0].TargetIdentity.Value);
    }

    [TestMethod]
    public void UnsupportedFutureVersionUsesDefaultsAndReportsRecoveryNeed()
    {
        var result = DockConfigurationJson.Deserialize("{\"schemaVersion\":99}");

        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredUnsupportedVersion, result.Status);
        Assert.AreEqual("UnsupportedSchemaVersion:99", result.DiagnosticCode);
        Assert.IsTrue(result.RequiresRecovery);
        Assert.AreEqual(1, result.State.PinnedApplications.Count);
    }

    [TestMethod]
    public void InvalidEnumAndMissingCollectionsAreNotSilentlyAccepted()
    {
        var invalidEnum = Version1Configuration.Replace("\"left\"", "99", StringComparison.Ordinal);
        var missingCollection = """
            { "schemaVersion": 2, "preferences": { "edge": "bottom", "showRecentApplications": true, "appearance": "followSystem" }, "pinnedApplications": [], "recentApplications": [] }
            """;

        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData,
            DockConfigurationJson.Deserialize(invalidEnum).Status);
        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData,
            DockConfigurationJson.Deserialize(missingCollection).Status);

        const string missingIdentityFields = """
            { "schemaVersion": 3, "preferences": { "edge": "bottom", "showRecentApplications": true, "appearance": "followSystem" }, "pinnedApplications": [ { "id": "11111111-1111-1111-1111-111111111111", "kind": "application", "displayName": "Editor", "identityScheme": "windowsAppUserModelId", "identityValue": "Contoso.Editor" } ], "recentApplications": [], "filesAndFolders": [] }
            """;
        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData,
            DockConfigurationJson.Deserialize(missingIdentityFields).Status);
    }

    [TestMethod]
    public void OversizedConfigurationIsRejectedBeforeJsonParsing()
    {
        var oversized = new string(' ', DockConfigurationJson.MaximumConfigurationCharacters + 1);
        var result = DockConfigurationJson.Deserialize(oversized);

        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData, result.Status);
        Assert.AreEqual("ConfigurationTooLarge", result.DiagnosticCode);
        Assert.IsTrue(result.RequiresRecovery);
        Assert.AreEqual(1, result.State.PinnedApplications.Count);
    }

    private static DockTargetIdentity AppIdentity(string value) =>
        DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, value);

    private static DockProjectItem Application(Guid id, string identity, string label) => new(
        DockItemId.FromGuid(id), DockProjectItemKind.Application, DockSection.PinnedApplications,
        label, AppIdentity(identity));
}
