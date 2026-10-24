namespace GlassDock.Core;

/// <summary>
/// Framework-neutral measurements for the replaceable Dock visual host.
/// All values are device-independent pixels (DIP); the platform host converts
/// them to physical pixels when Windows lays out the window.
/// </summary>
public static class DockVisualGeometry
{
    public const double WindowShadowGutterDip = 22;
    public const double MinimumBackplateLengthDip = 56;

    private const double BorderDip = 1;
    private const double HorizontalPaddingDip = 10;
    private const double VerticalPaddingDip = 8;
    private const double TileChromeDip = 12;
    private const double TileMarginDip = 2;
    private const double SectionMarginDip = 3;
    private const double SeparatorSlotDip = 9;

    public static DockVisualLayout Measure(
        DockEdge edge,
        int pinnedCount,
        int recentCount,
        int fileCount,
        double iconSizeDip,
        double availableAxisLengthDip)
    {
        ValidateCount(pinnedCount, nameof(pinnedCount));
        ValidateCount(recentCount, nameof(recentCount));
        ValidateCount(fileCount, nameof(fileCount));
        if (!double.IsFinite(iconSizeDip) || iconSizeDip is < 16 or > 256)
            throw new ArgumentOutOfRangeException(nameof(iconSizeDip), "Icon size must be between 16 and 256 DIP.");
        if (!double.IsFinite(availableAxisLengthDip) || availableAxisLengthDip < 100)
            throw new ArgumentOutOfRangeException(nameof(availableAxisLengthDip), "Available axis length must be at least 100 DIP.");
        if (edge is not (DockEdge.Bottom or DockEdge.Left or DockEdge.Right))
            throw new ArgumentOutOfRangeException(nameof(edge));

        var sections = new List<(DockSection Section, int Count)>(3);
        if (pinnedCount > 0) sections.Add((DockSection.PinnedApplications, pinnedCount));
        if (recentCount > 0) sections.Add((DockSection.RecentApplications, recentCount));
        if (fileCount > 0) sections.Add((DockSection.FilesAndFolders, fileCount));

        var vertical = edge is DockEdge.Left or DockEdge.Right;
        var tileSize = iconSizeDip + TileChromeDip;
        var tileAdvance = tileSize + (TileMarginDip * 2);
        var sectionCross = SectionMarginDip * 2;
        var separatorCount = Math.Max(0, sections.Count - 1);
        var contentAxis = sections.Sum(section => sectionCross + (section.Count * tileAdvance))
            + (separatorCount * SeparatorSlotDip);
        var maxBackplateAxis = Math.Max(MinimumBackplateLengthDip,
            availableAxisLengthDip - (WindowShadowGutterDip * 2));
        var naturalBackplateAxis = Math.Max(MinimumBackplateLengthDip,
            contentAxis + (vertical ? (VerticalPaddingDip + BorderDip) * 2 : (HorizontalPaddingDip + BorderDip) * 2));
        var backplateAxis = Math.Min(naturalBackplateAxis, maxBackplateAxis);
        var viewportAxis = Math.Max(1,
            backplateAxis - (vertical ? (VerticalPaddingDip + BorderDip) * 2 : (HorizontalPaddingDip + BorderDip) * 2));

        var backplateCross = sections.Count == 0
            ? MinimumBackplateLengthDip
            : iconSizeDip + (vertical
                ? ((HorizontalPaddingDip + BorderDip) * 2) + sectionCross + (TileMarginDip * 2) + TileChromeDip
                : TileChromeDip + (TileMarginDip * 2) + ((VerticalPaddingDip + BorderDip) * 2));
        var windowAxis = backplateAxis + (WindowShadowGutterDip * 2);
        var windowCross = backplateCross + (WindowShadowGutterDip * 2);
        var backplate = vertical
            ? new PixelRect(WindowShadowGutterDip, WindowShadowGutterDip, backplateCross, backplateAxis)
            : new PixelRect(WindowShadowGutterDip, WindowShadowGutterDip, backplateAxis, backplateCross);

        var viewport = vertical
            ? new PixelSize(backplateCross - ((HorizontalPaddingDip + BorderDip) * 2), viewportAxis)
            : new PixelSize(viewportAxis, backplateCross - ((VerticalPaddingDip + BorderDip) * 2));
        var extent = vertical
            ? new PixelSize(backplateCross - ((HorizontalPaddingDip + BorderDip) * 2), contentAxis)
            : new PixelSize(contentAxis, backplateCross - ((VerticalPaddingDip + BorderDip) * 2));

        var items = new List<DockVisualItemBounds>(pinnedCount + recentCount + fileCount);
        var separators = new List<PixelRect>(separatorCount);
        var cursor = vertical
            ? backplate.Y + BorderDip + VerticalPaddingDip
            : backplate.X + BorderDip + HorizontalPaddingDip;
        var contentCrossStart = vertical
            ? backplate.X + BorderDip + HorizontalPaddingDip
            : backplate.Y + BorderDip + VerticalPaddingDip;
        var contentCrossSize = vertical ? viewport.Width : viewport.Height;

        for (var sectionIndex = 0; sectionIndex < sections.Count; sectionIndex++)
        {
            var section = sections[sectionIndex];
            cursor += SectionMarginDip;
            for (var itemIndex = 0; itemIndex < section.Count; itemIndex++)
            {
                var tileStart = cursor + TileMarginDip;
                var tile = vertical
                    ? new PixelRect(contentCrossStart + (contentCrossSize - tileSize) / 2, tileStart, tileSize, tileSize)
                    : new PixelRect(tileStart, contentCrossStart + (contentCrossSize - tileSize) / 2, tileSize, tileSize);
                var icon = new PixelRect(
                    tile.X + (tile.Width - iconSizeDip) / 2,
                    tile.Y + (tile.Height - iconSizeDip) / 2,
                    iconSizeDip,
                    iconSizeDip);
                items.Add(new DockVisualItemBounds(section.Section, itemIndex, tile, icon));
                cursor += tileAdvance;
            }
            cursor += SectionMarginDip;

            if (sectionIndex < sections.Count - 1)
            {
                if (vertical)
                {
                    var lineWidth = iconSizeDip * 0.72;
                    separators.Add(new PixelRect(
                        backplate.X + (backplate.Width - lineWidth) / 2,
                        cursor + (SeparatorSlotDip - 1) / 2,
                        lineWidth,
                        1));
                }
                else
                {
                    var lineHeight = iconSizeDip * 0.72;
                    separators.Add(new PixelRect(
                        cursor + (SeparatorSlotDip - 1) / 2,
                        backplate.Y + (backplate.Height - lineHeight) / 2,
                        1,
                        lineHeight));
                }
                cursor += SeparatorSlotDip;
            }
        }

        return new DockVisualLayout(
            vertical ? windowCross : windowAxis,
            vertical ? windowAxis : windowCross,
            backplate,
            viewport,
            extent,
            Array.AsReadOnly(items.ToArray()),
            Array.AsReadOnly(separators.ToArray()),
            sections.Count,
            vertical,
            contentAxis > viewportAxis + 0.01);
    }

    private static void ValidateCount(int count, string parameterName)
    {
        if (count is < 0 or > DockConfigurationJson.MaximumItemsPerSection)
            throw new ArgumentOutOfRangeException(parameterName,
                $"Section item count must be between 0 and {DockConfigurationJson.MaximumItemsPerSection}.");
    }
}

public sealed record DockVisualLayout(
    double WindowWidthDip,
    double WindowHeightDip,
    PixelRect BackplateBounds,
    PixelSize ViewportSize,
    PixelSize ContentExtent,
    IReadOnlyList<DockVisualItemBounds> Items,
    IReadOnlyList<PixelRect> Separators,
    int VisibleSectionCount,
    bool IsVertical,
    bool IsScrollable);

public sealed record DockVisualItemBounds(
    DockSection Section,
    int Index,
    PixelRect TileBounds,
    PixelRect IconBounds);
