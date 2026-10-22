using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace GlassDock.M0.InputProbe;

internal static class M0SafetyWatchdog
{
    private const int HotkeyId = 0x4D30;
    private const uint WmHotkey = 0x0312;
    private const uint WmQuit = 0x0012;
    private const uint PmNoRemove = 0;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private const uint VkF12 = 0x7B;
    private const uint MouseLeftUp = 0x0004;
    private const ulong SyntheticInputMarker = 0x474C415353444F43;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    private static Options? _options;
    private static int _stopRequested;
    private static uint _threadId;
    private static long _deadlineQpc;

    public static int Run(string[] args)
    {
        try
        {
            _options = Parse(args);
            if (!ValidatePaths(_options, out var validationError))
            {
                WriteReady(_options, false, validationError, 0);
                return 2;
            }

            _threadId = GetCurrentThreadId();
            _deadlineQpc = Stopwatch.GetTimestamp() + ((long)_options.TimeoutSeconds * Stopwatch.Frequency);
            _ = PeekMessage(out _, IntPtr.Zero, 0, 0, PmNoRemove); // Create this thread's queue before timer callbacks post WM_QUIT.
            if (!RegisterHotKey(IntPtr.Zero, HotkeyId, ModControl | ModShift | ModNoRepeat, VkF12))
            {
                var error = Marshal.GetLastWin32Error();
                WriteLog(_options, new { Event = "RegisterHotKeyFailed", Win32Error = error, EmergencyKey = "Ctrl+Shift+F12" });
                WriteReady(_options, false, $"RegisterHotKey failed; Win32={error}.", error);
                return 3;
            }

            WriteReady(_options, true, null, 0);
            WriteLog(_options, new { Event = "WatchdogReady", EmergencyKey = "Ctrl+Shift+F12", TimeoutSeconds = _options.TimeoutSeconds });
            using var timer = new System.Threading.Timer(_ => PollStopAndDeadline(), null, 200, 200);
            try
            {
                while (true)
                {
                    var messageResult = GetMessage(out var message, IntPtr.Zero, 0, 0);
                    if (messageResult <= 0)
                        break;
                    if (message.Message == WmHotkey && message.WParam.ToUInt64() == HotkeyId)
                    {
                        RequestStop("emergency-hotkey", killOwnedProcesses: true);
                        break;
                    }
                }
            }
            finally
            {
                UnregisterHotKey(IntPtr.Zero, HotkeyId);
            }
            return 0;
        }
        catch (Exception exception)
        {
            if (_options is not null)
            {
                WriteLog(_options, new { Event = "WatchdogException", Exception = exception.ToString() });
                WriteReady(_options, false, exception.Message, 0);
            }
            return 1;
        }
    }

