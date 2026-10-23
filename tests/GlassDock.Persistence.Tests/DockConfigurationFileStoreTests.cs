using GlassDock.Core;
using GlassDock.Persistence;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Persistence.Tests;

[TestClass]
public sealed class DockConfigurationFileStoreTests
{
    private string _testDirectory = string.Empty;
    private string _testRoot = string.Empty;

    [TestInitialize]
    public void InitializeIsolatedDDriveTestDirectory()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GLASSDOCK_TEST_DATA_ROOT");
        Assert.IsFalse(string.IsNullOrWhiteSpace(configuredRoot),
            "Run tests through scripts/test.ps1 so data is isolated under the repository's ignored .local directory.");

        _testRoot = Path.GetFullPath(configuredRoot!);
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "GlassDock-Win.sln")))
            repository = repository.Parent;
        Assert.IsNotNull(repository, "Could not locate the repository root from the test assembly.");
        var expectedRoot = Path.GetFullPath(Path.Combine(repository!.FullName, ".local", "test-data"));
        Assert.AreEqual(expectedRoot, _testRoot, "Persistence tests must use the isolated test-data directory only.");

        _testDirectory = Path.Combine(_testRoot, $"config-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDirectory);
    }

    [TestCleanup]
    public void RemoveOnlyThisTestDirectory()
    {
        if (Directory.Exists(_testDirectory)
            && Path.GetFullPath(_testDirectory).StartsWith(
                _testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    [TestMethod]
    public void MissingConfigReturnsDefaultsAndSaveRoundTripsCompleteState()
    {
        var store = new DockConfigurationFileStore(_testDirectory);
        var missing = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.DefaultedMissing, missing.Status);
        Assert.AreEqual(1, missing.State.PinnedApplications.Count);
        Assert.IsFalse(File.Exists(store.ConfigurationFilePath));

        var state = CreateState("Editor", DockEdge.Left, DockAppearancePreference.Dark);
        Assert.IsTrue(store.Save(state).Succeeded);
        var loaded = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.LoadedPrimary, loaded.Status);
        Assert.AreEqual(DockEdge.Left, loaded.State.Preferences.Edge);
        Assert.AreEqual(DockAppearancePreference.Dark, loaded.State.Preferences.Appearance);
        CollectionAssert.AreEqual(state.PinnedApplications.Select(item => item.Id.ToString()).ToArray(),
            loaded.State.PinnedApplications.Select(item => item.Id.ToString()).ToArray());
        Assert.AreEqual(state.PinnedApplications[^1].TargetIdentity.ComparisonKey,
            loaded.State.PinnedApplications[^1].TargetIdentity.ComparisonKey);
    }

    [TestMethod]
    public void LegacyConfigIsReportedAsMigratedAndRewrittenOnlyAfterExplicitSave()
    {
        const string version1 = """
            { "schemaVersion": 1, "preferences": { "edge": "left", "showRecentApplications": false }, "pinnedApplications": [ { "id": "11111111-1111-1111-1111-111111111111", "kind": "application", "displayName": "Editor", "identityScheme": "windowsAppUserModelId", "identityValue": "Contoso.Editor", "lastUsedUtc": null } ], "recentApplications": [], "filesAndFolders": [] }
            """;
        var store = new DockConfigurationFileStore(_testDirectory);
        File.WriteAllText(store.ConfigurationFilePath, version1, new System.Text.UTF8Encoding(false));

        var migrated = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.MigratedPrimary, migrated.Status);
        Assert.AreEqual(DockConfigurationLoadStatus.MigratedFromVersion1, migrated.CodecStatus);
        Assert.IsTrue(migrated.RequiresWriteBack);
        Assert.AreEqual(DockAppearancePreference.FollowSystem, migrated.State.Preferences.Appearance);
        Assert.AreEqual(version1, File.ReadAllText(store.ConfigurationFilePath), "Load must not silently rewrite user data.");

        Assert.IsTrue(store.Save(migrated.State).Succeeded);
        Assert.AreEqual(DockConfigurationStoreLoadStatus.LoadedPrimary, store.Load().Status);
    }

    [TestMethod]
    public void CorruptPrimaryRecoversNewestValidBackupAndPreservesCorruptFile()
    {
        var store = new DockConfigurationFileStore(_testDirectory);
        var previous = CreateState("Previous", DockEdge.Bottom, DockAppearancePreference.FollowSystem);
        var current = CreateState("Current", DockEdge.Right, DockAppearancePreference.Light);
        Assert.IsTrue(store.Save(previous).Succeeded);
        Assert.IsTrue(store.Save(current).Succeeded);
        File.WriteAllText(store.ConfigurationFilePath, "{broken primary", new System.Text.UTF8Encoding(false));

        var recovered = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.RecoveredFromBackup1, recovered.Status);
        Assert.IsTrue(recovered.LoadedFromBackup);
        Assert.IsTrue(recovered.RequiresWriteBack);
        Assert.AreEqual(previous.PinnedApplications[^1].Id, recovered.State.PinnedApplications[^1].Id);
        Assert.AreEqual("{broken primary", File.ReadAllText(store.ConfigurationFilePath));
    }

    [TestMethod]
    public void BackupHistoryIsBoundedAndSecondBackupCanRecoverWhenNewerCopiesAreInvalid()
    {
        var store = new DockConfigurationFileStore(_testDirectory);
        var states = Enumerable.Range(1, 4)
            .Select(index => CreateState($"Editor {index}", DockEdge.Bottom, DockAppearancePreference.FollowSystem))
            .ToArray();
        foreach (var state in states)
            Assert.IsTrue(store.Save(state).Succeeded);

        var backup1 = Path.Combine(_testDirectory, DockConfigurationFileStore.Backup1FileName);
        var backup2 = Path.Combine(_testDirectory, DockConfigurationFileStore.Backup2FileName);
        Assert.AreEqual(2, Directory.GetFiles(_testDirectory, "dock-config.json.bak.*").Length);
        File.WriteAllText(store.ConfigurationFilePath, "broken current");
        File.WriteAllText(backup1, "broken recent backup");

        var recovered = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.RecoveredFromBackup2, recovered.Status);
        Assert.AreEqual(states[1].PinnedApplications[^1].Id, recovered.State.PinnedApplications[^1].Id);
        Assert.IsTrue(File.Exists(backup2));
    }

    [TestMethod]
    public void InterruptedTemporaryWriteLeavesPrimaryUntouchedAndCleansItsOwnTemporaryFile()
    {
        var original = CreateState("Original", DockEdge.Bottom, DockAppearancePreference.FollowSystem);
        var store = new DockConfigurationFileStore(_testDirectory);
        Assert.IsTrue(store.Save(original).Succeeded);

        var interruptedStore = new DockConfigurationFileStore(
            _testDirectory, new InterruptedWriteFileOperations(new PhysicalDockConfigurationFileOperations()));
        var failedSave = interruptedStore.Save(CreateState("Replacement", DockEdge.Right, DockAppearancePreference.Dark));

        Assert.AreEqual(DockConfigurationStoreSaveStatus.IoFailure, failedSave.Status);
        Assert.AreEqual("WriteFailed", failedSave.DiagnosticCode);
        Assert.AreEqual(original.PinnedApplications[^1].Id, store.Load().State.PinnedApplications[^1].Id);
        Assert.AreEqual(1, Directory.GetFiles(_testDirectory, DockConfigurationFileStore.ConfigurationFileName).Length);
        Assert.AreEqual(0, Directory.GetFiles(_testDirectory, ".dock-config.*.tmp").Length);
    }

    [TestMethod]
    public void PermissionFailureIsReportedWithoutChangingSystemPermissions()
    {
        var deniedStore = new DockConfigurationFileStore(_testDirectory, new DeniedDirectoryFileOperations());
        var result = deniedStore.Save(CreateState("Editor", DockEdge.Bottom, DockAppearancePreference.FollowSystem));

        Assert.AreEqual(DockConfigurationStoreSaveStatus.AccessDenied, result.Status);
        Assert.AreEqual("AccessDenied", result.DiagnosticCode);
        Assert.AreEqual(0, Directory.GetFiles(_testDirectory).Length);
    }

    [TestMethod]
    public void ReadPermissionFailureIsReportedInsteadOfSilentlyReturningDefaults()
    {
        var store = new DockConfigurationFileStore(_testDirectory);
        Assert.IsTrue(store.Save(CreateState("Editor", DockEdge.Bottom, DockAppearancePreference.FollowSystem)).Succeeded);
        var deniedReadStore = new DockConfigurationFileStore(
            _testDirectory, new DeniedReadFileOperations(new PhysicalDockConfigurationFileOperations()));

        var result = deniedReadStore.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.AccessDenied, result.Status);
        Assert.AreEqual("AccessDenied", result.DiagnosticCode);
    }

    [TestMethod]
    public void UnrecoverableCorruptionReturnsDefaultsWithoutOverwritingInput()
    {
        var store = new DockConfigurationFileStore(_testDirectory);
        File.WriteAllText(store.ConfigurationFilePath, "not-json");

        var recovered = store.Load();

        Assert.AreEqual(DockConfigurationStoreLoadStatus.RecoveredInvalidData, recovered.Status);
        Assert.AreEqual(DockConfigurationLoadStatus.RecoveredInvalidData, recovered.CodecStatus);
        Assert.AreEqual(1, recovered.State.PinnedApplications.Count);
        Assert.AreEqual("not-json", File.ReadAllText(store.ConfigurationFilePath));
    }

    private static DockState CreateState(string label, DockEdge edge, DockAppearancePreference appearance)
    {
        var state = DockState.CreateDefault().AddPinnedApplication(new DockProjectItem(
            DockItemId.New(), DockProjectItemKind.Application, DockSection.PinnedApplications,
            label,
            DockTargetIdentity.Create(DockIdentityScheme.WindowsAppUserModelId, $"Contoso.{label.Replace(' ', '.')}",
                $"Contoso.{label.Replace(' ', '.')}.Canonical"))).State;
        state = state.SetEdge(edge).State;
        return state.SetAppearance(appearance).State;
    }

    private sealed class InterruptedWriteFileOperations(IDockConfigurationFileOperations inner)
        : IDockConfigurationFileOperations
    {
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public long GetLength(string path) => inner.GetLength(path);
        public Stream OpenRead(string path) => inner.OpenRead(path);
        public Stream CreateNewWriteThrough(string path) => new InterruptedWriteStream(inner.CreateNewWriteThrough(path));
        public void FlushToDisk(Stream stream) => inner.FlushToDisk(stream);
        public void Replace(string sourcePath, string destinationPath, string backupPath) => inner.Replace(sourcePath, destinationPath, backupPath);
        public void Move(string sourcePath, string destinationPath, bool overwrite) => inner.Move(sourcePath, destinationPath, overwrite);
        public void Delete(string path) => inner.Delete(path);
    }

    private sealed class DeniedDirectoryFileOperations : IDockConfigurationFileOperations
    {
        public void CreateDirectory(string path) => throw new UnauthorizedAccessException("Deterministic permission-denied simulation.");
        public bool FileExists(string path) => File.Exists(path);
        public long GetLength(string path) => new FileInfo(path).Length;
        public Stream OpenRead(string path) => File.OpenRead(path);
        public Stream CreateNewWriteThrough(string path) => throw new AssertFailedException("Write should not be reached.");
        public void FlushToDisk(Stream stream) => throw new AssertFailedException("Flush should not be reached.");
        public void Replace(string sourcePath, string destinationPath, string backupPath) => throw new AssertFailedException("Replace should not be reached.");
        public void Move(string sourcePath, string destinationPath, bool overwrite) => throw new AssertFailedException("Move should not be reached.");
        public void Delete(string path) => File.Delete(path);
    }

    private sealed class DeniedReadFileOperations(IDockConfigurationFileOperations inner)
        : IDockConfigurationFileOperations
    {
        public void CreateDirectory(string path) => inner.CreateDirectory(path);
        public bool FileExists(string path) => inner.FileExists(path);
        public long GetLength(string path) => inner.GetLength(path);
        public Stream OpenRead(string path) => throw new UnauthorizedAccessException("Deterministic permission-denied simulation.");
        public Stream CreateNewWriteThrough(string path) => inner.CreateNewWriteThrough(path);
        public void FlushToDisk(Stream stream) => inner.FlushToDisk(stream);
        public void Replace(string sourcePath, string destinationPath, string backupPath) => inner.Replace(sourcePath, destinationPath, backupPath);
        public void Move(string sourcePath, string destinationPath, bool overwrite) => inner.Move(sourcePath, destinationPath, overwrite);
        public void Delete(string path) => inner.Delete(path);
    }

    private sealed class InterruptedWriteStream(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;
        public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            var partialCount = Math.Max(1, count / 2);
            inner.Write(buffer, offset, partialCount);
            throw new IOException("Deterministic interrupted write.");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var partialCount = Math.Max(1, buffer.Length / 2);
            inner.Write(buffer[..partialCount]);
            throw new IOException("Deterministic interrupted write.");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
