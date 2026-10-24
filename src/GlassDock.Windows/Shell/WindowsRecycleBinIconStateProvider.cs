using System.Runtime.InteropServices;
using System.Diagnostics;
using GlassDock.App;

namespace GlassDock.Windows.Shell;

public sealed record WindowsRecycleBinStatusQueryResult(
    bool Succeeded,
    long ItemCount,
    int HResult = 0,
    double ApiElapsedMilliseconds = 0);

public sealed record WindowsRecycleBinQueryDiagnostic(
    string Event,
    double WaitElapsedMilliseconds,
    double? ApiElapsedMilliseconds,
    bool NativeCallStillRunning,
    string? Result,
    int HResult = 0);

public interface IWindowsRecycleBinStatusQuery
{
    WindowsRecycleBinStatusQueryResult QueryAllDrives();
}

/// <summary>Queries Windows Shell's actual Recycle Bin state without changing its contents.</summary>
public sealed class WindowsRecycleBinStatusQuery : IWindowsRecycleBinStatusQuery
{
    public WindowsRecycleBinStatusQueryResult QueryAllDrives()
    {
        var timer = Stopwatch.StartNew();
        try
        {
            var info = new NativeMethods.ShQueryRecycleBinInfo
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.ShQueryRecycleBinInfo>()
            };
            var result = NativeMethods.SHQueryRecycleBinW(string.Empty, ref info);
            return new WindowsRecycleBinStatusQueryResult(
                result >= 0,
                checked((long)info.ItemCount),
                result,
                timer.Elapsed.TotalMilliseconds);
        }
        catch (DllNotFoundException)
        {
            return Failed(unchecked((int)0x8007007E), timer);
        }
        catch (EntryPointNotFoundException)
        {
            return Failed(unchecked((int)0x8007007F), timer);
        }
        catch (OverflowException)
        {
            return Failed(unchecked((int)0x80070216), timer, -1);
        }
    }

    private static WindowsRecycleBinStatusQueryResult Failed(int hresult, Stopwatch timer, long itemCount = 0) =>
        new(false, itemCount, hresult, timer.Elapsed.TotalMilliseconds);

    private static class NativeMethods
    {
        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        public struct ShQueryRecycleBinInfo
        {
            public uint Size;
            public ulong Bytes;
            public ulong ItemCount;
        }

        [DllImport("shell32.dll", EntryPoint = "SHQueryRecycleBinW", CharSet = CharSet.Unicode, PreserveSig = true)]
        public static extern int SHQueryRecycleBinW(
            [MarshalAs(UnmanagedType.LPWStr)] string rootPath,
            ref ShQueryRecycleBinInfo info);
    }
}

/// <summary>Bridges the Shell query into icon variant selection. Unknown query results never pretend to be empty.</summary>
public sealed class WindowsDockIconVisualStateProvider : IDockIconVisualStateProvider
{
    public static readonly TimeSpan DefaultWaitBudget = TimeSpan.FromSeconds(1);

    private readonly IWindowsRecycleBinStatusQuery _query;
    private readonly TimeSpan _waitBudget;
    private readonly Action<WindowsRecycleBinQueryDiagnostic>? _diagnostics;
    private readonly object _queryLock = new();
    private Task<WindowsRecycleBinStatusQueryResult>? _queryInFlight;

    public WindowsDockIconVisualStateProvider(
        IWindowsRecycleBinStatusQuery? query = null,
        TimeSpan? waitBudget = null,
        Action<WindowsRecycleBinQueryDiagnostic>? diagnostics = null)
    {
        _query = query ?? new WindowsRecycleBinStatusQuery();
        _waitBudget = waitBudget ?? DefaultWaitBudget;
        if (_waitBudget <= TimeSpan.Zero || _waitBudget > TimeSpan.FromSeconds(30))
            throw new ArgumentOutOfRangeException(nameof(waitBudget));
        _diagnostics = diagnostics;
    }

    public ValueTask<DockIconVisualState> GetVisualStateAsync(
        DockIconRole role,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(role))
            throw new ArgumentOutOfRangeException(nameof(role));
        if (role != DockIconRole.Trash)
            return ValueTask.FromResult(DockIconVisualState.Default);

