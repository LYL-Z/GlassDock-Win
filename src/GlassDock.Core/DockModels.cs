namespace GlassDock.Core;

public enum DockEdge
{
    Bottom,
    Left,
    Right
}

public sealed record DockItem(string Id, string Label, string Glyph, string AccentHex);

public readonly record struct PixelSize(double Width, double Height);

public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;
}

public static class DemoDockItems
{
    private static readonly DockItem[] Placeholders =
    [
        new("grid", "Grid", "▦", "#6C7CF0"),
        new("web", "Web", "◉", "#39A9DB"),
        new("inbox", "Inbox", "✉", "#E7816F"),
        new("planner", "Planner", "▣", "#E4AA45"),
        new("audio", "Audio", "♫", "#8A70D6"),
        new("console", "Console", "›_", "#55AA88"),
        new("tools", "Tools", "⚙", "#708396"),
        new("gallery", "Gallery", "▧", "#D276A5")
    ];

    public static IReadOnlyList<DockItem> Create(int count = 8)
    {
        if (count is not (8 or 24))
            throw new ArgumentOutOfRangeException(nameof(count), "The M0 sample supports 8 or 24 placeholders.");

        return Enumerable.Range(0, count)
            .Select(index =>
            {
                var item = Placeholders[index % Placeholders.Length];
                return new DockItem(
                    $"{item.Id}-{index + 1}",
                    count == 8 ? item.Label : $"{item.Label} {index + 1}",
                    item.Glyph,
                    item.AccentHex);
            })
            .ToArray();
    }
}
