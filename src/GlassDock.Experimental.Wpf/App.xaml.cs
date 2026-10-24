using System.Diagnostics;
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
        var startupTimer = Stopwatch.StartNew();
        try
        {
            var repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
            var options = ParseOptions(e.Args, repositoryRoot);
            var diagnostics = options.DiagnosticsPath is null
                ? null
                : new ExperimentalDockDiagnostics(options.DiagnosticsPath);
            var configurationTimer = Stopwatch.StartNew();
            var store = new DockConfigurationFileStore(options.ProfileDirectory);
            var loaded = store.Load();
            diagnostics?.Record("configuration-loaded", configurationTimer.Elapsed, new
            {
                Status = loaded.Status.ToString(),
                DiagnosticCode = loaded.DiagnosticCode,
                PinnedCount = loaded.State.PinnedApplications.Count,
                RecentCount = loaded.State.RecentApplications.Count,
                FileCount = loaded.State.FilesAndFolders.Count,
                PinnedOrderSha256 = ExperimentalDockDiagnostics.GetPinnedOrderSha256(loaded.State)
            });

            var shellProvider = new WindowsShellProvider();
            var iconManifestTimer = Stopwatch.StartNew();
            var iconManifestStore = new DockIconManifestFileStore(options.ProfileDirectory);
            var iconManifest = iconManifestStore.Load();
            diagnostics?.Record("icon-manifest-loaded", iconManifestTimer.Elapsed, new
            {
                Status = iconManifest.Status.ToString(),
                iconManifest.DiagnosticCode,
                RoleAssignmentCount = iconManifest.Manifest.RoleAssignments.Count,
                MappingCount = iconManifest.Manifest.Mappings.Count
            });
            var session = new DockApplicationSession(
                loaded.State,
                store,
                shellProvider,
                options.ExecutionMode);
            var iconResolver = new DockIconResolver(
                iconManifest.Manifest,
                iconManifestStore,
                shellProvider,
                new WindowsDockIconVisualStateProvider());
            var window = new DockWindow(session, loaded.DiagnosticCode ?? loaded.Status.ToString(), diagnostics, startupTimer, iconResolver);
            MainWindow = window;
            diagnostics?.Record("window-show-called", startupTimer.Elapsed);
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

    private static StartupOptions ParseOptions(string[] args, string repositoryRoot)
    {
        var previewOnly = false;
        var allowShellCommands = false;
        string? profileDirectory = null;
        string? diagnosticsPath = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--preview-only":
                    previewOnly = true;
                    break;
                case "--enable-shell-commands":
                    allowShellCommands = true;
                    break;
                case "--profile-dir":
                    profileDirectory = ReadValue(args, ref index, "--profile-dir");
                    break;
                case "--diagnostics-path":
                    diagnosticsPath = ReadValue(args, ref index, "--diagnostics-path");
                    break;
                default:
                    throw new ArgumentException($"Unknown experimental host option: {args[index]}");
            }
        }

        if (previewOnly && allowShellCommands)
            throw new ArgumentException("Use either --preview-only or --enable-shell-commands, not both.");

        var localRoot = Path.Combine(repositoryRoot, ".local");
        var resolvedProfile = profileDirectory is null
            ? Path.Combine(localRoot, "experimental-dock")
            : ResolveLocalPath(profileDirectory, localRoot, "profile directory");
        var resolvedDiagnostics = diagnosticsPath is null
            ? null
            : ResolveLocalPath(diagnosticsPath, Path.Combine(localRoot, "evidence", "experimental-preview"), "diagnostics path");

        return new StartupOptions(
            allowShellCommands ? DockExecutionMode.AllowShellCommands : DockExecutionMode.PreviewOnly,
            resolvedProfile,
            resolvedDiagnostics);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || string.IsNullOrWhiteSpace(args[index + 1]) || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            throw new ArgumentException($"{option} requires a value.");
        return args[++index];
    }

    private static string ResolveLocalPath(string path, string allowedRoot, string description)
    {
        var fullPath = Path.GetFullPath(path);
        var fullRoot = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase)
            || !fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"The {description} must be under the approved D: .local directory.");
        return fullPath;
    }

    private static string FindRepositoryRoot(string startPath)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startPath));
        while (directory is not null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git"))
                || File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                if (!directory.FullName.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The experimental host only runs from the D: project checkout.");
                return directory.FullName;
            }
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Run this experimental build from inside the D: project checkout.");
    }

    private sealed record StartupOptions(
        DockExecutionMode ExecutionMode,
        string ProfileDirectory,
        string? DiagnosticsPath);
}
