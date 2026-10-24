using System.Collections.Concurrent;
using System.Security;
using System.Text;
using GlassDock.App;
using GlassDock.Core;

namespace GlassDock.Persistence;

public enum DockConfigurationStoreLoadStatus
{
    LoadedPrimary = 1,
    MigratedPrimary = 2,
    RecoveredFromBackup1 = 3,
    RecoveredFromBackup2 = 4,
    DefaultedMissing = 5,
    RecoveredInvalidData = 6,
    RecoveredUnsupportedVersion = 7,
    AccessDenied = 8,
    IoFailure = 9
}

public sealed record DockConfigurationStoreLoadResult(
    DockState State,
    DockConfigurationStoreLoadStatus Status,
    DockConfigurationLoadStatus? CodecStatus = null,
    string? DiagnosticCode = null)
{
    public bool LoadedFromBackup => Status is DockConfigurationStoreLoadStatus.RecoveredFromBackup1
        or DockConfigurationStoreLoadStatus.RecoveredFromBackup2;

    public bool RequiresWriteBack => LoadedFromBackup
        || Status == DockConfigurationStoreLoadStatus.MigratedPrimary;
}

public enum DockConfigurationStoreSaveStatus
{
    Saved = 1,
    AccessDenied = 2,
    IoFailure = 3
}

public sealed record DockConfigurationStoreSaveResult(
    DockConfigurationStoreSaveStatus Status,
    string? DiagnosticCode = null)
{
    public bool Succeeded => Status == DockConfigurationStoreSaveStatus.Saved;
}

/// <summary>
/// File-system adapter for the Core JSON codec. The caller must explicitly choose an application-owned
/// directory; this type never discovers or changes a per-user settings path on its own.
/// </summary>
public sealed class DockConfigurationFileStore : IDockStateStore
{
    internal const string ConfigurationFileName = "dock-config.json";
    internal const string Backup1FileName = "dock-config.json.bak.1";
    internal const string Backup2FileName = "dock-config.json.bak.2";
    private const int MaximumUtf8Bytes = DockConfigurationJson.MaximumConfigurationCharacters * 4 + 3;
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly ConcurrentDictionary<string, object> DirectoryLocks = new(StringComparer.OrdinalIgnoreCase);
    private readonly string _directoryPath;
    private readonly IDockConfigurationFileOperations _fileOperations;
    private readonly object _directoryLock;

    public DockConfigurationFileStore(string directoryPath)
        : this(directoryPath, new PhysicalDockConfigurationFileOperations())
    {
    }