    private static Options Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < args.Length; index++)
        {
            if (!args[index].StartsWith("--", StringComparison.Ordinal) || index + 1 >= args.Length)
                throw new ArgumentException("Watchdog arguments must be --name value pairs.");
            values[args[index]] = args[++index];
        }

        string RequiredPath(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? Path.GetFullPath(value)
            : throw new ArgumentException($"Missing {name}.");
        string RequiredValue(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"Missing {name}.");

        if (!int.TryParse(values.GetValueOrDefault("--timeout-seconds"), out var timeout) || timeout is < 10 or > 1800)
            throw new ArgumentOutOfRangeException("--timeout-seconds", "Timeout must be 10..1800 seconds.");
        return new Options(
            RequiredPath("--manifest"),
            RequiredPath("--stop-path"),
            RequiredPath("--ready-path"),
            RequiredPath("--log-path"),
            timeout,
            RequiredValue("--run-id"));
    }

    private static bool ValidatePaths(Options options, out string? error)
    {
        var manifest = ReadManifest(options.ManifestPath);
        var allowedRoot = Path.GetFullPath(manifest.AllowedRoot);
        var repoRoot = Path.GetFullPath(manifest.RepoRoot);
        var evidenceRoot = Path.GetFullPath(manifest.EvidenceRoot);
        if (!IsDPath(allowedRoot) || !IsDPath(repoRoot) || !IsDPath(evidenceRoot) ||
            !IsUnder(allowedRoot, evidenceRoot) || !IsUnder(evidenceRoot, options.ManifestPath) ||
            !IsUnder(evidenceRoot, options.StopPath) || !IsUnder(evidenceRoot, options.ReadyPath) ||
            !IsUnder(evidenceRoot, options.LogPath) || !string.Equals(manifest.RunId, options.RunId, StringComparison.Ordinal))
        {
            error = "Safety paths or run id do not match the D: evidence root.";
            return false;
        }
        error = null;
        return true;
    }

    private static void PollStopAndDeadline()
    {
        if (_options is null || Volatile.Read(ref _stopRequested) != 0)
            return;
        try
        {
            if (File.Exists(_options.StopPath))
            {
                var signal = JsonSerializer.Deserialize<StopSignal>(File.ReadAllText(_options.StopPath), JsonOptions);
                if (signal is not null && string.Equals(signal.RunId, _options.RunId, StringComparison.Ordinal))
                {
                    // A completion signal is written only after the runner attempts cleanup; rechecking
                    // owned processes here makes an incomplete or premature completion signal fail safe.
                    RequestStop(signal.Reason, killOwnedProcesses: true);
                }
                else
                {
                    WriteLog(_options, new { Event = "InvalidStopSignal", SignalRunId = signal?.RunId, ExpectedRunId = _options.RunId });
                    RequestStop("invalid-stop-signal", killOwnedProcesses: true);
                }
                return;
            }

            if (Stopwatch.GetTimestamp() >= _deadlineQpc)
                RequestStop("watchdog-timeout", killOwnedProcesses: true);
        }
        catch (Exception exception)
        {
            WriteLog(_options, new { Event = "WatchdogPollFailed", Exception = exception.ToString() });
            RequestStop("watchdog-internal-error", killOwnedProcesses: true);
        }
    }

    private static void RequestStop(string reason, bool killOwnedProcesses)
    {
        if (_options is null || Interlocked.Exchange(ref _stopRequested, 1) != 0)
            return;
        try
        {
            var manifest = ReadManifest(_options.ManifestPath);
            WriteStopSignal(_options, reason);
            if (killOwnedProcesses)
            {
                if (manifest.MouseButtonDown)
                    ReleaseInjectedLeftButton(_options);
                StopOwnedProcesses(_options, manifest);
                RestoreForeground(_options, manifest.ForegroundWindowHandle);
                RestoreFiles(_options, manifest);
            }
            WriteLog(_options, new { Event = "WatchdogStopped", Reason = reason, KillOwnedProcesses = killOwnedProcesses });
        }
        catch (Exception exception)
        {
            WriteLog(_options, new { Event = "WatchdogStopCleanupFailed", Reason = reason, Exception = exception.ToString() });
        }
        _ = PostThreadMessage(_threadId, WmQuit, IntPtr.Zero, IntPtr.Zero);
    }

    private static void StopOwnedProcesses(Options options, SafetyManifest manifest)
    {
        foreach (var identity in manifest.Processes)
        {
            try
            {
                using var process = Process.GetProcessById(identity.ProcessId);
                if (!MatchesOwnedIdentity(process, identity, manifest.RepoRoot))
                {
                    WriteLog(options, new { Event = "RefusedProcessTermination", identity.ProcessId, identity.ExecutablePath, Reason = "PID, image path, or start time did not match the current run manifest." });
                    continue;
                }
                process.Kill(entireProcessTree: false);
                var exited = process.WaitForExit(5000);
                if (!exited && MatchesOwnedIdentity(process, identity, manifest.RepoRoot))
                {
                    process.Kill(entireProcessTree: false);
                    exited = process.WaitForExit(5000);
                }
                WriteLog(options, new
                {
                    Event = exited ? "OwnedProcessTerminated" : "OwnedProcessTerminationTimedOut",
                    identity.ProcessId,
                    identity.ExecutablePath
                });
            }
            catch (ArgumentException)
            {
                WriteLog(options, new { Event = "OwnedProcessAlreadyExited", identity.ProcessId });
            }
            catch (InvalidOperationException exception)
            {
                WriteLog(options, new { Event = "OwnedProcessStateUnavailable", identity.ProcessId, Exception = exception.ToString() });
            }
            catch (Exception exception)
            {
                WriteLog(options, new { Event = "OwnedProcessTerminationFailed", identity.ProcessId, Exception = exception.ToString() });
            }
        }
    }

    private static bool MatchesOwnedIdentity(Process process, OwnedProcessIdentity identity, string repoRoot)
    {
        var expectedPath = Path.GetFullPath(identity.ExecutablePath);
        if (!IsDPath(expectedPath) || !IsUnder(Path.GetFullPath(repoRoot), expectedPath))
            return false;
        var actualPath = Path.GetFullPath(process.MainModule?.FileName ?? string.Empty);
        var startTimeTicks = process.StartTime.ToUniversalTime().Ticks;
        return string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase) &&
               startTimeTicks == identity.StartTimeUtcTicks;
    }

    private static void RestoreFiles(Options options, SafetyManifest manifest)
    {
        foreach (var file in manifest.FileRestores)
        {
            try
            {
                var target = Path.GetFullPath(file.TargetPath);
                if (!IsUnder(Path.GetFullPath(manifest.AllowedRoot), target))
                {
                    WriteLog(options, new { Event = "RefusedFileRestore", file.TargetPath, Reason = "Target is outside the allowed .local root." });
                    continue;
                }
                if (file.TargetExisted)
                {
                    var backup = Path.GetFullPath(file.BackupPath ?? string.Empty);
                    if (!IsUnder(Path.GetFullPath(manifest.EvidenceRoot), backup) || !File.Exists(backup))
                    {
                        WriteLog(options, new { Event = "FileRestoreFailed", file.TargetPath, Reason = "Backup missing or outside evidence root." });
                        continue;
                    }
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.Copy(backup, target, overwrite: true);
                }
                else if (File.Exists(target))
                {
                    File.Delete(target);
                }
                WriteLog(options, new { Event = "FileRestoreAttempted", file.TargetPath, file.TargetExisted });
            }
            catch (Exception exception)
            {
                WriteLog(options, new { Event = "FileRestoreFailed", file.TargetPath, Exception = exception.ToString() });
            }
        }
    }

    private static void RestoreForeground(Options options, long hwndValue)
    {
        var hwnd = new IntPtr(hwndValue);
        var success = hwndValue == 0 || (IsWindow(hwnd) && SetForegroundWindow(hwnd));
        WriteLog(options, new { Event = "ForegroundRestoreAttempted", Hwnd = $"0x{hwndValue:X}", Success = success, StillValid = hwndValue == 0 || IsWindow(hwnd) });
    }

    private static void ReleaseInjectedLeftButton(Options options)
    {
        var input = new Input
        {
            Type = 0,
            Data = new InputUnion
            {
                Mouse = new MouseInput { Flags = MouseLeftUp, ExtraInfo = new UIntPtr(SyntheticInputMarker) }
            }
        };
        var sent = SendInput(1, new[] { input }, Marshal.SizeOf<Input>());
        WriteLog(options, new { Event = "InjectedLeftButtonRelease", Sent = sent, Win32Error = sent == 1 ? 0 : Marshal.GetLastWin32Error() });
    }

    private static void WriteReady(Options options, bool success, string? error, int win32Error)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.ReadyPath)!);
        File.WriteAllText(options.ReadyPath, JsonSerializer.Serialize(new
        {
            RunId = options.RunId,
            ReadyAtUtc = DateTimeOffset.UtcNow,
            Success = success,
            EmergencyKey = "Ctrl+Shift+F12",
            Error = error,
            Win32Error = win32Error,
            ProcessId = Environment.ProcessId
        }, JsonOptions));
    }

    private static void WriteStopSignal(Options options, string reason)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.StopPath)!);
        File.WriteAllText(options.StopPath, JsonSerializer.Serialize(new StopSignal(options.RunId, reason, DateTimeOffset.UtcNow), JsonOptions));
    }

    private static void WriteLog(Options options, object entry)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(options.LogPath)!);
            File.AppendAllText(options.LogPath, JsonSerializer.Serialize(new { TimestampUtc = DateTimeOffset.UtcNow, RunId = options.RunId, Data = entry }) + Environment.NewLine);
        }
        catch
        {
            // The watchdog keeps its process boundary even if evidence storage fails.
        }
    }

    private static SafetyManifest ReadManifest(string path) =>
        JsonSerializer.Deserialize<SafetyManifest>(File.ReadAllText(path), JsonOptions) ?? throw new InvalidDataException("Safety manifest was empty.");

    private static bool IsDPath(string path) => Path.GetFullPath(path).StartsWith("D:\\", StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string parent, string candidate)
    {
        var normalizedParent = Path.GetFullPath(parent).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(normalizedParent, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record Options(string ManifestPath, string StopPath, string ReadyPath, string LogPath, int TimeoutSeconds, string RunId);
    private sealed record StopSignal(string RunId, string Reason, DateTimeOffset TimestampUtc);
    private sealed class SafetyManifest
    {
        public string RunId { get; set; } = string.Empty;
        public string AllowedRoot { get; set; } = string.Empty;
        public string RepoRoot { get; set; } = string.Empty;
        public string EvidenceRoot { get; set; } = string.Empty;
        public long DeadlineUnixMilliseconds { get; set; }
        public long ForegroundWindowHandle { get; set; }
        public bool MouseButtonDown { get; set; }
        public List<OwnedProcessIdentity> Processes { get; set; } = [];
        public List<FileRestoreEntry> FileRestores { get; set; } = [];
    }
    private sealed class OwnedProcessIdentity
    {
        public int ProcessId { get; set; }
        public string ExecutablePath { get; set; } = string.Empty;
        public long StartTimeUtcTicks { get; set; }
    }
    private sealed class FileRestoreEntry
    {
        public string TargetPath { get; set; } = string.Empty;
        public string? BackupPath { get; set; }
        public bool TargetExisted { get; set; }
    }

    [StructLayout(LayoutKind.Sequential)] private struct NativePoint { public int X; public int Y; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeMessage
    {
        public IntPtr Hwnd;
        public uint Message;
        public UIntPtr WParam;
        public IntPtr LParam;
        public uint Time;
        public NativePoint Point;
        public uint Private;
    }
    [StructLayout(LayoutKind.Sequential)] private struct MouseInput { public int Dx; public int Dy; public uint MouseData; public uint Flags; public uint Time; public UIntPtr ExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] private struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; }
    [StructLayout(LayoutKind.Sequential)] private struct Input { public uint Type; public InputUnion Data; }

    [DllImport("user32.dll", SetLastError = true)] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint virtualKey);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [DllImport("user32.dll", SetLastError = true)] private static extern int GetMessage(out NativeMessage message, IntPtr hwnd, uint minFilter, uint maxFilter);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PeekMessage(out NativeMessage message, IntPtr hwnd, uint minFilter, uint maxFilter, uint remove);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
