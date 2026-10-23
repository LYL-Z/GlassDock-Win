using System.IO;
using System.Windows;
using GlassDock.App;
using GlassDock.Persistence;
using GlassDock.Windows.Shell;

namespace GlassDock.Experimental.Wpf;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            var configurationDirectory = Path.Combine(repositoryRoot, ".local", "experimental-dock");
            var store = new DockConfigurationFileStore(configurationDirectory);
            var loaded = store.Load();
            var session = new DockApplicationSession(loaded.State, store, new WindowsShellProvider());
            var window = new DockWindow(session, loaded.DiagnosticCode ?? loaded.Status.ToString());
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"The experimental Dock could not initialize. {exception.GetType().Name}",
                "GlassDock experimental host",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run this experimental build from inside the D: project checkout.");
    }
}
