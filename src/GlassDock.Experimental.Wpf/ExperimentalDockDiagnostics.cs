using System.IO;
using System.Text;
using System.Text.Json;
using System.Security.Cryptography;
using GlassDock.Core;

namespace GlassDock.Experimental.Wpf;

/// <summary>Optional, local-only diagnostics for an explicitly bounded preview run.</summary>
public sealed class ExperimentalDockDiagnostics
{
    private readonly object _sync = new();
    private readonly string _path;

    public ExperimentalDockDiagnostics(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        _path = Path.GetFullPath(path);
        if (!_path.StartsWith("D:\\", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Preview diagnostics must be stored on D:.", nameof(path));
    }

    public void Record(string eventName, TimeSpan elapsed, object? detail = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventName);
        try
        {
            var directory = Path.GetDirectoryName(_path);
            if (directory is null)
                return;
            Directory.CreateDirectory(directory);
            var entry = JsonSerializer.Serialize(new
            {
                TimestampUtc = DateTimeOffset.UtcNow,
                Event = eventName,
                ElapsedMilliseconds = Math.Round(elapsed.TotalMilliseconds, 3),
                Detail = detail
            });
            lock (_sync)
                File.AppendAllText(_path, entry + Environment.NewLine, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // A failed diagnostics write must not prevent the experimental preview from opening or closing.
        }
    }

    public static string GetPinnedOrderSha256(DockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var canonicalSequence = string.Join("\n", state.PinnedApplications.Select(item =>
            $"{item.Id.Value:D}\0{(int)item.TargetIdentity.Scheme}\0{item.TargetIdentity.Value}\0{item.TargetIdentity.ComparisonKey}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalSequence)));
    }
}
