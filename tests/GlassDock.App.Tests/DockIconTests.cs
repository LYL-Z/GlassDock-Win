using System.Buffers.Binary;
using GlassDock.App;
using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.App.Tests;

[TestClass]
public sealed class DockIconTests
{
    [TestMethod]
    public async Task BuiltInFinderRoleUsesUserAssetWithoutChangingExplorerTargetIdentity()
    {
        var explorer = DockState.CreateDefault().PinnedApplications.Single();
        var assets = new FakeIconAssetReader();
        assets.Add("finder.png", DockIconAsset.Create(ValidPng(), DockIconFormat.Png));
        var shell = new FakeApplicationProvider();
        var manifest = new DockIconManifest(mappings:
        [
            new DockIconMapping(DockIconSelector.ForRole(DockIconRole.Finder),
            [new DockIconAssetVariant("finder.png", sourceNote: "user-provided reference", redistributionStatus: DockIconRedistributionStatus.LocalUseOnly)])
        ]);
        var resolver = new DockIconResolver(manifest, assets, shell);

        var result = await resolver.ResolveAsync(explorer, DockIconTheme.Dark);

        Assert.AreEqual(DockApplicationIconStatus.Found, result.Status);
        Assert.AreEqual(DockIconResolutionSource.UserRoleOverride, result.Source);
        Assert.AreEqual(DockIconRole.Finder, result.Role);
        Assert.AreEqual(DockIdentityScheme.WindowsSystemRole, explorer.TargetIdentity.Scheme);
        Assert.AreEqual("windows.file-manager", explorer.TargetIdentity.ComparisonKey);
        Assert.AreEqual(0, shell.IconResolveCount);
    }

