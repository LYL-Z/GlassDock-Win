using GlassDock.Windows.Windowing;

namespace GlassDock.Windows.Tests;

[TestClass]
public sealed class DockWindowReadinessPolicyTests
{
    private const int DockProcessId = 4200;
    private static readonly nint DockHandle = new(0x1234);
    private static readonly DockWindowBounds ValidBounds = new(10, 20, 1010, 220);

    [TestMethod]
    public void ZeroHwndIsRejectedEvenWhenLayoutMilestonesWereReported()
    {
        var result = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow()], DockProcessId, nint.Zero, layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.ZeroHandle, result.Status);
        Assert.IsFalse(result.IsReady);
    }

    [TestMethod]
    public void ReportedHwndOwnedByAnotherProcessIsRejected()
    {
        var result = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow(processId: DockProcessId + 1)], DockProcessId, DockHandle,
            layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.WrongProcess, result.Status);
    }

    [TestMethod]
    public void InvisibleReportedWindowIsRejected()
    {
        var result = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow(isVisible: false)], DockProcessId, DockHandle,
            layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.NotVisible, result.Status);
    }

    [TestMethod]
    public void InvalidWindowBoundsAreRejected()
    {
        var invalidBounds = new[]
        {
            new DockWindowBounds(10, 20, 10, 220),
            new DockWindowBounds(10, 20, 1010, 20),
            new DockWindowBounds(10, 20, 9, 219)
        };

        foreach (var bounds in invalidBounds)
        {
            var observation = VisibleDockWindow(bounds: bounds);
            var result = DockWindowReadinessPolicy.Evaluate(
                [observation], DockProcessId, DockHandle, layoutPrepared: true, contentRendered: true);
            Assert.AreEqual(DockWindowReadinessStatus.InvalidBounds, result.Status);
        }
    }

    [TestMethod]
    public void ValidEnumeratedHwndIsAcceptedWhenProcessMainWindowHandleWouldBeZero()
    {
        // The observation models WindowInteropHelper.Handle + EnumWindows. Process.MainWindowHandle
        // is deliberately absent from the policy and cannot veto this valid WPF tool window.
        var result = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow()], DockProcessId, DockHandle, layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.Ready, result.Status);
        Assert.AreEqual(DockHandle, result.Handle);
        Assert.AreEqual(1, result.VisibleCandidateCount);
    }

    [TestMethod]
    public void MultipleVisibleWindowsFromDockProcessAreRejectedAsAmbiguous()
    {
        var second = VisibleDockWindow(handle: new nint(0x5678));
        var result = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow(), second], DockProcessId, DockHandle,
            layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.MultipleVisibleCandidates, result.Status);
        Assert.AreEqual(2, result.VisibleCandidateCount);
    }

    [TestMethod]
    public void UnreportedVisibleWindowDoesNotReplaceTheSourceInitializedHwnd()
    {
        var other = VisibleDockWindow(handle: new nint(0x5678));
        var result = DockWindowReadinessPolicy.Evaluate(
            [other], DockProcessId, DockHandle, layoutPrepared: true, contentRendered: true);

        Assert.AreEqual(DockWindowReadinessStatus.HandleNotEnumerated, result.Status);
    }

    [TestMethod]
    public void WgcReadinessRequiresLayoutAndFirstContentRenderMilestones()
    {
        var beforeLayout = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow()], DockProcessId, DockHandle, layoutPrepared: false, contentRendered: true);
        var beforeRender = DockWindowReadinessPolicy.Evaluate(
            [VisibleDockWindow()], DockProcessId, DockHandle, layoutPrepared: true, contentRendered: false);

        Assert.AreEqual(DockWindowReadinessStatus.LayoutNotPrepared, beforeLayout.Status);
        Assert.AreEqual(DockWindowReadinessStatus.ContentNotRendered, beforeRender.Status);
    }

    private static DockWindowObservation VisibleDockWindow(
        nint? handle = null,
        int processId = DockProcessId,
        bool isWindow = true,
        bool isVisible = true,
        DockWindowBounds? bounds = null) =>
        new(handle ?? DockHandle, (uint)processId, isWindow, isVisible, bounds ?? ValidBounds);
}
