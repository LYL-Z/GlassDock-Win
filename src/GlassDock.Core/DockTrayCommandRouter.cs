namespace GlassDock.Core;

public enum DockTrayCommand : uint
{
    Show = 1,
    Exit = 2
}

public static class DockTrayCommandRouter
{
    public static bool Dispatch(uint command, Action show, Action exit)
    {
        ArgumentNullException.ThrowIfNull(show);
        ArgumentNullException.ThrowIfNull(exit);

        switch ((DockTrayCommand)command)
        {
            case DockTrayCommand.Show:
                show();
                return true;
            case DockTrayCommand.Exit:
                exit();
                return true;
            default:
                return false;
        }
    }
}
