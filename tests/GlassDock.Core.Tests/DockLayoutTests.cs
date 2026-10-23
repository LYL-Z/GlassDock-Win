using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Core.Tests;

[TestClass]
public sealed class DockLayoutTests
{
    private static readonly PixelRect WorkArea = new(100, -50, 1000, 800);
    private static readonly PixelSize DockSize = new(580, 132);

    [TestMethod]
    [DataRow(DockEdge.Bottom, 310d, 606d)]
    [DataRow(DockEdge.Left, 112d, 284d)]
    [DataRow(DockEdge.Right, 1100d - 580d - 12d, 284d)]
    public void PlacePositionsDockOnRequestedEdge(DockEdge edge, double expectedX, double expectedY)
    {
        var result = DockLayout.Place(WorkArea, DockSize, edge);

        Assert.AreEqual(expectedX, result.X);
        Assert.AreEqual(expectedY, result.Y);
        Assert.AreEqual(DockSize.Width, result.Width);
        Assert.AreEqual(DockSize.Height, result.Height);
    }

    [TestMethod]
    public void PlaceCentersDockOnBottomEdge()
    {
        var result = DockLayout.Place(WorkArea, DockSize, DockEdge.Bottom);

        Assert.AreEqual(WorkArea.X + (WorkArea.Width - DockSize.Width) / 2, result.X);
        Assert.AreEqual(WorkArea.Bottom - DockSize.Height - 12, result.Y);
    }

    [TestMethod]
    public void PlaceRejectsInvalidWorkArea()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockLayout.Place(new PixelRect(0, 0, 0, 800), DockSize, DockEdge.Bottom));
    }

    [TestMethod]
    public void PlaceRejectsInvalidDockSize()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockLayout.Place(WorkArea, new PixelSize(580, -1), DockEdge.Bottom));
    }

    [TestMethod]
    public void PlaceRejectsNegativeInset()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockLayout.Place(WorkArea, DockSize, DockEdge.Bottom, -1));
    }

    [TestMethod]
    public void PlaceRejectsUnknownEdge()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockLayout.Place(WorkArea, DockSize, (DockEdge)99));
    }
}

[TestClass]
public sealed class DemoDockItemsTests
{
    [TestMethod]
    [DataRow(8)]
    [DataRow(24)]
    public void CreateReturnsRequestedUniquePlaceholderItems(int count)
    {
        var items = DemoDockItems.Create(count);

        Assert.AreEqual(count, items.Count);
        Assert.AreEqual(count, items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.IsTrue(items.All(item => !string.IsNullOrWhiteSpace(item.Label)));
        Assert.IsTrue(items.All(item => !string.IsNullOrWhiteSpace(item.Glyph)));
        Assert.IsTrue(items.All(item => System.Text.RegularExpressions.Regex.IsMatch(item.AccentHex, "^#[0-9A-Fa-f]{6}$")));
    }

    [TestMethod]
    public void CreateRejectsCountsOutsideM0Samples()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DemoDockItems.Create(12));
    }
}

[TestClass]
public sealed class DockTrayCommandRouterTests
{
    [TestMethod]
    public void ShowCommandInvokesShowOnly()
    {
        var showCount = 0;
        var exitCount = 0;

        var handled = DockTrayCommandRouter.Dispatch((uint)DockTrayCommand.Show,
            () => showCount++, () => exitCount++);

        Assert.IsTrue(handled);
        Assert.AreEqual(1, showCount);
        Assert.AreEqual(0, exitCount);
    }

    [TestMethod]
    public void ExitCommandInvokesExitOnly()
    {
        var showCount = 0;
        var exitCount = 0;

        var handled = DockTrayCommandRouter.Dispatch((uint)DockTrayCommand.Exit,
            () => showCount++, () => exitCount++);

        Assert.IsTrue(handled);
        Assert.AreEqual(0, showCount);
        Assert.AreEqual(1, exitCount);
    }

    [TestMethod]
    public void UnknownCommandInvokesNeitherAction()
    {
        var invocationCount = 0;

        var handled = DockTrayCommandRouter.Dispatch(99,
            () => invocationCount++, () => invocationCount++);

        Assert.IsFalse(handled);
        Assert.AreEqual(0, invocationCount);
    }
}

