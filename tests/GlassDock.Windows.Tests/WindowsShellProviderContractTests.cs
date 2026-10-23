using System.Collections.Generic;
using GlassDock.Core;
using GlassDock.Windows.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Windows.Tests;

[TestClass]
public sealed class WindowsShellProviderContractTests
{
    [TestMethod]
    public void AppUserModelIdDescriptorRetainsStableIdentityAndTrimsOnlyDisplayName()
    {
        var descriptor = WindowsShellApplicationDescriptor.FromAppUserModelId("  Editor  ", "Contoso.Editor");
        var caseVariant = DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, "contoso.editor");

        Assert.AreEqual("Editor", descriptor.DisplayName);
        Assert.AreEqual("Contoso.Editor", descriptor.TargetIdentity.Value);
        Assert.AreEqual(DockIdentityScheme.WindowsAppUserModelId, descriptor.TargetIdentity.Scheme);
        Assert.AreEqual(descriptor.TargetIdentity, caseVariant);
    }

    [TestMethod]
    public void ShellLinkAliasesDeduplicateOnlyWhenTheProviderSuppliesTheSameCanonicalKey()
    {
        var first = WindowsShellApplicationDescriptor.FromShellLink(
            "Editor", @"D:\Apps\Editor.lnk", "provider:editor-target-42");
        var alias = WindowsShellApplicationDescriptor.FromShellLink(
            "Editor shortcut", @"D:\UserLinks\Editor-copy.lnk", "provider:editor-target-42");
        var unrelatedCaseVariant = WindowsShellApplicationDescriptor.FromShellLink(
            "Editor", @"D:\Apps\Editor.lnk", "PROVIDER:EDITOR-TARGET-42");

        Assert.AreNotEqual(first.TargetIdentity.Value, alias.TargetIdentity.Value);
        Assert.AreEqual(first.TargetIdentity, alias.TargetIdentity);
        Assert.IsFalse(first.TargetIdentity.Equals(unrelatedCaseVariant),
            "Shell Link keys are provider-defined and must not be lowercased generically.");
    }

    [TestMethod]
    public void PackageFamilyNameAloneCannotBecomeAnApplicationDescriptor()
    {
        var packageFamilyOnly = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsPackageFamilyName,
            "Contoso_1234567890abc");

        Assert.ThrowsExactly<ArgumentException>(() =>
            new WindowsShellApplicationDescriptor("Contoso", packageFamilyOnly));
    }

    [TestMethod]
    public void DescriptorProjectsToPinnedCoreItemWithoutInventingRecentActivity()
    {
        var id = DockItemId.Parse("11111111-1111-1111-1111-111111111111");
        var descriptor = WindowsShellApplicationDescriptor.FromAppUserModelId("Editor", "Contoso.Editor");

        var item = descriptor.CreatePinnedItem(id);

        Assert.AreEqual(id, item.Id);
        Assert.AreEqual(DockProjectItemKind.Application, item.Kind);
        Assert.AreEqual(DockSection.PinnedApplications, item.Section);
        Assert.IsNull(item.LastUsedUtc);
        Assert.AreEqual(descriptor.TargetIdentity, item.TargetIdentity);
    }

    [TestMethod]
    public void CatalogResultSnapshotsEntriesAndExposesAnImmutableList()
    {
        var source = new List<WindowsShellApplicationDescriptor>
        {
            WindowsShellApplicationDescriptor.FromAppUserModelId("Editor", "Contoso.Editor")
        };
        var result = WindowsShellCatalogResult.Succeeded(source);
        source.Clear();

        Assert.AreEqual(WindowsShellCatalogStatus.Succeeded, result.Status);
        Assert.AreEqual(1, result.Applications.Count);
        Assert.ThrowsExactly<NotSupportedException>(() =>
            ((IList<WindowsShellApplicationDescriptor>)result.Applications).Clear());
    }

    [TestMethod]
    public void FailedCatalogResultCarriesDiagnosticAndNoPartialItems()
    {
        var result = WindowsShellCatalogResult.Failed(WindowsShellCatalogStatus.AccessDenied, "DiscoveryDenied");

        Assert.AreEqual(WindowsShellCatalogStatus.AccessDenied, result.Status);
        Assert.AreEqual("DiscoveryDenied", result.DiagnosticCode);
        Assert.AreEqual(0, result.Applications.Count);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            WindowsShellCatalogResult.Failed(WindowsShellCatalogStatus.Succeeded, "InvalidSuccessFactory"));
    }

    [TestMethod]
    public void IconAssetCopiesEncodedBytesAndRequiresPngSignature()
    {
        byte[] encoded = [137, 80, 78, 71, 13, 10, 26, 10, 1, 2];
        var asset = new WindowsShellIconAsset(encoded);
        encoded[0] = 0;

        Assert.AreEqual((byte)137, asset.EncodedPng.Span[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new WindowsShellIconAsset(new byte[] { 0, 1, 2 }));
    }

    [TestMethod]
    public void IconResultDistinguishesMissingAndAccessDeniedFromFound()
    {
        var missing = WindowsShellIconResult.Unavailable(WindowsShellIconStatus.NotFound);
        var denied = WindowsShellIconResult.Unavailable(WindowsShellIconStatus.AccessDenied, "IconAccessDenied");

        Assert.AreEqual(WindowsShellIconStatus.NotFound, missing.Status);
        Assert.IsNull(missing.Asset);
        Assert.AreEqual(WindowsShellIconStatus.AccessDenied, denied.Status);
        Assert.AreEqual("IconAccessDenied", denied.DiagnosticCode);
        Assert.ThrowsExactly<ArgumentException>(() =>
            WindowsShellIconResult.Unavailable(WindowsShellIconStatus.Failed));
    }
}
