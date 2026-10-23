using GlassDock.Core;
using GlassDock.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.App.Tests;

[TestClass]
public sealed class DockApplicationSessionTests
{
    [TestMethod]
    public async Task CatalogRefreshDoesNotAutopinAndRepeatedPinKeepsStateIdAndOrder()
    {
        var provider = new FakeProvider();
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var original = session.State;
        var app = new DockApplicationOption("Editor", Aumid("Contoso!Editor"));
        provider.Catalog = DockApplicationCatalogResult.Succeeded([app]);

        await session.RefreshCatalogAsync();

        Assert.AreSame(original, session.State);
        Assert.AreEqual(1, session.State.PinnedApplications.Count);
        Assert.AreEqual(0, store.SaveCount);

        var added = session.PinApplication(app);
        var pinned = session.State.PinnedApplications[1];
        provider.Catalog = DockApplicationCatalogResult.Succeeded([
            new DockApplicationOption("Editor renamed", Aumid("contoso!editor"))]);
        await session.RefreshCatalogAsync();
        var duplicate = session.PinApplication(session.Catalog.Applications.Single());

        Assert.IsTrue(added.Succeeded);
        Assert.IsTrue(added.Changed);
        Assert.AreEqual(DockStateChange.Added, added.Change);
        Assert.IsTrue(duplicate.Succeeded);
        Assert.IsFalse(duplicate.Changed);
        Assert.AreEqual(DockStateChange.AlreadyPresent, duplicate.Change);
        Assert.AreEqual(2, session.State.PinnedApplications.Count);
        Assert.AreEqual(pinned.Id, session.State.PinnedApplications[1].Id);
        Assert.AreEqual("Editor", session.State.PinnedApplications[1].DisplayName);
        Assert.AreEqual(1, store.SaveCount);
    }

    [TestMethod]
    public void ReorderAndUnpinPersistOnlyDockReferences()
    {
        var provider = new FakeProvider();
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var first = new DockApplicationOption("First", Aumid("Contoso!First"));
        var second = new DockApplicationOption("Second", Aumid("Contoso!Second"));
        session.PinApplication(first);
        session.PinApplication(second);
        var firstItem = session.State.PinnedApplications.Single(item => item.TargetIdentity == first.TargetIdentity);
        var secondItem = session.State.PinnedApplications.Single(item => item.TargetIdentity == second.TargetIdentity);

        var reordered = session.Reorder(DockSection.PinnedApplications, secondItem.Id, 1);
        var removed = session.RemoveReference(firstItem.Id);

        Assert.IsTrue(reordered.Succeeded);
        Assert.IsTrue(removed.Succeeded);
        Assert.AreEqual(DockStateChange.Reordered, reordered.Change);
        Assert.AreEqual(DockStateChange.Removed, removed.Change);
        Assert.AreEqual("Second", session.State.PinnedApplications[^1].DisplayName);
        Assert.AreEqual(2, session.State.PinnedApplications.Count);
        Assert.AreEqual(4, store.SaveCount);
        Assert.AreEqual(0, session.State.RecentApplications.Count);
        Assert.AreEqual(0, session.State.FilesAndFolders.Count);
    }

    [TestMethod]
    public void FailedPersistenceLeavesPublishedStateUnchanged()
    {
        var provider = new FakeProvider();
        var store = new FakeStateStore(DockState.CreateDefault()) { FailSave = true };
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var original = session.State;

        var result = session.PinApplication(new DockApplicationOption("Editor", Aumid("Contoso!Editor")));

        Assert.IsFalse(result.Succeeded);
        Assert.IsFalse(result.Changed);
        Assert.AreEqual("test-write-failure", result.DiagnosticCode);
        Assert.AreSame(original, session.State);
        Assert.AreEqual(1, session.State.PinnedApplications.Count);
    }

