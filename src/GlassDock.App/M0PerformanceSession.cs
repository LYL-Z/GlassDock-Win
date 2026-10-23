using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using GlassDock.Core;

namespace GlassDock.App;

/// <summary>
/// Opt-in M0 trace writer. It writes only under an explicitly configured D: state root.
/// </summary>
public sealed class M0PerformanceSession : IDisposable
{
    private readonly object _gate = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private readonly DockPerformanceRecorder _recorder = new();
    private readonly Channel<string> _traceLines = Channel.CreateUnbounded<string>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
    private readonly System.Threading.Timer _resourceTimer;
    private readonly Task _traceWriterTask;
    private readonly string _tracePath;
    private readonly List<ProcessPhaseWindow> _phaseResourceWindows = [];
    private long _previousResourceTimestamp;
    private double _previousCpuMilliseconds;
    private DockPerformancePhase _phaseWindow = DockPerformancePhase.Idle;
    private DateTimeOffset _phaseWindowStartUtc;
    private long _phaseWindowStartTimestamp;
    private double _phaseWindowStartCpuMilliseconds;
    private bool _disposed;
    private Exception? _traceWriterFailure;

    private M0PerformanceSession(string stateRoot, string framework)
    {
        var traceDirectory = Path.Combine(stateRoot, "performance");
        Directory.CreateDirectory(traceDirectory);
        _tracePath = Path.Combine(traceDirectory,
            $"m0-performance-{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{framework}.jsonl");
        _process.Refresh();
        _phaseWindowStartUtc = DateTimeOffset.UtcNow;
        _phaseWindowStartTimestamp = Stopwatch.GetTimestamp();
        _phaseWindowStartCpuMilliseconds = _process.TotalProcessorTime.TotalMilliseconds;
        _traceWriterTask = Task.Run(WriteTraceAsync);
        Append(new
        {
            Event = "session-start",
            TimestampUtc = DateTimeOffset.UtcNow,
            Framework = framework,
            ProcessId = Environment.ProcessId,
            ProcessPath = Environment.ProcessPath,
            Measures = new[]
            {
                "logical-update request intervals",
                "framework render callback intervals before the framework render pass",
                "tracked animation duration",
                "process CPU and memory once per second"
            },
            DoesNotMeasure = new[]
            {
                "GPU command submission",
                "per-process DWM present intervals",
                "physical display scanout"
            }
        });
        _resourceTimer = new System.Threading.Timer(_ => SampleProcessResources(), null, 1000, 1000);
    }

    public DockPerformanceRecorder Recorder => _recorder;
    public string TracePath => _tracePath;

