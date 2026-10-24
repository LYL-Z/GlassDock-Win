using GlassDock.App;

namespace GlassDock.App.Tests;

[TestClass]
public sealed class DockIconLoadBatchTests
{
    [TestMethod]
    public async Task SlowIconDoesNotBlockFastIconFromPreparingAlongsideIt()
    {
        using var slowStarted = new ManualResetEventSlim();
        using var releaseSlow = new ManualResetEventSlim();
        using var fastFinished = new ManualResetEventSlim();
        var batch = DockIconLoadBatch.RunAsync(
            ["slow-trash", "fast-finder"],
            maxConcurrency: 2,
            async (name, cancellationToken) =>
            {
                if (name == "slow-trash")
                {
                    slowStarted.Set();
                    await Task.Run(() => releaseSlow.Wait(cancellationToken), cancellationToken);
                    return;
                }
                fastFinished.Set();
            });

        try
        {
            Assert.IsTrue(slowStarted.Wait(TimeSpan.FromSeconds(1)));
            Assert.IsTrue(fastFinished.Wait(TimeSpan.FromSeconds(1)),
                "The Shell-bound slow item must not hold up other icon work or first-frame UI preparation.");
            Assert.IsFalse(batch.IsCompleted, "The batch still records the slow item until it really completes.");
        }
        finally
        {
            releaseSlow.Set();
        }

        await batch;
    }

    [TestMethod]
    public async Task IconBatchStopsQueuedWorkWhenCancelledAndDoesNotExceedConcurrencyLimit()
    {
        using var cancellation = new CancellationTokenSource();
        using var release = new ManualResetEventSlim();
        var active = 0;
        var maximumActive = 0;
        var started = 0;
        var batch = DockIconLoadBatch.RunAsync(
            Enumerable.Range(0, 8).ToArray(),
            maxConcurrency: 2,
            async (_, cancellationToken) =>
            {
                var current = Interlocked.Increment(ref active);
                InterlockedExtensionsMax(ref maximumActive, current);
                Interlocked.Increment(ref started);
                try
                {
                    await Task.Run(() => release.Wait(cancellationToken), cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                }
            },
            cancellation.Token);

        Assert.IsTrue(SpinWait.SpinUntil(() => Volatile.Read(ref started) == 2, TimeSpan.FromSeconds(1)));
        cancellation.Cancel();
        release.Set();
        var cancellationPropagated = false;
        try
        {
            await batch;
        }
        catch (OperationCanceledException)
        {
            cancellationPropagated = true;
        }
        Assert.IsTrue(cancellationPropagated, "Cancelling the batch must propagate an OperationCanceledException.");

        Assert.AreEqual(2, maximumActive);
        Assert.IsTrue(started <= 2, "Queued icon work must not start after cancellation.");
    }

    private static void InterlockedExtensionsMax(ref int location, int value)
    {
        var observed = Volatile.Read(ref location);
        while (value > observed)
        {
            var prior = Interlocked.CompareExchange(ref location, value, observed);
            if (prior == observed)
                return;
            observed = prior;
        }
    }
}
