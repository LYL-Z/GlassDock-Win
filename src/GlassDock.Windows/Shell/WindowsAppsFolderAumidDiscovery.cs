using System.Globalization;
using System.Runtime.InteropServices;

namespace GlassDock.Windows.Shell;

internal static class WindowsAppsFolderAumidDiscovery
{
    private const string AppsFolderParsingName = "shell:::{4234d49b-0245-4df3-b780-3893943456e1}";
    private const int MaximumEntries = 4096;

    public static IReadOnlyList<(string DisplayName, string AppUserModelId)> Read(
        List<WindowsShellDiscoveryIssue> issues,
        CancellationToken cancellationToken)
    {
        object? shell = null;
        object? folder = null;
        object? items = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application", throwOnError: true)!;
            shell = Activator.CreateInstance(shellType)!;
            dynamic shellDispatch = shell;
            folder = shellDispatch.NameSpace(AppsFolderParsingName);
            if (folder is null)
            {
                issues.Add(new WindowsShellDiscoveryIssue(
                    WindowsShellDiscoveryIssueKind.MetadataUnavailable,
                    "apps-folder-unavailable"));
                return [];
            }

            dynamic shellFolder = folder;
            items = shellFolder.Items();
            if (items is null)
            {
                issues.Add(new WindowsShellDiscoveryIssue(
                    WindowsShellDiscoveryIssueKind.MetadataUnavailable,
                    "apps-folder-items-unavailable"));
                return [];
            }

            dynamic shellItems = items;
            var count = Convert.ToInt32(shellItems.Count, CultureInfo.InvariantCulture);
            if (count < 0 || count > MaximumEntries)
            {
                issues.Add(new WindowsShellDiscoveryIssue(
                    WindowsShellDiscoveryIssueKind.EntryLimitReached,
                    "apps-folder-entry-limit-reached"));
                count = Math.Clamp(count, 0, MaximumEntries);
            }

            var applications = new List<(string DisplayName, string AppUserModelId)>();
            for (var index = 0; index < count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                object? item = null;
                try
                {
                    item = shellItems.Item(index);
                    if (item is null)
                        continue;

                    dynamic shellItem = item;
                    var name = Convert.ToString(shellItem.Name, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                    var appUserModelId = Convert.ToString(shellItem.Path, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(name) || name.Length > 512
                        || string.IsNullOrWhiteSpace(appUserModelId) || appUserModelId.Length > 512
                        || !appUserModelId.Contains('!')
                        || appUserModelId.Contains('\\')
                        || appUserModelId.Contains('/')
                        || appUserModelId.Contains('\0'))
                    {
                        continue;
                    }

                    applications.Add((name, appUserModelId));
                }
                catch (COMException exception)
                {
                    var error = exception.HResult & 0xFFFF;
                    issues.Add(new WindowsShellDiscoveryIssue(
                        error == 5 ? WindowsShellDiscoveryIssueKind.AccessDenied : WindowsShellDiscoveryIssueKind.MetadataUnavailable,
                        error == 5 ? "apps-folder-item-access-denied" : "apps-folder-item-unavailable"));
                }
                finally
                {
                    Release(item);
                }
            }

            return applications;
        }
        catch (UnauthorizedAccessException)
        {
            issues.Add(new WindowsShellDiscoveryIssue(
                WindowsShellDiscoveryIssueKind.AccessDenied,
                "apps-folder-access-denied"));
            return [];
        }
        catch (COMException exception)
        {
            var error = exception.HResult & 0xFFFF;
            issues.Add(new WindowsShellDiscoveryIssue(
                error == 5 ? WindowsShellDiscoveryIssueKind.AccessDenied : WindowsShellDiscoveryIssueKind.MetadataUnavailable,
                error == 5 ? "apps-folder-access-denied" : "apps-folder-enumeration-failed"));
            return [];
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            issues.Add(new WindowsShellDiscoveryIssue(
                WindowsShellDiscoveryIssueKind.MetadataUnavailable,
                "apps-folder-enumeration-failed"));
            return [];
        }
        finally
        {
            Release(items);
            Release(folder);
            Release(shell);
        }
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
            Marshal.ReleaseComObject(value);
    }
}
