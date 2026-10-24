namespace GlassDock.App;

/// <summary>Runs independent Dock icon loads progressively with a fixed concurrency ceiling.</summary>
public static class DockIconLoadBatch
{
    public static async Task RunAsync<T>(
        IReadOnlyCollection<T> items,
        int maxConcurrency,
        Func<T, CancellationToken, Task> loadAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(loadAsync);
        if (maxConcurrency is < 1 or > 16)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency));
        if (items.Count == 0)
            return;

        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = items.Select(async item =>
        {
            // Preserve the caller's synchronization context so WPF adapters resume on the
            // dispatcher before applying an image to UI elements.
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                await loadAsync(item, cancellationToken);
            }
            finally
            {
                semaphore.Release();
            }
        });
        await Task.WhenAll(tasks);
    }
}
