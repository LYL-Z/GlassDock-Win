using GlassDock.Core;
using GlassDock.Windows.Shell;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Windows.Tests;

[TestClass]
public sealed class WindowsShellCommandExecutorTests
{
    [TestMethod]
    public async Task LaunchUsesAppActivationAndDoesNotRouteThroughWindowActivation()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var item = MakeApplication(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor", "Contoso.Editor");

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Launch, item));

        Assert.AreEqual(DockCommandOutcome.Completed, result.Outcome);
        Assert.AreEqual("aumid:Contoso.Editor", backend.LastOperation);
        Assert.AreEqual(0, backend.FindWindowCalls);
        Assert.AreEqual(0, backend.SetForegroundCalls);
    }

    [TestMethod]
    public async Task AumidShortcutLaunchUsesTheOriginalShellLinkRatherThanAssumingPackagedActivation()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var descriptor = WindowsShellApplicationDescriptor.FromAppUserModelIdShortcut(
            "Editor", "Contoso.Editor", @"D:\Menu\Editor.lnk");
        var item = descriptor.CreatePinnedItem(DockItemId.New());

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Launch, item));

        Assert.AreEqual(DockCommandOutcome.Completed, result.Outcome);
        Assert.AreEqual("link:D:\\Menu\\Editor.lnk", backend.LastOperation);
    }

    [TestMethod]
    public async Task AumidEndingInLnkIsNotMistakenForAShellLinkPath()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var item = MakeApplication(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor.lnk", "Contoso.Editor.lnk");

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Launch, item));

        Assert.AreEqual(DockCommandOutcome.Completed, result.Outcome);
        Assert.AreEqual("aumid:Contoso.Editor.lnk", backend.LastOperation);
    }

    [TestMethod]
    public async Task ActivateReturnsForegroundDenialWithoutRetryingOrForcingFocus()
    {
        var backend = new RecordingBackend
        {
            WindowToReturn = new IntPtr(1234),
            ForegroundAllowed = false
        };
        var executor = new WindowsShellCommandExecutor(backend);
        var item = MakeApplication(DockIdentityScheme.WindowsAppUserModelId, "Contoso.Editor", "Contoso.Editor");

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Activate, item));

        Assert.AreEqual(DockCommandOutcome.ForegroundActivationDenied, result.Outcome);
        Assert.AreEqual("windows-foreground-policy-denied", result.DiagnosticCode);
        Assert.AreEqual(1, backend.FindWindowCalls);
        Assert.AreEqual(1, backend.SetForegroundCalls);
        Assert.AreEqual(new IntPtr(1234), backend.LastWindow);
    }

    [TestMethod]
    public async Task ActivateDoesNotGuessWhichWindowBelongsToAnArgumentSpecificShortcut()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var identity = DockTargetIdentity.Create(
            DockIdentityScheme.WindowsShellLinkPath,
            @"D:\Menu\Editor.lnk",
            "fileid:00000001:0000000000000002|args64:" + Convert.ToBase64String("--profile work"u8.ToArray()));
        var item = new DockProjectItem(
            DockItemId.New(),
            DockProjectItemKind.Application,
            DockSection.PinnedApplications,
            "Editor",
            identity);

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Activate, item));

        Assert.AreEqual(DockCommandOutcome.Unsupported, result.Outcome);
        Assert.AreEqual(0, backend.FindWindowCalls);
        Assert.AreEqual(0, backend.SetForegroundCalls);
    }

    [TestMethod]
    public async Task OpenRoutesCanonicalPathSeparatelyFromApplicationLaunch()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var identity = DockTargetIdentity.Create(DockIdentityScheme.WindowsCanonicalPath, @"D:\Docs\file.txt");
        var item = new DockProjectItem(
            DockItemId.New(),
            DockProjectItemKind.File,
            DockSection.FilesAndFolders,
            "file.txt",
            identity);

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Open, item));

        Assert.AreEqual(DockCommandOutcome.Completed, result.Outcome);
        Assert.AreEqual("open:D:\\Docs\\file.txt", backend.LastOperation);
    }

    [TestMethod]
    public async Task OpenDoesNotMistakeKnownFolderIdentityForAFileSystemPath()
    {
        var backend = new RecordingBackend();
        var executor = new WindowsShellCommandExecutor(backend);
        var identity = DockTargetIdentity.Create(DockIdentityScheme.WindowsKnownFolderId, "Downloads", "FOLDERID_Downloads");
        var item = new DockProjectItem(
            DockItemId.New(),
            DockProjectItemKind.Folder,
            DockSection.FilesAndFolders,
            "Downloads",
            identity);

        var result = await executor.ExecuteAsync(DockCommand.For(DockCommandKind.Open, item));

        Assert.AreEqual(DockCommandOutcome.Unsupported, result.Outcome);
        Assert.AreEqual("known-folder-open-not-yet-implemented", result.DiagnosticCode);
        Assert.IsNull(backend.LastOperation);
    }

    private static DockProjectItem MakeApplication(DockIdentityScheme scheme, string value, string comparisonKey)
    {
        var identity = DockTargetIdentity.Create(scheme, value, comparisonKey);
        return new DockProjectItem(
            DockItemId.New(),
            DockProjectItemKind.Application,
            DockSection.PinnedApplications,
            "Editor",
            identity);
    }

    private sealed class RecordingBackend : IWindowsShellCommandBackend
    {
        public string? LastOperation { get; private set; }
        public int FindWindowCalls { get; private set; }
        public int SetForegroundCalls { get; private set; }
        public IntPtr WindowToReturn { get; init; }
        public IntPtr LastWindow { get; private set; }
        public bool ForegroundAllowed { get; init; } = true;

        public DockCommandResult LaunchAppUserModelId(string appUserModelId)
        {
            LastOperation = $"aumid:{appUserModelId}";
            return new DockCommandResult(DockCommandOutcome.Completed);
        }

        public DockCommandResult LaunchShellLink(string shellLinkPath)
        {
            LastOperation = $"link:{shellLinkPath}";
            return new DockCommandResult(DockCommandOutcome.Completed);
        }

        public DockCommandResult OpenPath(string path)
        {
            LastOperation = $"open:{path}";
            return new DockCommandResult(DockCommandOutcome.Completed);
        }

        public bool TryFindApplicationWindow(DockTargetIdentity identity, out IntPtr windowHandle)
        {
            FindWindowCalls++;
            windowHandle = WindowToReturn;
            return windowHandle != IntPtr.Zero;
        }

        public bool TrySetForegroundWindow(IntPtr windowHandle)
        {
            SetForegroundCalls++;
            LastWindow = windowHandle;
            return ForegroundAllowed;
        }
    }
}
