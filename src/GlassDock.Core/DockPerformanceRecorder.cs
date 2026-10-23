namespace GlassDock.Core;

public enum DockPerformancePhase
{
    Idle,
    Animation
}

public sealed record DockTimingDistribution(
    int SampleCount,
    double? P50Milliseconds,
    double? P95Milliseconds,
    double? P99Milliseconds);

public sealed record DockPerformanceSnapshot(
    long LogicalUpdateCount,
    long FrameworkRenderCallbackCount,
    int CompletedAnimationCount,
    int CancelledAnimationCount,
    int OpenAnimationCount,
    DockTimingDistribution LogicalUpdateIntervals,
    DockTimingDistribution IdleFrameworkRenderIntervals,
    DockTimingDistribution AnimationFrameworkRenderIntervals,
    DockTimingDistribution CompletedAnimationDurations,
    DockTimingDistribution CancelledAnimationDurations,
    IReadOnlyList<double> LogicalUpdateIntervalSamplesMilliseconds,
    IReadOnlyList<double> IdleFrameworkRenderIntervalSamplesMilliseconds,
    IReadOnlyList<double> AnimationFrameworkRenderIntervalSamplesMilliseconds,
    IReadOnlyList<double> CompletedAnimationDurationSamplesMilliseconds,
    IReadOnlyList<double> CancelledAnimationDurationSamplesMilliseconds);

/// <summary>
/// Records app-side event intervals only. Framework render callbacks are not GPU submission,
/// DWM presentation, or physical display scanout measurements.
/// </summary>
public sealed class DockPerformanceRecorder
{
    private const int MaximumRetainedSamplesPerSeries = 120_000;
    private readonly object _gate = new();
    private readonly long _timestampFrequency;
    private readonly List<double> _logicalIntervals = [];
    private readonly List<double> _idleRenderIntervals = [];
    private readonly List<double> _animationRenderIntervals = [];
    private readonly List<double> _completedAnimationDurations = [];
    private readonly List<double> _cancelledAnimationDurations = [];
    private readonly Dictionary<long, long> _openAnimations = [];
    private long? _lastLogicalUpdateTimestamp;
    private long? _lastFrameworkRenderTimestamp;
    private DockPerformancePhase? _lastFrameworkRenderPhase;
    private long _logicalUpdateCount;
    private long _frameworkRenderCallbackCount;
    private long _nextAnimationId;
    private int _completedAnimationCount;
    private int _cancelledAnimationCount;

    public DockPerformanceRecorder(long? timestampFrequency = null)
    {
        _timestampFrequency = timestampFrequency ?? System.Diagnostics.Stopwatch.Frequency;
        if (_timestampFrequency <= 0)
            throw new ArgumentOutOfRangeException(nameof(timestampFrequency));
    }

    public DockPerformancePhase CurrentPhase
    {
        get
        {
            lock (_gate)
                return _openAnimations.Count == 0 ? DockPerformancePhase.Idle : DockPerformancePhase.Animation;
        }
    }

    public void RecordLogicalUpdate(long timestamp)
    {
        lock (_gate)
        {
            _logicalUpdateCount++;
            if (_lastLogicalUpdateTimestamp is { } previous)
                AddSample(_logicalIntervals, ToMilliseconds(timestamp - previous));
            _lastLogicalUpdateTimestamp = timestamp;
        }
    }

    public void RecordFrameworkRenderCallback(long timestamp)
    {
        lock (_gate)
        {
            _frameworkRenderCallbackCount++;
            var phase = _openAnimations.Count == 0
                ? DockPerformancePhase.Idle
                : DockPerformancePhase.Animation;
            if (_lastFrameworkRenderTimestamp is { } previous && _lastFrameworkRenderPhase == phase)
            {
                var target = phase == DockPerformancePhase.Idle ? _idleRenderIntervals : _animationRenderIntervals;
                AddSample(target, ToMilliseconds(timestamp - previous));
            }

            _lastFrameworkRenderTimestamp = timestamp;
            _lastFrameworkRenderPhase = phase;
        }
    }

    public long BeginAnimation(long timestamp)
    {
        lock (_gate)
        {
            var id = ++_nextAnimationId;
            _openAnimations.Add(id, timestamp);
            return id;
        }
    }

    public bool EndAnimation(long id, long timestamp, bool completed)
    {
        lock (_gate)
        {
            if (!_openAnimations.Remove(id, out var startTimestamp))
                return false;

            var duration = ToMilliseconds(timestamp - startTimestamp);
            if (completed)
            {
                _completedAnimationCount++;
                AddSample(_completedAnimationDurations, duration);
            }
            else
            {
                _cancelledAnimationCount++;
                AddSample(_cancelledAnimationDurations, duration);
            }

            return true;
        }
    }

    public DockPerformanceSnapshot CreateSnapshot()
    {
        lock (_gate)
        {
            var logical = _logicalIntervals.ToArray();
            var idleRender = _idleRenderIntervals.ToArray();
            var animationRender = _animationRenderIntervals.ToArray();
            var completedAnimations = _completedAnimationDurations.ToArray();
            var cancelledAnimations = _cancelledAnimationDurations.ToArray();

            return new DockPerformanceSnapshot(
                _logicalUpdateCount,
                _frameworkRenderCallbackCount,
                _completedAnimationCount,
                _cancelledAnimationCount,
                _openAnimations.Count,
                Distribution(logical),
                Distribution(idleRender),
                Distribution(animationRender),
                Distribution(completedAnimations),
                Distribution(cancelledAnimations),
                logical,
                idleRender,
                animationRender,
                completedAnimations,
                cancelledAnimations);
        }
    }

    private double ToMilliseconds(long ticks) => ticks * 1000d / _timestampFrequency;

    private static void AddSample(List<double> samples, double value)
    {
        if (double.IsFinite(value) && value >= 0 && samples.Count < MaximumRetainedSamplesPerSeries)
            samples.Add(value);
    }

    private static DockTimingDistribution Distribution(IReadOnlyCollection<double> samples)
    {
        if (samples.Count == 0)
            return new DockTimingDistribution(0, null, null, null);

        var sorted = samples.Order().ToArray();
        return new DockTimingDistribution(
            sorted.Length,
            Percentile(sorted, 0.50),
            Percentile(sorted, 0.95),
            Percentile(sorted, 0.99));
    }

    private static double Percentile(IReadOnlyList<double> sorted, double percentile)
    {
        var rank = (int)Math.Ceiling(percentile * sorted.Count);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Count - 1)];
    }
}