    [TestMethod]
    public async Task DownloadsRoleRecognizesKnownFolderGuidAndKeepsTargetIdentity()
    {
        var downloadsIdentity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsKnownFolderId,
            "{374DE290-123F-4565-9164-39C4925E467B}");
        var item = new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.Folder, DockSection.FilesAndFolders,
            "Downloads", downloadsIdentity);
        var assets = new FakeIconAssetReader();
        assets.Add("downloads.png", DockIconAsset.Create(ValidPng(), DockIconFormat.Png));
        var resolver = new DockIconResolver(
            new DockIconManifest(mappings:
            [new DockIconMapping(DockIconSelector.ForRole(DockIconRole.Downloads), [new DockIconAssetVariant("downloads.png")])]),
            assets,
            new FakeApplicationProvider());

        var result = await resolver.ResolveAsync(item);

        Assert.AreEqual(DockIconRole.Downloads, result.Role);
        Assert.AreEqual(DockIconResolutionSource.UserRoleOverride, result.Source);
        Assert.AreEqual(downloadsIdentity, item.TargetIdentity);
    }

    [TestMethod]
    public async Task SettingsAndTrashSystemRolesResolveSeparatelyFromTheirWindowsShellTargets()
    {
        var settingsItem = new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.SystemRole, DockSection.PinnedApplications,
            "System Settings", DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.settings"));
        var trashItem = new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.SystemRole, DockSection.FilesAndFolders,
            "Trash", DockTargetIdentity.Create(DockIdentityScheme.WindowsSystemRole, "windows.recycle-bin"));
        var assets = new FakeIconAssetReader();
        assets.Add("settings.png", DockIconAsset.Create(ValidPng(2, 2), DockIconFormat.Png));
        assets.Add("trash-empty.png", DockIconAsset.Create(ValidPng(3, 2), DockIconFormat.Png));
        var manifest = new DockIconManifest(mappings:
        [
            new DockIconMapping(DockIconSelector.ForRole(DockIconRole.SystemSettings), [new DockIconAssetVariant("settings.png")]),
            new DockIconMapping(DockIconSelector.ForRole(DockIconRole.Trash),
            [new DockIconAssetVariant("trash-empty.png", state: DockIconVisualState.Empty)])
        ]);
        var resolver = new DockIconResolver(manifest, assets, new FakeApplicationProvider(), new FakeVisualStateProvider(DockIconVisualState.Full));

        var settings = await resolver.ResolveAsync(settingsItem);
        var fullTrash = await resolver.ResolveAsync(trashItem);

        Assert.AreEqual(DockIconRole.SystemSettings, settings.Role);
        Assert.AreEqual(DockIconResolutionSource.UserRoleOverride, settings.Source);
        Assert.AreEqual(DockIconRole.Trash, fullTrash.Role);
        Assert.AreEqual(DockApplicationIconStatus.NotFound, fullTrash.Status,
            "An empty-trash image must not be shown for a non-empty Recycle Bin.");
    }

    [TestMethod]
    public async Task ItemOverridePrecedesTargetAndRoleOverrides()
    {
        var item = CreateApplicationItem("Contoso.Editor");
        var assets = new FakeIconAssetReader();
        assets.Add("item.png", DockIconAsset.Create(ValidPng(3, 2), DockIconFormat.Png));
        assets.Add("target.png", DockIconAsset.Create(ValidPng(4, 2), DockIconFormat.Png));
        assets.Add("role.png", DockIconAsset.Create(ValidPng(5, 2), DockIconFormat.Png));
        var manifest = new DockIconManifest(
            roleAssignments: [new DockIconRoleAssignment(DockIconSelector.ForTarget(item.TargetIdentity), DockIconRole.Finder)],
            mappings:
            [
                new DockIconMapping(DockIconSelector.ForItem(item.Id), [new DockIconAssetVariant("item.png")]),
                new DockIconMapping(DockIconSelector.ForTarget(item.TargetIdentity), [new DockIconAssetVariant("target.png")]),
                new DockIconMapping(DockIconSelector.ForRole(DockIconRole.Finder), [new DockIconAssetVariant("role.png")])
            ]);
        var resolver = new DockIconResolver(manifest, assets, new FakeApplicationProvider());

        var result = await resolver.ResolveAsync(item);

        Assert.AreEqual(DockIconResolutionSource.UserItemOverride, result.Source);
        Assert.AreEqual(3u, BinaryPrimitives.ReadUInt32BigEndian(result.Asset!.EncodedBytes.Span.Slice(16, 4)));
    }

    [TestMethod]
    public async Task MissingHigherPriorityAssetFallsThroughToValidRoleAssetAndThemeStateAreSelected()
    {
        var item = CreateApplicationItem("Contoso.Settings");
        var assets = new FakeIconAssetReader();
        assets.Add("role-dark-full.png", DockIconAsset.Create(ValidPng(6, 2), DockIconFormat.Png));
        var manifest = new DockIconManifest(
            roleAssignments: [new DockIconRoleAssignment(DockIconSelector.ForTarget(item.TargetIdentity), DockIconRole.Trash)],
            mappings:
            [
                new DockIconMapping(DockIconSelector.ForItem(item.Id), [new DockIconAssetVariant("missing-item.png")]),
                new DockIconMapping(DockIconSelector.ForRole(DockIconRole.Trash),
                [
                    new DockIconAssetVariant("role-dark-full.png", DockIconTheme.Dark, DockIconVisualState.Full),
                    new DockIconAssetVariant("unused-light-empty.png", DockIconTheme.Light, DockIconVisualState.Empty)
                ])
            ]);
        var state = new FakeVisualStateProvider(DockIconVisualState.Full);
        var shell = new FakeApplicationProvider();
        var resolver = new DockIconResolver(manifest, assets, shell, state);

        var result = await resolver.ResolveAsync(item, DockIconTheme.Dark);

        Assert.AreEqual(DockIconResolutionSource.UserRoleOverride, result.Source);
        Assert.AreEqual(DockIconRole.Trash, state.LastRole);
        Assert.AreEqual(0, shell.IconResolveCount);
    }

    [TestMethod]
    public async Task MissingCustomAssetFallsBackToWindowsShellIcon()
    {
        var item = CreateApplicationItem("Contoso.MissingIcon");
        var shell = new FakeApplicationProvider
        {
            IconResult = DockApplicationIconResult.Found(new DockApplicationIconAsset(ValidPng()))
        };
        var manifest = new DockIconManifest(mappings:
        [new DockIconMapping(DockIconSelector.ForTarget(item.TargetIdentity), [new DockIconAssetVariant("absent.png")])]);
        var resolver = new DockIconResolver(manifest, new FakeIconAssetReader(), shell);

        var result = await resolver.ResolveAsync(item);

        Assert.AreEqual(DockIconResolutionSource.WindowsShell, result.Source);
        Assert.AreEqual("custom-icon-not-found", result.DiagnosticCode);
        Assert.AreEqual(1, shell.IconResolveCount);
    }

    [TestMethod]
    public void ManifestRoundTripPreservesRoleTargetSelectorVariantsAndRedistributionState()
    {
        var target = AppIdentity("Contoso.Editor");
        var manifest = new DockIconManifest(
            roleAssignments: [new DockIconRoleAssignment(DockIconSelector.ForTarget(target), DockIconRole.SystemSettings)],
            mappings:
            [
                new DockIconMapping(DockIconSelector.ForRole(DockIconRole.SystemSettings),
                [new DockIconAssetVariant("settings/dark.ico", DockIconTheme.Dark, sourceNote: "local user asset", redistributionStatus: DockIconRedistributionStatus.Unverified)])
            ]);

        var json = DockIconManifestJson.Serialize(manifest);
        var decoded = DockIconManifestJson.Deserialize(json);

        Assert.AreEqual(DockIconManifestDecodeStatus.Loaded, decoded.Status);
        Assert.AreEqual(DockIconRole.SystemSettings, decoded.Manifest.RoleAssignments.Single().Role);
        Assert.AreEqual(target, decoded.Manifest.RoleAssignments.Single().Selector.TargetIdentity);
        Assert.AreEqual(DockIconRedistributionStatus.Unverified,
            decoded.Manifest.Mappings.Single().Variants.Single().RedistributionStatus);
        Assert.AreEqual("settings/dark.ico", decoded.Manifest.Mappings.Single().Variants.Single().RelativePath);
    }

    [TestMethod]
    public void InvalidManifestAndAssetPathsFailClosed()
    {
        var invalidVersion = DockIconManifestJson.Deserialize("{\"schemaVersion\":77,\"roleAssignments\":[],\"mappings\":[]}");

        Assert.AreEqual(DockIconManifestDecodeStatus.UnsupportedVersion, invalidVersion.Status);
        Assert.ThrowsExactly<ArgumentException>(() => new DockIconAssetVariant("../outside.png"));
        Assert.ThrowsExactly<ArgumentException>(() => new DockIconAssetVariant("C:/private/icon.png"));
        Assert.ThrowsExactly<ArgumentException>(() => DockIconAsset.Create(new byte[] { 1, 2, 3, 4 }, DockIconFormat.Png));
    }

    [TestMethod]
    public void PngAndIcoHeadersAreBoundedAndAcceptedWithoutCroppingOrReencoding()
    {
        var png = ValidPng(24, 12);
        var ico = ValidIco(64, 32);

        var pngAsset = DockIconAsset.Create(png, DockIconFormat.Png);
        var icoAsset = DockIconAsset.Create(ico, DockIconFormat.Ico);

        Assert.AreEqual(DockIconFormat.Png, pngAsset.Format);
        Assert.AreEqual(DockIconFormat.Ico, icoAsset.Format);
        CollectionAssert.AreEqual(png, pngAsset.EncodedBytes.ToArray());
        CollectionAssert.AreEqual(ico, icoAsset.EncodedBytes.ToArray());
        Assert.ThrowsExactly<ArgumentException>(() => DockIconAsset.Create(ValidPng(2048, 1), DockIconFormat.Png));
    }

    private static DockProjectItem CreateApplicationItem(string appId) => new(
        DockItemId.New(),
        DockProjectItemKind.Application,
        DockSection.PinnedApplications,
        "Editor",
        AppIdentity(appId));

    private static DockTargetIdentity AppIdentity(string value) =>
        DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, value);

    private static byte[] ValidPng(uint width = 1, uint height = 1)
    {
        var bytes = new byte[33];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12, 4));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }

    private static byte[] ValidIco(byte width, byte height)
    {
        var bytes = new byte[30];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4, 2), 1);
        bytes[6] = width;
        bytes[7] = height;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(10, 2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(12, 2), 32);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(14, 4), 8);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(18, 4), 22);
        return bytes;
    }

    private sealed class FakeIconAssetReader : IDockIconAssetReader
    {
        private readonly Dictionary<string, DockIconAsset> _assets = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string path, DockIconAsset asset) => _assets[path] = asset;

        public ValueTask<DockIconAssetReadResult> ReadLocalAssetAsync(string relativePath, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_assets.TryGetValue(relativePath, out var asset)
                ? DockIconAssetReadResult.Found(asset)
                : DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.NotFound, "custom-icon-not-found"));
        }
    }

    private sealed class FakeVisualStateProvider(DockIconVisualState state) : IDockIconVisualStateProvider
    {
        public DockIconRole? LastRole { get; private set; }

        public ValueTask<DockIconVisualState> GetVisualStateAsync(DockIconRole role, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRole = role;
            return ValueTask.FromResult(state);
        }
    }

    private sealed class FakeApplicationProvider : IDockApplicationProvider
    {
        public int IconResolveCount { get; private set; }
        public DockApplicationIconResult IconResult { get; init; } = DockApplicationIconResult.Unavailable(DockApplicationIconStatus.NotFound);

        public ValueTask<DockApplicationCatalogResult> DiscoverDockApplicationsAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(DockApplicationCatalogResult.Succeeded([]));

        public ValueTask<DockApplicationIconResult> ResolveApplicationIconAsync(DockTargetIdentity targetIdentity, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            IconResolveCount++;
            return ValueTask.FromResult(IconResult);
        }

        public ValueTask<DockCommandResult> ExecuteAsync(DockCommand command, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new DockCommandResult(DockCommandOutcome.Unsupported, "not-used-in-test"));
    }
}
