namespace GlassDock.App;

public static class DockLogger
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(
        GetStateRoot(), "logs", "dock.log");

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message, Exception exception) =>
        Write("ERROR", $"{message}{Environment.NewLine}{exception}");

    private static void Write(string level, string message)
    {
        lock (Gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            if (File.Exists(LogPath) && new FileInfo(LogPath).Length > 1_000_000)
                File.Delete(LogPath);
            File.AppendAllText(LogPath,
                $"{DateTimeOffset.UtcNow:O} [{level}] {message}{Environment.NewLine}");
        }
    }

    private static string GetStateRoot()
    {
        var configuredRoot = Environment.GetEnvironmentVariable("GLASSDOCK_POC_STATE_ROOT");
        return string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlassDock-Win-M0-PoC")
            : configuredRoot;
    }
}
