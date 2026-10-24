using GlassDock.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GlassDock.Core.Tests;

[TestClass]
public sealed class DockVisualGeometryTests
{
    [TestMethod]
    [DataRow(44d)]
    [DataRow(48d)]
    [DataRow(58d)]
    [DataRow(72d)]
    public void HorizontalDockKeepsEveryIconInsideItsTileAndRoundedBackplate(double iconSize)
    {
        var layout = DockVisualGeometry.Measure(DockEdge.Bottom, 4, 2, 3, iconSize, 1800);

        Assert.IsFalse(layout.IsVertical);
        Assert.AreEqual(9, layout.Items.Count);
        Assert.AreEqual(2, layout.Separators.Count);
        Assert.IsFalse(layout.IsScrollable);
        foreach (var item in layout.Items)
        {
            Assert.IsTrue(Contains(item.TileBounds, item.IconBounds), $"Icon escaped its tile at {iconSize} DIP.");
            Assert.IsTrue(Contains(layout.BackplateBounds, item.IconBounds), $"Icon escaped the backplate at {iconSize} DIP.");
            Assert.AreEqual(iconSize, item.IconBounds.Width);
            Assert.AreEqual(iconSize, item.IconBounds.Height);
        }
    }

    [TestMethod]
    public void BackplateWidthTracksVisibleItemCountAndSemanticSections()
    {
        var oneSection = DockVisualGeometry.Measure(DockEdge.Bottom, 2, 0, 0, 48, 1800);
        var threeSections = DockVisualGeometry.Measure(DockEdge.Bottom, 2, 1, 2, 48, 1800);

        Assert.AreEqual(1, oneSection.VisibleSectionCount);
        Assert.AreEqual(0, oneSection.Separators.Count);
        Assert.AreEqual(3, threeSections.VisibleSectionCount);
        Assert.AreEqual(2, threeSections.Separators.Count);
        Assert.IsTrue(threeSections.BackplateBounds.Width > oneSection.BackplateBounds.Width);
        CollectionAssert.AreEqual(
            new[] { DockSection.PinnedApplications, DockSection.PinnedApplications,
                DockSection.RecentApplications, DockSection.FilesAndFolders, DockSection.FilesAndFolders },
            threeSections.Items.Select(item => item.Section).ToArray());
    }

    [TestMethod]
    [DataRow(DockEdge.Bottom)]
    [DataRow(DockEdge.Left)]
    [DataRow(DockEdge.Right)]
    public void ConstrainedAxisScrollsWithoutClippingIconCrossSection(DockEdge edge)
    {
        var layout = DockVisualGeometry.Measure(edge, 24, 8, 12, 58, 520);

        Assert.IsTrue(layout.IsScrollable);
        Assert.IsTrue(layout.WindowWidthDip <= 520);
        Assert.IsTrue(layout.WindowHeightDip <= 520);
        foreach (var item in layout.Items)
        {
            if (layout.IsVertical)
            {
                Assert.IsTrue(item.IconBounds.X >= layout.BackplateBounds.X - 0.001);
                Assert.IsTrue(item.IconBounds.Right <= layout.BackplateBounds.Right + 0.001);
            }
            else
            {
                Assert.IsTrue(item.IconBounds.Y >= layout.BackplateBounds.Y - 0.001);
                Assert.IsTrue(item.IconBounds.Bottom <= layout.BackplateBounds.Bottom + 0.001);
            }
            Assert.AreEqual(58, layout.IsVertical ? item.IconBounds.Width : item.IconBounds.Height);
        }
    }

    [TestMethod]
    public void BottomDockUsesHorizontalGeometryAndSideDocksUseVerticalGeometry()
    {
        var bottom = DockVisualGeometry.Measure(DockEdge.Bottom, 2, 1, 2, 48, 1600);
        var left = DockVisualGeometry.Measure(DockEdge.Left, 2, 1, 2, 48, 1000);
        var right = DockVisualGeometry.Measure(DockEdge.Right, 2, 1, 2, 48, 1000);

        Assert.IsFalse(bottom.IsVertical);
        Assert.IsTrue(left.IsVertical);
        Assert.IsTrue(right.IsVertical);
        Assert.AreEqual(left.WindowWidthDip, right.WindowWidthDip);
        Assert.AreEqual(left.WindowHeightDip, right.WindowHeightDip);
        Assert.AreEqual(2, left.Separators.Count);
    }

    [TestMethod]
    public void EmptyDockReceivesAUsableSmallBackplateWithoutInventingItems()
    {
        var layout = DockVisualGeometry.Measure(DockEdge.Bottom, 0, 0, 0, 48, 1200);

        Assert.AreEqual(0, layout.Items.Count);
        Assert.AreEqual(0, layout.Separators.Count);
        Assert.AreEqual(0, layout.VisibleSectionCount);
        Assert.IsTrue(layout.BackplateBounds.Width >= DockVisualGeometry.MinimumBackplateLengthDip);
    }

    [TestMethod]
    public void MeasureRejectsUnsupportedIconSizeAndInvalidAvailableArea()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockVisualGeometry.Measure(DockEdge.Bottom, 1, 0, 0, 0, 1200));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
            DockVisualGeometry.Measure(DockEdge.Bottom, 1, 0, 0, 48, 80));
    }

    private static bool Contains(PixelRect outer, PixelRect inner) =>
        inner.X >= outer.X - 0.001 && inner.Y >= outer.Y - 0.001
        && inner.Right <= outer.Right + 0.001 && inner.Bottom <= outer.Bottom + 0.001;
}
