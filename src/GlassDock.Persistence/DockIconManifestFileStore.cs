using System.Security;
using System.Text;
using GlassDock.App;

namespace GlassDock.Persistence;

public enum DockIconManifestLoadStatus
{
    Loaded = 1,
    DefaultedMissing = 2,
    RecoveredInvalid = 3,
    UnsupportedVersion = 4,
    AccessDenied = 5,
    IoFailure = 6
}

public sealed record DockIconManifestLoadResult(
    DockIconManifest Manifest,
    DockIconManifestLoadStatus Status,
    string? DiagnosticCode = null);

public enum DockIconManifestSaveStatus
{
    Saved = 1,
    AccessDenied = 2,
    IoFailure = 3
}

public sealed record DockIconManifestSaveResult(DockIconManifestSaveStatus Status, string? DiagnosticCode = null)
{
    public bool Succeeded => Status == DockIconManifestSaveStatus.Saved;
}

/// <summary>
/// Local file adapter for a user-maintained icon manifest and its assets. It never discovers,
/// downloads, embeds, or publishes image files.
/// </summary>
public sealed class DockIconManifestFileStore : IDockIconAssetReader
{
    public const string AssetDirectoryName = "icon-assets";
    public const string ManifestFileName = "manifest.json";
    private readonly string _assetRoot;
    private readonly string _manifestPath;

    public DockIconManifestFileStore(string profileDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileDirectory);
        _assetRoot = Path.GetFullPath(Path.Combine(profileDirectory, AssetDirectoryName));
        _manifestPath = Path.Combine(_assetRoot, ManifestFileName);
    }

    public DockIconManifestLoadResult Load()
    {
        try
        {
            if (ContainsReparsePoint(_manifestPath))
                return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.IoFailure, "IconManifestReparsePointRejected");
            if (!File.Exists(_manifestPath))
                return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.DefaultedMissing);
            var bytes = ReadBoundedFile(_manifestPath, DockIconManifestJson.MaximumCharacters * 4);
            if (bytes is null)
                return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.RecoveredInvalid, "IconManifestTooLarge");

            var json = new UTF8Encoding(false, true).GetString(bytes);
            var decoded = DockIconManifestJson.Deserialize(json);
            return decoded.Status switch
            {
                DockIconManifestDecodeStatus.Loaded => new DockIconManifestLoadResult(decoded.Manifest, DockIconManifestLoadStatus.Loaded),
                DockIconManifestDecodeStatus.UnsupportedVersion => new DockIconManifestLoadResult(
                    DockIconManifest.Empty, DockIconManifestLoadStatus.UnsupportedVersion, decoded.DiagnosticCode),
                _ => new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.RecoveredInvalid, decoded.DiagnosticCode)
            };
        }
        catch (UnauthorizedAccessException)
        {
            return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.AccessDenied, "IconManifestAccessDenied");
        }
        catch (SecurityException)
        {
            return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.AccessDenied, "IconManifestAccessDenied");
        }
        catch (IOException)
        {
            return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.IoFailure, "IconManifestReadFailed");
        }
        catch (DecoderFallbackException)
        {
            return new DockIconManifestLoadResult(DockIconManifest.Empty, DockIconManifestLoadStatus.RecoveredInvalid, "IconManifestInvalidUtf8");
        }
    }

    public DockIconManifestSaveResult Save(DockIconManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        string? temporaryPath = null;
        try
        {
            var json = DockIconManifestJson.Serialize(manifest);
            if (ContainsReparsePoint(_assetRoot))
                return new DockIconManifestSaveResult(DockIconManifestSaveStatus.IoFailure, "IconManifestReparsePointRejected");
            Directory.CreateDirectory(_assetRoot);
            if (ContainsReparsePoint(_manifestPath))
                return new DockIconManifestSaveResult(DockIconManifestSaveStatus.IoFailure, "IconManifestReparsePointRejected");
            temporaryPath = Path.Combine(_assetRoot, $"manifest.{Guid.NewGuid():N}.tmp");
            File.WriteAllText(temporaryPath, json, new UTF8Encoding(false));
            if (File.Exists(_manifestPath))
                File.Replace(temporaryPath, _manifestPath, null, ignoreMetadataErrors: true);
            else
                File.Move(temporaryPath, _manifestPath);
            return new DockIconManifestSaveResult(DockIconManifestSaveStatus.Saved);
        }
        catch (UnauthorizedAccessException)
        {
            return new DockIconManifestSaveResult(DockIconManifestSaveStatus.AccessDenied, "IconManifestAccessDenied");
        }
        catch (SecurityException)
        {
            return new DockIconManifestSaveResult(DockIconManifestSaveStatus.AccessDenied, "IconManifestAccessDenied");
        }
        catch (IOException)
        {
            return new DockIconManifestSaveResult(DockIconManifestSaveStatus.IoFailure, "IconManifestWriteFailed");
        }
        catch (InvalidOperationException)
        {
            return new DockIconManifestSaveResult(DockIconManifestSaveStatus.IoFailure, "IconManifestInvalid");
        }
        finally
        {
            if (temporaryPath is not null)
            {
                try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    public async ValueTask<DockIconAssetReadResult> ReadLocalAssetAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        try
        {
            var segments = relativePath.Replace('\\', '/').Split('/');
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(':')
                || segments.Any(segment => segment is ".." or "." or ""))
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetPathInvalid");

            var path = Path.GetFullPath(Path.Combine(_assetRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var rootPrefix = _assetRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetPathOutsideRoot");
            if (ContainsReparsePoint(path))
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetReparsePointRejected");
            if (!File.Exists(path))
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.NotFound, "IconAssetMissing");

            var extension = Path.GetExtension(path);
            var format = extension.Equals(".ico", StringComparison.OrdinalIgnoreCase)
                ? DockIconFormat.Ico
                : extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
                    ? DockIconFormat.Png
                    : (DockIconFormat?)null;
            if (format is null)
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetFormatUnsupported");

            var bytes = await ReadBoundedFileAsync(path, DockIconAsset.MaximumEncodedBytes, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length < 24)
                return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetSizeInvalid");
            return DockIconAssetReadResult.Found(DockIconAsset.Create(bytes, format.Value));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (UnauthorizedAccessException)
        {
            return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.AccessDenied, "IconAssetAccessDenied");
        }
        catch (SecurityException)
        {
            return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.AccessDenied, "IconAssetAccessDenied");
        }
        catch (IOException)
        {
            return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Failed, "IconAssetReadFailed");
        }
        catch (ArgumentException)
        {
            return DockIconAssetReadResult.Unavailable(DockIconAssetReadStatus.Invalid, "IconAssetInvalid");
        }
    }

    private static bool ContainsReparsePoint(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        if (string.IsNullOrEmpty(root))
            return true;

        var current = root;
        foreach (var segment in fullPath[root.Length..].Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                     .Where(segment => segment.Length > 0))
        {
            current = Path.Combine(current, segment);
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(current);
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }

            if ((attributes & FileAttributes.ReparsePoint) != 0)
                return true;
        }

        return false;
    }

    private static async ValueTask<byte[]?> ReadBoundedFileAsync(
        string path,
        int maximumBytes,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        if (stream.Length < 0 || stream.Length > maximumBytes)
            return null;

        var bytes = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        if (stream.ReadByte() != -1)
            return null;
        return bytes;
    }

    private static byte[]? ReadBoundedFile(string path, int maximumBytes)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length < 0 || stream.Length > maximumBytes)
            return null;

        var bytes = new byte[(int)stream.Length];
        stream.ReadExactly(bytes);
        if (stream.ReadByte() != -1)
            return null;
        return bytes;
    }
}