[TestClass]
public sealed class DockPerformanceRecorderTests
{
    [TestMethod]
    public void RecordsPhaseSeparatedRenderIntervalsAndAnimationDuration()
    {
        var recorder = new DockPerformanceRecorder(timestampFrequency: 1000);
        recorder.RecordFrameworkRenderCallback(0);
        recorder.RecordFrameworkRenderCallback(10);
        var animation = recorder.BeginAnimation(12);
        recorder.RecordFrameworkRenderCallback(20);
        recorder.RecordFrameworkRenderCallback(36);
        Assert.IsTrue(recorder.EndAnimation(animation, 42, completed: true));
        recorder.RecordFrameworkRenderCallback(50);

        var snapshot = recorder.CreateSnapshot();

        // Intervals that cross an idle/animation boundary are omitted instead of mislabeled.
        Assert.AreEqual(1, snapshot.IdleFrameworkRenderIntervals.SampleCount);
        Assert.AreEqual(1, snapshot.AnimationFrameworkRenderIntervals.SampleCount);
        Assert.AreEqual(10d, snapshot.IdleFrameworkRenderIntervals.P50Milliseconds);
        Assert.AreEqual(16d, snapshot.AnimationFrameworkRenderIntervals.P50Milliseconds);
        Assert.AreEqual(16d, snapshot.AnimationFrameworkRenderIntervals.P95Milliseconds);
        Assert.AreEqual(30d, snapshot.CompletedAnimationDurations.P50Milliseconds);
        Assert.AreEqual(1, snapshot.CompletedAnimationCount);
        Assert.AreEqual(0, snapshot.OpenAnimationCount);
    }

    [TestMethod]
    public void UsesNearestRankPercentilesAndKeepsRawSamples()
    {
        var recorder = new DockPerformanceRecorder(timestampFrequency: 1000);
        recorder.RecordLogicalUpdate(0);
        recorder.RecordLogicalUpdate(10);
        recorder.RecordLogicalUpdate(30);
        recorder.RecordLogicalUpdate(60);
        recorder.RecordLogicalUpdate(100);

        var snapshot = recorder.CreateSnapshot();

        Assert.AreEqual(4, snapshot.LogicalUpdateIntervals.SampleCount);
        Assert.AreEqual(20d, snapshot.LogicalUpdateIntervals.P50Milliseconds);
        Assert.AreEqual(40d, snapshot.LogicalUpdateIntervals.P95Milliseconds);
        Assert.AreEqual(40d, snapshot.LogicalUpdateIntervals.P99Milliseconds);
        CollectionAssert.AreEqual(new[] { 10d, 20d, 30d, 40d },
            snapshot.LogicalUpdateIntervalSamplesMilliseconds.ToArray());
    }

    [TestMethod]
    public void ReportsEmptyDistributionsWithoutInventingZeroMeasurements()
    {
        var snapshot = new DockPerformanceRecorder(timestampFrequency: 1000).CreateSnapshot();

        Assert.AreEqual(0, snapshot.IdleFrameworkRenderIntervals.SampleCount);
        Assert.IsNull(snapshot.IdleFrameworkRenderIntervals.P50Milliseconds);
        Assert.IsNull(snapshot.IdleFrameworkRenderIntervals.P95Milliseconds);
        Assert.IsNull(snapshot.IdleFrameworkRenderIntervals.P99Milliseconds);
    }

    [TestMethod]
    public void RecordsInterruptedAndUnknownAnimationStatesSeparately()
    {
        var recorder = new DockPerformanceRecorder(timestampFrequency: 1000);
        var animation = recorder.BeginAnimation(100);

        Assert.IsFalse(recorder.EndAnimation(animation + 1, 110, completed: false));
        Assert.IsTrue(recorder.EndAnimation(animation, 115, completed: false));

        var snapshot = recorder.CreateSnapshot();

        Assert.AreEqual(1, snapshot.CancelledAnimationCount);
        Assert.AreEqual(15d, snapshot.CancelledAnimationDurations.P50Milliseconds);
        Assert.AreEqual(0, snapshot.OpenAnimationCount);
    }
}
