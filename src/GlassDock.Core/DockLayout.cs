namespace GlassDock.Core;

public static class DockLayout
{
    public static PixelRect Place(PixelRect workArea, PixelSize dockSize, DockEdge edge, double inset = 12)
    {
        if (workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(workArea), "Work area must have positive dimensions.");
        if (dockSize.Width <= 0 || dockSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(dockSize), "Dock size must have positive dimensions.");
        if (inset < 0)
            throw new ArgumentOutOfRangeException(nameof(inset), "Inset cannot be negative.");

        var x = edge switch
        {
            DockEdge.Left => workArea.X + inset,
            DockEdge.Right => workArea.Right - dockSize.Width - inset,
            DockEdge.Bottom => workArea.X + (workArea.Width - dockSize.Width) / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };

        var y = edge switch
        {
            DockEdge.Bottom => workArea.Bottom - dockSize.Height - inset,
            DockEdge.Left or DockEdge.Right => workArea.Y + (workArea.Height - dockSize.Height) / 2,
            _ => throw new ArgumentOutOfRangeException(nameof(edge))
        };

        return new PixelRect(x, y, dockSize.Width, dockSize.Height);
    }
}