    [TestMethod]
    public void FileStoreRoundTripRestoresPinnedIdsOrderAndPreferences()
    {
        var testRoot = Environment.GetEnvironmentVariable("GLASSDOCK_TEST_DATA_ROOT")
            ?? Path.Combine(AppContext.BaseDirectory, "test-data");
        var isolatedDirectory = Path.Combine(testRoot, "app-session", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(isolatedDirectory);
        try
        {
            var store = new DockConfigurationFileStore(isolatedDirectory);
            var session = new DockApplicationSession(store.Load().State, store, new FakeProvider());
            var first = new DockApplicationOption("First", Aumid("Contoso!First"));
            var second = new DockApplicationOption("Second", Aumid("Contoso!Second"));
            session.PinApplication(first);
            session.PinApplication(second);

            var firstItem = session.State.PinnedApplications.Single(item => item.TargetIdentity == first.TargetIdentity);
            var secondItem = session.State.PinnedApplications.Single(item => item.TargetIdentity == second.TargetIdentity);
            Assert.IsTrue(session.Reorder(DockSection.PinnedApplications, secondItem.Id, 1).Succeeded);
            Assert.IsTrue(session.SetEdge(DockEdge.Left).Succeeded);

            var reopenedStore = new DockConfigurationFileStore(isolatedDirectory);
            var reopened = new DockApplicationSession(reopenedStore.Load().State, reopenedStore, new FakeProvider());

            Assert.AreEqual(DockEdge.Left, reopened.State.Preferences.Edge);
            Assert.AreEqual(secondItem.Id, reopened.State.PinnedApplications[1].Id);
            Assert.AreEqual(firstItem.Id, reopened.State.PinnedApplications[2].Id);
            Assert.AreEqual("Second", reopened.State.PinnedApplications[1].DisplayName);
            Assert.AreEqual("First", reopened.State.PinnedApplications[2].DisplayName);
        }
        finally
        {
            if (Directory.Exists(isolatedDirectory))
                Directory.Delete(isolatedDirectory, recursive: true);
        }
    }

    [TestMethod]
    public async Task CommandsRemainSeparateAndDoNotInventRecentActivity()
    {
        var provider = new FakeProvider
        {
            CommandResult = new DockCommandResult(
                DockCommandOutcome.ForegroundActivationDenied,
                "windows-foreground-policy-denied")
        };
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var app = new DockApplicationOption("Editor", Aumid("Contoso!Editor"));
        session.PinApplication(app);
        var item = session.State.PinnedApplications.Single(candidate => candidate.TargetIdentity == app.TargetIdentity);

        var result = await session.ExecuteAsync(item.Id, DockCommandKind.Activate);

        Assert.AreEqual(DockCommandKind.Activate, provider.LastCommand?.Kind);
        Assert.AreEqual(item.Id, provider.LastCommand?.ItemId);
        Assert.AreEqual(DockCommandOutcome.ForegroundActivationDenied, result.Outcome);
        Assert.AreEqual(0, session.State.RecentApplications.Count);
    }

    [TestMethod]
    public async Task ApplicationInvocationLaunchesOnlyWhenNoWindowWasFound()
    {
        var provider = new FakeProvider();
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var app = new DockApplicationOption("Editor", Aumid("Contoso!Editor"));
        session.PinApplication(app);
        var item = session.State.PinnedApplications.Single(candidate => candidate.TargetIdentity == app.TargetIdentity);
        provider.CommandHandler = command => command.Kind == DockCommandKind.Activate
            ? new DockCommandResult(DockCommandOutcome.TargetNotFound, "application-window-not-found")
            : new DockCommandResult(DockCommandOutcome.Completed);

        var result = await session.InvokeApplicationAsync(item.Id);

        CollectionAssert.AreEqual(
            new[] { DockCommandKind.Activate, DockCommandKind.Launch },
            provider.Commands.Select(command => command.Kind).ToArray());
        Assert.AreEqual(DockCommandOutcome.Completed, result.Outcome);
    }

    [TestMethod]
    public async Task ApplicationInvocationDoesNotLaunchWhenWindowsDeniesForegroundActivation()
    {
        var provider = new FakeProvider
        {
            CommandResult = new DockCommandResult(
                DockCommandOutcome.ForegroundActivationDenied,
                "windows-foreground-policy-denied")
        };
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);
        var app = new DockApplicationOption("Editor", Aumid("Contoso!Editor"));
        session.PinApplication(app);
        var item = session.State.PinnedApplications.Single(candidate => candidate.TargetIdentity == app.TargetIdentity);

        var result = await session.InvokeApplicationAsync(item.Id);

        CollectionAssert.AreEqual(new[] { DockCommandKind.Activate }, provider.Commands.Select(command => command.Kind).ToArray());
        Assert.AreEqual(DockCommandOutcome.ForegroundActivationDenied, result.Outcome);
    }

    [TestMethod]
    public async Task MissingDockItemReturnsTargetNotFoundWithoutCallingShell()
    {
        var provider = new FakeProvider();
        var store = new FakeStateStore(DockState.CreateDefault());
        var session = new DockApplicationSession(store.LoadState(), store, provider);

        var result = await session.ExecuteAsync(DockItemId.New(), DockCommandKind.Launch);

        Assert.AreEqual(DockCommandOutcome.TargetNotFound, result.Outcome);
        Assert.IsNull(provider.LastCommand);
    }

    private static DockTargetIdentity Aumid(string value) =>
        DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, value);

    private sealed class FakeProvider : IDockApplicationProvider
    {
        public DockApplicationCatalogResult Catalog { get; set; } = DockApplicationCatalogResult.Succeeded([]);

        public DockCommandResult CommandResult { get; set; } = new(DockCommandOutcome.Completed);

        public Func<DockCommand, DockCommandResult>? CommandHandler { get; set; }

        public List<DockCommand> Commands { get; } = [];

        public DockCommand? LastCommand { get; private set; }

        public ValueTask<DockApplicationCatalogResult> DiscoverDockApplicationsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(Catalog);
        }

        public ValueTask<DockApplicationIconResult> ResolveApplicationIconAsync(
            DockTargetIdentity targetIdentity,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(DockApplicationIconResult.Unavailable(DockApplicationIconStatus.NotFound));
        }

        public ValueTask<DockCommandResult> ExecuteAsync(DockCommand command, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastCommand = command;
            Commands.Add(command);
            return ValueTask.FromResult(CommandHandler?.Invoke(command) ?? CommandResult);
        }
    }

    private sealed class FakeStateStore(DockState initialState) : IDockStateStore
    {
        private DockState _state = initialState;

        public bool FailSave { get; init; }

        public int SaveCount { get; private set; }

        public DockState LoadState() => _state;

        public DockStateStoreSaveResult SaveState(DockState state)
        {
            SaveCount++;
            if (FailSave)
                return new DockStateStoreSaveResult(false, "test-write-failure");
            _state = state;
            return new DockStateStoreSaveResult(true);
        }
    }
}
