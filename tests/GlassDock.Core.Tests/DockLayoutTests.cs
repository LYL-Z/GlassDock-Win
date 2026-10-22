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