    internal DockConfigurationFileStore(string directoryPath, IDockConfigurationFileOperations fileOperations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);
        ArgumentNullException.ThrowIfNull(fileOperations);
        _directoryPath = Path.GetFullPath(directoryPath);
        _fileOperations = fileOperations;
        _directoryLock = DirectoryLocks.GetOrAdd(_directoryPath, static _ => new object());
    }

    internal string ConfigurationFilePath => Path.Combine(_directoryPath, ConfigurationFileName);

    public DockConfigurationStoreLoadResult Load()
    {
        lock (_directoryLock)
            return LoadCore();
    }

    private DockConfigurationStoreLoadResult LoadCore()
    {
        Candidate? primary = null;
        var backupCandidates = new List<Candidate>();
        try
        {
            var primaryPath = ConfigurationFilePath;
            if (_fileOperations.FileExists(primaryPath))
            {
                primary = ReadCandidate(primaryPath);
                if (primary.IsValid)
                    return ToPrimaryResult(primary);
            }

            foreach (var (fileName, status) in new[]
            {
                (Backup1FileName, DockConfigurationStoreLoadStatus.RecoveredFromBackup1),
                (Backup2FileName, DockConfigurationStoreLoadStatus.RecoveredFromBackup2)
            })
            {
                var path = Path.Combine(_directoryPath, fileName);
                if (!_fileOperations.FileExists(path))
                    continue;

                var candidate = ReadCandidate(path);
                backupCandidates.Add(candidate);
                if (candidate.IsValid)
                    return ToBackupResult(candidate, status, primary?.DiagnosticCode);
            }
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(DockConfigurationStoreLoadStatus.AccessDenied, "AccessDenied");
        }
        catch (SecurityException)
        {
            return Failure(DockConfigurationStoreLoadStatus.AccessDenied, "AccessDenied");
        }
        catch (IOException)
        {
            return Failure(DockConfigurationStoreLoadStatus.IoFailure, "ReadFailed");
        }

        if (primary is not null)
            return ToFailureResult(primary);
        if (backupCandidates.Count > 0)
            return ToFailureResult(backupCandidates[0]);

        return new DockConfigurationStoreLoadResult(
            DockState.CreateDefault(), DockConfigurationStoreLoadStatus.DefaultedMissing,
            DockConfigurationLoadStatus.DefaultedMissing);
    }

    public DockConfigurationStoreSaveResult Save(DockState state)
    {
        lock (_directoryLock)
            return SaveCore(state);
    }

    DockState IDockStateStore.LoadState() => Load().State;

    DockStateStoreSaveResult IDockStateStore.SaveState(DockState state)
    {
        var result = Save(state);
        return new DockStateStoreSaveResult(result.Succeeded, result.DiagnosticCode);
    }

    private DockConfigurationStoreSaveResult SaveCore(DockState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var json = DockConfigurationJson.Serialize(state);
        var contents = StrictUtf8.GetBytes(json);
        var primaryPath = ConfigurationFilePath;
        var backup1Path = Path.Combine(_directoryPath, Backup1FileName);
        var backup2Path = Path.Combine(_directoryPath, Backup2FileName);
        var temporaryPath = Path.Combine(_directoryPath, $".dock-config.{Guid.NewGuid():N}.tmp");

        try
        {
            _fileOperations.CreateDirectory(_directoryPath);
            using (var stream = _fileOperations.CreateNewWriteThrough(temporaryPath))
            {
                stream.Write(contents);
                _fileOperations.FlushToDisk(stream);
            }

            if (_fileOperations.FileExists(primaryPath))
            {
                if (_fileOperations.FileExists(backup1Path))
                    _fileOperations.Move(backup1Path, backup2Path, overwrite: true);
                _fileOperations.Replace(temporaryPath, primaryPath, backup1Path);
            }
            else
            {
                _fileOperations.Move(temporaryPath, primaryPath, overwrite: false);
            }

            return new DockConfigurationStoreSaveResult(DockConfigurationStoreSaveStatus.Saved);
        }
        catch (UnauthorizedAccessException)
        {
            return new DockConfigurationStoreSaveResult(DockConfigurationStoreSaveStatus.AccessDenied, "AccessDenied");
        }
        catch (SecurityException)
        {
            return new DockConfigurationStoreSaveResult(DockConfigurationStoreSaveStatus.AccessDenied, "AccessDenied");
        }
        catch (IOException)
        {
            return new DockConfigurationStoreSaveResult(DockConfigurationStoreSaveStatus.IoFailure, "WriteFailed");
        }
        finally
        {
            try
            {
                if (_fileOperations.FileExists(temporaryPath))
                    _fileOperations.Delete(temporaryPath);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
        }
    }

    private Candidate ReadCandidate(string path)
    {
        try
        {
            var bytes = ReadBoundedBytes(path);
            var hasUtf8Preamble = bytes.Length >= 3
                && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
            var offset = hasUtf8Preamble ? 3 : 0;
            var json = StrictUtf8.GetString(bytes, offset, bytes.Length - offset);
            if (json.Length > DockConfigurationJson.MaximumConfigurationCharacters)
                return Candidate.Invalid("ConfigurationTooLarge");

            var loaded = DockConfigurationJson.Deserialize(json);
            if (loaded.Status == DockConfigurationLoadStatus.DefaultedMissing)
                return Candidate.Invalid("EmptyConfigurationFile");

            return loaded.Status is DockConfigurationLoadStatus.LoadedCurrentVersion
                or DockConfigurationLoadStatus.MigratedFromVersion1
                or DockConfigurationLoadStatus.MigratedFromVersion2
                    ? Candidate.Valid(loaded)
                    : Candidate.Invalid(loaded.Status, loaded.DiagnosticCode);
        }
        catch (DecoderFallbackException)
        {
            return Candidate.Invalid("InvalidUtf8");
        }
        catch (UnauthorizedAccessException)
        {
            return Candidate.AccessDenied();
        }
        catch (SecurityException)
        {
            return Candidate.AccessDenied();
        }
        catch (ConfigurationTooLargeException)
        {
            return Candidate.Invalid("ConfigurationTooLarge");
        }
        catch (IOException)
        {
            return Candidate.IoFailure();
        }
    }

    private byte[] ReadBoundedBytes(string path)
    {
        if (_fileOperations.GetLength(path) > MaximumUtf8Bytes)
            throw new ConfigurationTooLargeException();

        using var stream = _fileOperations.OpenRead(path);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var count = stream.Read(chunk, 0, chunk.Length);
            if (count == 0)
                break;
            if (buffer.Length + count > MaximumUtf8Bytes)
                throw new ConfigurationTooLargeException();
            buffer.Write(chunk, 0, count);
        }

        return buffer.ToArray();
    }

    private static DockConfigurationStoreLoadResult ToPrimaryResult(Candidate candidate)
    {
        var loaded = candidate.Loaded!;
        var migrated = loaded.Status is DockConfigurationLoadStatus.MigratedFromVersion1
            or DockConfigurationLoadStatus.MigratedFromVersion2;
        return new DockConfigurationStoreLoadResult(
            loaded.State,
            migrated ? DockConfigurationStoreLoadStatus.MigratedPrimary : DockConfigurationStoreLoadStatus.LoadedPrimary,
            loaded.Status);
    }

    private static DockConfigurationStoreLoadResult ToBackupResult(
        Candidate candidate,
        DockConfigurationStoreLoadStatus backupStatus,
        string? primaryDiagnostic)
    {
        var loaded = candidate.Loaded!;
        var diagnostic = primaryDiagnostic is null ? null : $"Primary:{primaryDiagnostic}";
        return new DockConfigurationStoreLoadResult(loaded.State, backupStatus, loaded.Status, diagnostic);
    }

    private static DockConfigurationStoreLoadResult ToFailureResult(Candidate candidate)
    {
        if (candidate.StoreStatus == DockConfigurationStoreLoadStatus.AccessDenied)
            return Failure(DockConfigurationStoreLoadStatus.AccessDenied, candidate.DiagnosticCode ?? "AccessDenied");
        if (candidate.StoreStatus == DockConfigurationStoreLoadStatus.IoFailure)
            return Failure(DockConfigurationStoreLoadStatus.IoFailure, candidate.DiagnosticCode ?? "ReadFailed");

        var codecStatus = candidate.CodecStatus ?? DockConfigurationLoadStatus.RecoveredInvalidData;
        var status = codecStatus == DockConfigurationLoadStatus.RecoveredUnsupportedVersion
            ? DockConfigurationStoreLoadStatus.RecoveredUnsupportedVersion
            : DockConfigurationStoreLoadStatus.RecoveredInvalidData;
        return Failure(status, candidate.DiagnosticCode ?? "NoValidConfigurationCopy", codecStatus);
    }

    private static DockConfigurationStoreLoadResult Failure(
        DockConfigurationStoreLoadStatus status,
        string diagnosticCode,
        DockConfigurationLoadStatus? codecStatus = null) =>
        new(DockState.CreateDefault(), status, codecStatus, diagnosticCode);

    private sealed record Candidate(
        DockConfigurationLoadResult? Loaded,
        DockConfigurationLoadStatus? CodecStatus,
        DockConfigurationStoreLoadStatus? StoreStatus,
        string? DiagnosticCode)
    {
        public bool IsValid => Loaded is not null;

        public static Candidate Valid(DockConfigurationLoadResult loaded) => new(loaded, loaded.Status, null, null);

        public static Candidate Invalid(DockConfigurationLoadStatus status, string? diagnostic) =>
            new(null, status, null, diagnostic ?? "InvalidConfiguration");

        public static Candidate Invalid(string diagnostic) =>
            new(null, DockConfigurationLoadStatus.RecoveredInvalidData, null, diagnostic);

        public static Candidate AccessDenied() =>
            new(null, null, DockConfigurationStoreLoadStatus.AccessDenied, "AccessDenied");

        public static Candidate IoFailure() =>
            new(null, null, DockConfigurationStoreLoadStatus.IoFailure, "ReadFailed");
    }

    private sealed class ConfigurationTooLargeException : IOException { }
}