        return new ValueTask<DockIconVisualState>(GetTrashVisualStateAsync(cancellationToken));
    }

    private async Task<DockIconVisualState> GetTrashVisualStateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<WindowsRecycleBinStatusQueryResult> queryTask;
        var startedQuery = false;
        lock (_queryLock)
        {
            if (_queryInFlight is null || _queryInFlight.IsCompleted)
            {
                // SHQueryRecycleBinW is synchronous and cannot be cancelled once entered. Run one
                // shared call without passing the caller token; callers only cancel their wait.
                _queryInFlight = Task.Run(() =>
                {
                    try
                    {
                        return _query.QueryAllDrives();
                    }
                    catch (Exception exception)
                    {
                        return new WindowsRecycleBinStatusQueryResult(
                            false,
                            -1,
                            exception.HResult);
                    }
                });
                var nativeTimer = Stopwatch.StartNew();
                var nativeTask = _queryInFlight;
                _ = nativeTask.ContinueWith(
                    completed =>
                    {
                        if (completed.Status == TaskStatus.RanToCompletion)
                        {
                            var result = completed.Result;
                            var state = !result.Succeeded || result.ItemCount < 0
                                ? DockIconVisualState.Unknown
                                : result.ItemCount == 0 ? DockIconVisualState.Empty : DockIconVisualState.Full;
                            Report(new WindowsRecycleBinQueryDiagnostic(
                                "recycle-bin-native-query-completed",
                                nativeTimer.Elapsed.TotalMilliseconds,
                                result.ApiElapsedMilliseconds,
                                NativeCallStillRunning: false,
                                Result: state.ToString(),
                                HResult: result.HResult));
                        }
                        else
                        {
                            Report(new WindowsRecycleBinQueryDiagnostic(
                                "recycle-bin-native-query-failed",
                                nativeTimer.Elapsed.TotalMilliseconds,
                                ApiElapsedMilliseconds: null,
                                NativeCallStillRunning: false,
                                Result: DockIconVisualState.Unknown.ToString()));
                        }
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
                startedQuery = true;
            }
            queryTask = _queryInFlight;
        }

        var waitTimer = Stopwatch.StartNew();
        Report(new WindowsRecycleBinQueryDiagnostic(
            startedQuery ? "recycle-bin-query-started" : "recycle-bin-query-reused",
            0,
            null,
            !queryTask.IsCompleted,
            null));

        try
        {
            var result = await queryTask.WaitAsync(_waitBudget, cancellationToken).ConfigureAwait(false);
            var state = !result.Succeeded || result.ItemCount < 0
                ? DockIconVisualState.Unknown
                : result.ItemCount == 0 ? DockIconVisualState.Empty : DockIconVisualState.Full;
            Report(new WindowsRecycleBinQueryDiagnostic(
                "recycle-bin-query-wait-completed",
                waitTimer.Elapsed.TotalMilliseconds,
                result.ApiElapsedMilliseconds,
                NativeCallStillRunning: false,
                Result: state.ToString(),
                HResult: result.HResult));
            return state;
        }
        catch (TimeoutException)
        {
            Report(new WindowsRecycleBinQueryDiagnostic(
                "recycle-bin-query-timeout",
                waitTimer.Elapsed.TotalMilliseconds,
                ApiElapsedMilliseconds: null,
                NativeCallStillRunning: !queryTask.IsCompleted,
                Result: DockIconVisualState.Unknown.ToString()));
            return DockIconVisualState.Unknown;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Report(new WindowsRecycleBinQueryDiagnostic(
                "recycle-bin-wait-cancelled",
                waitTimer.Elapsed.TotalMilliseconds,
                ApiElapsedMilliseconds: null,
                NativeCallStillRunning: !queryTask.IsCompleted,
                Result: null));
            throw;
        }
        catch (Exception)
        {
            Report(new WindowsRecycleBinQueryDiagnostic(
                "recycle-bin-query-failed",
                waitTimer.Elapsed.TotalMilliseconds,
                ApiElapsedMilliseconds: null,
                NativeCallStillRunning: !queryTask.IsCompleted,
                Result: DockIconVisualState.Unknown.ToString()));
            return DockIconVisualState.Unknown;
        }
        finally
        {
            if (queryTask.IsCompleted)
            {
                lock (_queryLock)
                {
                    if (ReferenceEquals(_queryInFlight, queryTask))
                        _queryInFlight = null;
                }
            }
        }
    }

    private void Report(WindowsRecycleBinQueryDiagnostic diagnostic)
    {
        try
        {
            _diagnostics?.Invoke(diagnostic);
        }
        catch
        {
            // Diagnostics must not change the Shell state selected for the Dock icon.
        }
    }
}
