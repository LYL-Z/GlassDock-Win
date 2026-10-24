namespace GlassDock.Windows.Windowing;

public readonly record struct DockWindowBounds(int Left, int Top, int Right, int Bottom)
{
    public long Width => (long)Right - Left;

    public long Height => (long)Bottom - Top;

    public bool IsUsable => Width > 0 && Height > 0;
}

/// <summary>A single top-level HWND observed by EnumWindows.</summary>
public sealed record DockWindowObservation(
    nint Handle,
    uint ProcessId,
    bool IsWindow,
    bool IsVisible,
    DockWindowBounds Bounds)
{
    public int Left => Bounds.Left;

    public int Top => Bounds.Top;

    public int Right => Bounds.Right;

    public int Bottom => Bounds.Bottom;

    public long Width => Bounds.Width;

    public long Height => Bounds.Height;
}

public enum DockWindowReadinessStatus
{
    Ready = 1,
    InvalidExpectedProcess = 2,
    ZeroHandle = 3,
    HandleNotEnumerated = 4,
    DuplicateHandleObservation = 5,
    InvalidWindow = 6,
    WrongProcess = 7,
    NotVisible = 8,
    InvalidBounds = 9,
    NoVisibleCandidate = 10,
    MultipleVisibleCandidates = 11,
    ReportedHandleMismatch = 12,
    LayoutNotPrepared = 13,
    ContentNotRendered = 14
}

public sealed record DockWindowReadinessResult(
    DockWindowReadinessStatus Status,
    nint Handle,
    int VisibleCandidateCount,
    string DiagnosticCode)
{
    public bool IsReady => Status == DockWindowReadinessStatus.Ready;
}

/// <summary>
/// Pure readiness policy for the experimental preview runner. The caller supplies HWND
/// observations from Win32 EnumWindows and WPF layout/render milestones; Process.MainWindowHandle
/// is intentionally not an input because WPF tool windows may report zero there.
/// </summary>
public static class DockWindowReadinessPolicy
{
    public static DockWindowReadinessResult Evaluate(
        IEnumerable<DockWindowObservation> observations,
        int expectedProcessId,
        nint reportedHandle,
        bool layoutPrepared,
        bool contentRendered)
    {
        ArgumentNullException.ThrowIfNull(observations);
        if (expectedProcessId <= 0)
            return Result(DockWindowReadinessStatus.InvalidExpectedProcess, reportedHandle, 0);
        if (reportedHandle == nint.Zero)
            return Result(DockWindowReadinessStatus.ZeroHandle, nint.Zero, 0);

        var snapshot = observations.ToArray();
        var handleMatches = snapshot.Where(candidate => candidate.Handle == reportedHandle).ToArray();
        if (handleMatches.Length == 0)
            return Result(DockWindowReadinessStatus.HandleNotEnumerated, reportedHandle, 0);
        if (handleMatches.Length > 1)
            return Result(DockWindowReadinessStatus.DuplicateHandleObservation, reportedHandle, 0);

        var reported = handleMatches[0];
        if (!reported.IsWindow)
            return Result(DockWindowReadinessStatus.InvalidWindow, reportedHandle, 0);
        if (reported.ProcessId != (uint)expectedProcessId)
            return Result(DockWindowReadinessStatus.WrongProcess, reportedHandle, 0);
        if (!reported.IsVisible)
            return Result(DockWindowReadinessStatus.NotVisible, reportedHandle, 0);
        if (!reported.Bounds.IsUsable)
            return Result(DockWindowReadinessStatus.InvalidBounds, reportedHandle, 0);

        var visibleCandidates = snapshot.Where(candidate =>
            candidate.IsWindow
            && candidate.ProcessId == (uint)expectedProcessId
            && candidate.IsVisible
            && candidate.Bounds.IsUsable).ToArray();
        if (visibleCandidates.Length == 0)
            return Result(DockWindowReadinessStatus.NoVisibleCandidate, reportedHandle, 0);
        if (visibleCandidates.Length > 1)
            return Result(DockWindowReadinessStatus.MultipleVisibleCandidates, reportedHandle, visibleCandidates.Length);
        if (visibleCandidates[0].Handle != reportedHandle)
            return Result(DockWindowReadinessStatus.ReportedHandleMismatch, reportedHandle, visibleCandidates.Length);
        if (!layoutPrepared)
            return Result(DockWindowReadinessStatus.LayoutNotPrepared, reportedHandle, visibleCandidates.Length);
        if (!contentRendered)
            return Result(DockWindowReadinessStatus.ContentNotRendered, reportedHandle, visibleCandidates.Length);

        return Result(DockWindowReadinessStatus.Ready, reportedHandle, visibleCandidates.Length);
    }

    private static DockWindowReadinessResult Result(
        DockWindowReadinessStatus status,
        nint handle,
        int visibleCandidateCount) =>
        new(status, handle, visibleCandidateCount, $"preview-window-{ToKebabCase(status)}");

    private static string ToKebabCase(DockWindowReadinessStatus status)
    {
        var name = status.ToString();
        var chars = new List<char>(name.Length + 8);
        for (var index = 0; index < name.Length; index++)
        {
            var character = name[index];
            if (char.IsUpper(character) && index > 0)
                chars.Add('-');
            chars.Add(char.ToLowerInvariant(character));
        }
        return new string(chars.ToArray());
    }
}