internal interface IDockConfigurationFileOperations
{
    void CreateDirectory(string path);
    bool FileExists(string path);
    long GetLength(string path);
    Stream OpenRead(string path);
    Stream CreateNewWriteThrough(string path);
    void FlushToDisk(Stream stream);
    void Replace(string sourcePath, string destinationPath, string backupPath);
    void Move(string sourcePath, string destinationPath, bool overwrite);
    void Delete(string path);
}

internal sealed class PhysicalDockConfigurationFileOperations : IDockConfigurationFileOperations
{
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public bool FileExists(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.Directory) == 0;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }
    public long GetLength(string path) => new FileInfo(path).Length;

    public Stream OpenRead(string path) => new FileStream(
        path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.SequentialScan);

    public Stream CreateNewWriteThrough(string path) => new FileStream(
        path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, FileOptions.WriteThrough);

    public void FlushToDisk(Stream stream)
    {
        if (stream is FileStream fileStream)
            fileStream.Flush(flushToDisk: true);
        else
            stream.Flush();
    }

    public void Replace(string sourcePath, string destinationPath, string backupPath) =>
        File.Replace(sourcePath, destinationPath, backupPath, ignoreMetadataErrors: false);

    public void Move(string sourcePath, string destinationPath, bool overwrite) =>
        File.Move(sourcePath, destinationPath, overwrite);

    public void Delete(string path) => File.Delete(path);
}
