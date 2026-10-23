using GlassDock.Core;
using GlassDock.Windows.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Windows.Tests;

[TestClass]
public sealed class WindowsShellProviderIntegrationTests
{
    [TestMethod]
    public async Task RepeatedDiscoveryPreservesPinnedIdAndOrderingAndDoesNotAutopinCatalogEntries()
    {
        var iconSource = @"D:\StartMenu\Editor.lnk";
        var firstDescriptor = WindowsShellApplicationDescriptor.FromAppUserModelId(
            "Editor", "Contoso.Editor", iconSource);
        var nextDescriptor = WindowsShellApplicationDescriptor.FromAppUserModelId(
            "Editor renamed", "contoso.editor", iconSource);
        var discovery = new SequenceDiscovery(firstDescriptor, nextDescriptor);
        var provider = new WindowsShellProvider(discovery, new RecordingIconExtractor(), new WindowsShellCommandExecutor(new NoOpBackend()));
        var initialState = DockState.CreateDefault();
        Assert.AreEqual(1, initialState.PinnedApplications.Count, "Discovery results must not be pinned automatically.");

        var firstScan = await provider.DiscoverApplicationsAsync();
        var persistedId = DockItemId.Parse("22222222-2222-2222-2222-222222222222");
        var pinned = initialState.AddPinnedApplication(firstScan.Applications.Single().CreatePinnedItem(persistedId));
        var secondScan = await provider.DiscoverApplicationsAsync();
        var duplicatePin = pinned.State.AddPinnedApplication(
            secondScan.Applications.Single().CreatePinnedItem(DockItemId.New()));

        Assert.AreEqual(DockStateChange.AlreadyPresent, duplicatePin.Change);
        Assert.AreSame(pinned.State, duplicatePin.State);
        Assert.AreEqual(persistedId, duplicatePin.State.PinnedApplications[^1].Id);
        Assert.AreEqual(2, duplicatePin.State.PinnedApplications.Count);
        Assert.AreEqual("Editor", duplicatePin.State.PinnedApplications[^1].DisplayName,
            "A scan must not silently rewrite the user's persisted label or ordering.");
    }

    [TestMethod]
    public async Task IconResolutionUsesCatalogShellSourceInsteadOfInventingPlaceholder()
    {
        var iconSource = @"D:\StartMenu\Editor.lnk";
        var descriptor = WindowsShellApplicationDescriptor.FromAppUserModelId("Editor", "Contoso.Editor", iconSource);
        var extractor = new RecordingIconExtractor();
        var provider = new WindowsShellProvider(
            new SequenceDiscovery(descriptor),
            extractor,
            new WindowsShellCommandExecutor(new NoOpBackend()));

        await provider.DiscoverApplicationsAsync();
        var icon = await provider.ResolveIconAsync(descriptor.TargetIdentity);

        Assert.AreEqual(iconSource, extractor.LastParsingName);
        Assert.AreEqual(WindowsShellIconStatus.NotFound, icon.Status);
        Assert.IsNull(icon.Asset);
    }

    private sealed class SequenceDiscovery(params WindowsShellApplicationDescriptor[] descriptors) : IWindowsApplicationDiscovery
    {
        private int _index;

        public ValueTask<WindowsShellCatalogResult> DiscoverApplicationsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selected = descriptors[Math.Min(_index++, descriptors.Length - 1)];
            return ValueTask.FromResult(WindowsShellCatalogResult.Succeeded([selected]));
        }
    }

    private sealed class RecordingIconExtractor : IWindowsShellIconExtractor
    {
        public string? LastParsingName { get; private set; }

        public ValueTask<WindowsShellIconResult> ResolveIconAsync(
            string shellParsingName,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastParsingName = shellParsingName;
            return ValueTask.FromResult(WindowsShellIconResult.Unavailable(WindowsShellIconStatus.NotFound));
        }
    }

    private sealed class NoOpBackend : IWindowsShellCommandBackend
    {
        public DockCommandResult LaunchAppUserModelId(string appUserModelId) =>
            new(DockCommandOutcome.Unsupported, "not-invoked-in-test");

        public DockCommandResult LaunchShellLink(string shellLinkPath) =>
            new(DockCommandOutcome.Unsupported, "not-invoked-in-test");

        public DockCommandResult OpenPath(string path) =>
            new(DockCommandOutcome.Unsupported, "not-invoked-in-test");

        public bool TryFindApplicationWindow(DockTargetIdentity identity, out IntPtr windowHandle)
        {
            windowHandle = IntPtr.Zero;
            return false;
        }

        public bool TrySetForegroundWindow(IntPtr windowHandle) => false;
    }
}
