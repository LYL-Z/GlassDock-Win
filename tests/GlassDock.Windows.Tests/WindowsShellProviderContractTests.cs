using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GlassDock.App;
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
    public void AumidShortcutKeepsItsLaunchLocatorWhileDeduplicatingByAumid()
    {
        var shortcut = WindowsShellApplicationDescriptor.FromAppUserModelIdShortcut(
            "Editor", "Contoso.Editor", @"D:\Menu\Editor.lnk");
        var appsFolderEntry = WindowsShellApplicationDescriptor.FromAppUserModelId(
            "Editor", "contoso.editor", @"shell:AppsFolder\contoso.editor");

        Assert.AreEqual(@"D:\Menu\Editor.lnk", shortcut.TargetIdentity.Value);
        Assert.AreEqual("Contoso.Editor", shortcut.TargetIdentity.ComparisonKey);
        Assert.AreEqual(shortcut.TargetIdentity, appsFolderEntry.TargetIdentity);
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
        Assert.AreEqual(descriptor.TargetIdentity.Value, descriptor.IconSource);
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
        byte[] encoded = MinimalPngHeader(16, 16);
        var asset = new WindowsShellIconAsset(encoded);
        encoded[0] = 0;

        Assert.AreEqual((byte)137, asset.EncodedPng.Span[0]);
        Assert.ThrowsExactly<ArgumentException>(() => new WindowsShellIconAsset(new byte[] { 0, 1, 2 }));
        Assert.ThrowsExactly<ArgumentException>(() => new WindowsShellIconAsset(MinimalPngHeader(2048, 2048)));
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

    [TestMethod]
    public void DownloadsKnownFolderRecognitionUsesKnownFolderIdentityInsteadOfSynthesizingAPath()
    {
        var byName = DockTargetIdentity.Create(DockIdentityScheme.WindowsKnownFolderId, "Downloads", "FOLDERID_Downloads");
        var byGuid = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsKnownFolderId,
            "{374DE290-123F-4565-9164-39C4925E467B}",
            "374de290-123f-4565-9164-39c4925e467b");
        var unrelated = DockTargetIdentity.Create(DockIdentityScheme.WindowsKnownFolderId, "Documents", "FOLDERID_Documents");

        Assert.IsTrue(WindowsKnownFolderPathProvider.IsDownloadsIdentity(byName));
        Assert.IsTrue(WindowsKnownFolderPathProvider.IsDownloadsIdentity(byGuid));
        Assert.IsFalse(WindowsKnownFolderPathProvider.IsDownloadsIdentity(unrelated));
    }

    [TestMethod]
    public async Task RecycleBinStateMapsActualQueryCountsAndFailuresWithoutPretendingUnknownIsEmpty()
    {
        var empty = new WindowsDockIconVisualStateProvider(new FakeRecycleBinStatusQuery(new(true, 0)));
        var full = new WindowsDockIconVisualStateProvider(new FakeRecycleBinStatusQuery(new(true, 3)));
        var unknown = new WindowsDockIconVisualStateProvider(new FakeRecycleBinStatusQuery(new(false, 0, unchecked((int)0x80004005))));
        var negative = new WindowsDockIconVisualStateProvider(new FakeRecycleBinStatusQuery(new(true, -1)));

        Assert.AreEqual(DockIconVisualState.Empty, await empty.GetVisualStateAsync(DockIconRole.Trash));
        Assert.AreEqual(DockIconVisualState.Full, await full.GetVisualStateAsync(DockIconRole.Trash));
        Assert.AreEqual(DockIconVisualState.Unknown, await unknown.GetVisualStateAsync(DockIconRole.Trash));
        Assert.AreEqual(DockIconVisualState.Unknown, await negative.GetVisualStateAsync(DockIconRole.Trash));
    }

    [TestMethod]
    public async Task RecycleBinWaitTimeoutReturnsUnknownAndReusesOneStillRunningNativeQuery()
    {
        using var query = new BlockingRecycleBinStatusQuery(new WindowsRecycleBinStatusQueryResult(true, 4));
        var diagnostics = new List<WindowsRecycleBinQueryDiagnostic>();
        var provider = new WindowsDockIconVisualStateProvider(
            query,
            TimeSpan.FromMilliseconds(40),
            diagnostics.Add);

        try
        {
            var first = await provider.GetVisualStateAsync(DockIconRole.Trash);
            var second = await provider.GetVisualStateAsync(DockIconRole.Trash);

            Assert.AreEqual(DockIconVisualState.Unknown, first);
            Assert.AreEqual(DockIconVisualState.Unknown, second);
            Assert.AreEqual(1, query.CallCount, "A timed-out native call must not fan out into repeated background tasks.");
            Assert.IsTrue(diagnostics.Any(item => item.Event == "recycle-bin-query-timeout" && item.NativeCallStillRunning));
        }
        finally
        {
            query.Release();
        }

        Assert.IsTrue(query.WaitUntilFinished(TimeSpan.FromSeconds(1)));
        Assert.AreEqual(DockIconVisualState.Full, await provider.GetVisualStateAsync(DockIconRole.Trash));
        Assert.AreEqual(2, query.CallCount, "A new query may start only after the previous synchronous call has finished.");
    }

    [TestMethod]
    public async Task RecycleBinWaitCancellationCancelsOnlyTheWaitAndDoesNotPretendNativeCallStopped()
    {
        using var query = new BlockingRecycleBinStatusQuery(new WindowsRecycleBinStatusQueryResult(true, 0));
        var diagnostics = new List<WindowsRecycleBinQueryDiagnostic>();
        var provider = new WindowsDockIconVisualStateProvider(
            query,
            TimeSpan.FromSeconds(2),
            diagnostics.Add);
        using var cancellation = new CancellationTokenSource();

        try
        {
            var pending = provider.GetVisualStateAsync(DockIconRole.Trash, cancellation.Token).AsTask();
            Assert.IsTrue(query.WaitUntilStarted(TimeSpan.FromSeconds(1)));
            cancellation.Cancel();

            var cancellationPropagated = false;
            try
            {
                await pending;
            }
            catch (OperationCanceledException)
            {
                cancellationPropagated = true;
            }
            Assert.IsTrue(cancellationPropagated, "Cancelling the wait must propagate an OperationCanceledException.");
            Assert.IsTrue(diagnostics.Any(item => item.Event == "recycle-bin-wait-cancelled" && item.NativeCallStillRunning));
            Assert.AreEqual(1, query.CallCount);
        }
        finally
        {
            query.Release();
        }
        Assert.IsTrue(query.WaitUntilFinished(TimeSpan.FromSeconds(1)));
    }

    [TestMethod]
    public async Task SlowTrashStateQueryDoesNotBlockOtherRolePreparation()
    {
        using var query = new BlockingRecycleBinStatusQuery(new WindowsRecycleBinStatusQueryResult(true, 2));
        var provider = new WindowsDockIconVisualStateProvider(query, TimeSpan.FromSeconds(2));
        var slowTrash = provider.GetVisualStateAsync(DockIconRole.Trash).AsTask();
        try
        {
            Assert.IsTrue(query.WaitUntilStarted(TimeSpan.FromSeconds(1)));
            var unrelated = await provider.GetVisualStateAsync(DockIconRole.Finder);

            Assert.AreEqual(DockIconVisualState.Default, unrelated);
            Assert.IsFalse(slowTrash.IsCompleted);
        }
        finally
        {
            query.Release();
        }

        Assert.IsTrue(query.WaitUntilFinished(TimeSpan.FromSeconds(1)));
        Assert.AreEqual(DockIconVisualState.Full, await slowTrash);
    }

    [TestMethod]
    public async Task RecycleBinQueryExceptionFallsBackToUnknown()
    {
        var provider = new WindowsDockIconVisualStateProvider(new ThrowingRecycleBinStatusQuery());

        Assert.AreEqual(DockIconVisualState.Unknown, await provider.GetVisualStateAsync(DockIconRole.Trash));
    }

    private sealed class FakeRecycleBinStatusQuery(WindowsRecycleBinStatusQueryResult result) : IWindowsRecycleBinStatusQuery
    {
        public WindowsRecycleBinStatusQueryResult QueryAllDrives() => result;
    }

    private sealed class ThrowingRecycleBinStatusQuery : IWindowsRecycleBinStatusQuery
    {
        public WindowsRecycleBinStatusQueryResult QueryAllDrives() => throw new InvalidOperationException("simulated query failure");
    }

    private sealed class BlockingRecycleBinStatusQuery(WindowsRecycleBinStatusQueryResult result) : IWindowsRecycleBinStatusQuery, IDisposable
    {
        private readonly ManualResetEventSlim _started = new();
        private readonly ManualResetEventSlim _release = new();
        private readonly ManualResetEventSlim _finished = new();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public WindowsRecycleBinStatusQueryResult QueryAllDrives()
        {
            Interlocked.Increment(ref _callCount);
            _started.Set();
            try
            {
                _release.Wait();
                return result;
            }
            finally
            {
                _finished.Set();
            }
        }

        public bool WaitUntilStarted(TimeSpan timeout) => _started.Wait(timeout);

        public bool WaitUntilFinished(TimeSpan timeout) => _finished.Wait(timeout);

        public void Release() => _release.Set();

        public void Dispose()
        {
            _started.Dispose();
            _release.Dispose();
            _finished.Dispose();
        }
    }

    private static byte[] MinimalPngHeader(uint width, uint height)
    {
        byte[] bytes = [137, 80, 78, 71, 13, 10, 26, 10, 0, 0, 0, 13, 73, 72, 68, 82, 0, 0, 0, 0, 0, 0, 0, 0];
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), height);
        return bytes;
    }
}