    public static M0PerformanceSession? CreateIfEnabled(string framework)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("GLASSDOCK_M0_PERF_TRACE"), "1",
                StringComparison.Ordinal))
            return null;

        var stateRoot = Environment.GetEnvironmentVariable("GLASSDOCK_POC_STATE_ROOT");
        if (string.IsNullOrWhiteSpace(stateRoot))
            throw new InvalidOperationException(
                "GLASSDOCK_POC_STATE_ROOT must point to a D: directory before enabling M0 performance tracing.");

        var fullPath = Path.GetFullPath(stateRoot);
        if (!string.Equals(Path.GetPathRoot(fullPath), @"D:\", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "M0 performance trace output is restricted to a D: directory.");

        return new M0PerformanceSession(fullPath, framework);
    }

    public void RecordLogicalUpdate()
    {
        var timestamp = Stopwatch.GetTimestamp();
        _recorder.RecordLogicalUpdate(timestamp);
        Append(new
        {
            Event = "logical-update",
            TimestampUtc = DateTimeOffset.UtcNow,
            MonotonicTimestamp = timestamp,
            Phase = _recorder.CurrentPhase.ToString()
        });
    }

    public void RecordFrameworkRenderCallback() =>
        _recorder.RecordFrameworkRenderCallback(Stopwatch.GetTimestamp());

    public long BeginAnimation()
    {
        var timestamp = Stopwatch.GetTimestamp();
        var phaseBefore = _recorder.CurrentPhase;
        var id = _recorder.BeginAnimation(timestamp);
        var phaseAfter = _recorder.CurrentPhase;
        if (phaseBefore != phaseAfter)
            TransitionResourcePhase(phaseAfter);
        Append(new
        {
            Event = "animation-start",
            TimestampUtc = DateTimeOffset.UtcNow,
            MonotonicTimestamp = timestamp,
            AnimationId = id
        });
        return id;
    }

    public void EndAnimation(long id, bool completed)
    {
        var timestamp = Stopwatch.GetTimestamp();
        var phaseBefore = _recorder.CurrentPhase;
        if (!_recorder.EndAnimation(id, timestamp, completed))
            return;
        var phaseAfter = _recorder.CurrentPhase;
        if (phaseBefore != phaseAfter)
            TransitionResourcePhase(phaseAfter);

        Append(new
        {
            Event = completed ? "animation-completed" : "animation-cancelled",
            TimestampUtc = DateTimeOffset.UtcNow,
            MonotonicTimestamp = timestamp,
            AnimationId = id
        });
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            CapturePhaseResourceWindowLocked();
            _disposed = true;
        }

        _resourceTimer.Dispose();
        SampleProcessResources(allowDisposed: true);
        ProcessPhaseWindow[] phaseWindows;
        lock (_gate)
            phaseWindows = _phaseResourceWindows.ToArray();
        foreach (var window in phaseWindows)
            Append(new { Event = "process-phase-window", Window = window }, allowAfterDispose: true);
        Append(new
        {
            Event = "session-summary",
            TimestampUtc = DateTimeOffset.UtcNow,
            Snapshot = _recorder.CreateSnapshot()
        }, allowAfterDispose: true);
        _traceLines.Writer.TryComplete();
        _traceWriterTask.GetAwaiter().GetResult();
        if (_traceWriterFailure is not null)
            DockLogger.Error($"M0 performance trace could not be written: {_tracePath}", _traceWriterFailure);
        _process.Dispose();
    }

    private void SampleProcessResources(bool allowDisposed = false)
    {
        lock (_gate)
        {
            if (_disposed && !allowDisposed)
                return;

            try
            {
                _process.Refresh();
                var timestamp = Stopwatch.GetTimestamp();
                var cpuMilliseconds = _process.TotalProcessorTime.TotalMilliseconds;
                double? oneCoreCpuPercent = null;
                double? machineNormalizedCpuPercent = null;
                if (_previousResourceTimestamp != 0)
                {
                    var elapsedMilliseconds = (timestamp - _previousResourceTimestamp) * 1000d / Stopwatch.Frequency;
                    var cpuDeltaMilliseconds = cpuMilliseconds - _previousCpuMilliseconds;
                    if (elapsedMilliseconds > 0 && cpuDeltaMilliseconds >= 0)
                    {
                        oneCoreCpuPercent = cpuDeltaMilliseconds / elapsedMilliseconds * 100d;
                        machineNormalizedCpuPercent = oneCoreCpuPercent / Environment.ProcessorCount;
                    }
                }

                _previousResourceTimestamp = timestamp;
                _previousCpuMilliseconds = cpuMilliseconds;
                Append(new
                {
                    Event = "process-resource-sample",
                    TimestampUtc = DateTimeOffset.UtcNow,
                    MonotonicTimestamp = timestamp,
                    PhaseAtSample = _recorder.CurrentPhase.ToString(),
                    CpuIntervalIncludesPhaseTransitions = true,
                    CpuPercentOneCoreOverPreviousTimerInterval = oneCoreCpuPercent,
                    CpuPercentMachineNormalizedOverPreviousTimerInterval = machineNormalizedCpuPercent,
                    WorkingSetBytes = _process.WorkingSet64,
                    PrivateBytes = _process.PrivateMemorySize64,
                    GpuUtilization = (double?)null,
                    GpuMemoryBytes = (long?)null
                }, allowAfterDispose: allowDisposed);
            }
            catch (ObjectDisposedException)
            {
                // Disposal may race with an already queued timer callback.
            }
            catch (InvalidOperationException)
            {
                // The process can be exiting while the timer callback is in flight.
            }
        }
    }

    private void TransitionResourcePhase(DockPerformancePhase nextPhase)
    {
        lock (_gate)
        {
            if (_disposed || nextPhase == _phaseWindow)
                return;

            var boundary = CapturePhaseResourceWindowLocked();
            _phaseWindow = nextPhase;
            _phaseWindowStartUtc = boundary?.TimestampUtc ?? DateTimeOffset.UtcNow;
            _phaseWindowStartTimestamp = boundary?.MonotonicTimestamp ?? Stopwatch.GetTimestamp();
            _phaseWindowStartCpuMilliseconds = boundary?.ProcessCpuMilliseconds ?? double.NaN;
        }
    }

    private ResourcePhaseBoundary? CapturePhaseResourceWindowLocked()
    {
        try
        {
            _process.Refresh();
            var cpuMilliseconds = _process.TotalProcessorTime.TotalMilliseconds;
            var endTimestamp = Stopwatch.GetTimestamp();
            var endUtc = DateTimeOffset.UtcNow;
            var elapsedMilliseconds = (endTimestamp - _phaseWindowStartTimestamp) * 1000d / Stopwatch.Frequency;
            var cpuDeltaMilliseconds = cpuMilliseconds - _phaseWindowStartCpuMilliseconds;
            var cpuDeltaIsValid = double.IsFinite(cpuDeltaMilliseconds) && cpuDeltaMilliseconds >= 0;
            var oneCoreCpuPercent = elapsedMilliseconds > 0 && cpuDeltaIsValid
                ? cpuDeltaMilliseconds / elapsedMilliseconds * 100d
                : (double?)null;
            _phaseResourceWindows.Add(new ProcessPhaseWindow(
                _phaseWindow.ToString(),
                _phaseWindowStartUtc,
                endUtc,
                elapsedMilliseconds,
                cpuDeltaIsValid ? cpuDeltaMilliseconds : null,
                oneCoreCpuPercent,
                oneCoreCpuPercent / Environment.ProcessorCount,
                _process.WorkingSet64,
                _process.PrivateMemorySize64));
            return new ResourcePhaseBoundary(endUtc, endTimestamp, cpuMilliseconds);
        }
        catch (ObjectDisposedException)
        {
            // The process wrapper may already be disposed during shutdown.
            return null;
        }
        catch (InvalidOperationException)
        {
            // The process may exit while a phase window is being closed.
            return null;
        }
    }

    private sealed record ResourcePhaseBoundary(
        DateTimeOffset TimestampUtc,
        long MonotonicTimestamp,
        double ProcessCpuMilliseconds);

    private sealed record ProcessPhaseWindow(
        string Phase,
        DateTimeOffset StartUtc,
        DateTimeOffset EndUtc,
        double DurationMilliseconds,
        double? ProcessCpuMilliseconds,
        double? CpuPercentOneCore,
        double? CpuPercentMachineNormalized,
        long WorkingSetBytesAtEnd,
        long PrivateBytesAtEnd);

    private void Append<T>(T entry, bool allowAfterDispose = false)
    {
        var json = JsonSerializer.Serialize(entry);
        lock (_gate)
        {
            if (_disposed && !allowAfterDispose)
                return;
            if (_traceWriterFailure is not null)
                return;
            if (!_traceLines.Writer.TryWrite(json))
                _traceWriterFailure ??= new IOException("The M0 performance trace writer has closed unexpectedly.");
        }
    }

    private async Task WriteTraceAsync()
    {
        try
        {
            await using var stream = new FileStream(_tracePath, FileMode.CreateNew, FileAccess.Write,
                FileShare.Read, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using var writer = new StreamWriter(stream);
            await foreach (var line in _traceLines.Reader.ReadAllAsync())
                await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
        catch (Exception exception)
        {
            lock (_gate)
            {
                _traceWriterFailure ??= exception;
                _traceLines.Writer.TryComplete(exception);
            }
        }
    }
}
