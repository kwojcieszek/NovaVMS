using NovaVMS.App.Services;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class AsyncWorkQueueTests
{
    [Fact]
    public async Task BlockingNativeOperationDoesNotBlockCallerAndCleanupWaitsInOrder()
    {
        var queue = new AsyncWorkQueue();
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var order = new List<string>();
        var stop = queue.RunAsync(() =>
        {
            order.Add("stop");
            entered.SetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            order.Add("stopped");
        });
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
            var play = queue.RunAsync(() => { order.Add("play"); });
            var dispose = queue.RunAsync(() => { order.Add("dispose"); });
            Assert.False(stop.IsCompleted);
            Assert.False(play.IsCompleted);
            Assert.False(dispose.IsCompleted);
            release.Set();
            await Task.WhenAll(stop, play, dispose).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.Equal(new[] { "stop", "stopped", "play", "dispose" }, order);
        }
        finally { release.Set(); }
    }

    [Fact]
    public async Task FailedStartStillAllowsStopAndDispose()
    {
        var queue = new AsyncWorkQueue();
        var stopped = false;
        var disposed = false;
        var failed = queue.RunAsync((Action)(() => throw new IOException("Native open failed")));
        var stop = queue.RunAsync(() => { stopped = true; });
        var dispose = queue.RunAsync(() => { Assert.True(stopped); disposed = true; });
        await Assert.ThrowsAsync<IOException>(() => failed);
        await Task.WhenAll(stop, dispose).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(disposed);
    }

    [Fact]
    public async Task AsyncOperationKeepsItsPlaceUntilItsAwaitedWorkFinishes()
    {
        var queue = new AsyncWorkQueue();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstFinished = false;
        var secondRan = false;
        var first = queue.RunAsync(async () => { await release.Task; firstFinished = true; });
        var second = queue.RunAsync(() => { Assert.True(firstFinished); secondRan = true; });
        Assert.False(secondRan);
        Assert.False(queue.WhenIdle.IsCompleted);
        release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(secondRan);
    }

    [Fact]
    public async Task NativeWorkDoesNotRunOnOrCaptureTheCallerSynchronizationContext()
    {
        var queue = new AsyncWorkQueue();
        var original = SynchronizationContext.Current;
        Task<SynchronizationContext?> work;
        try
        {
            SynchronizationContext.SetSynchronizationContext(new RejectingContext());
            work = queue.RunAsync(() => SynchronizationContext.Current);
        }
        finally { SynchronizationContext.SetSynchronizationContext(original); }
        Assert.Null(await work.WaitAsync(TimeSpan.FromSeconds(3)));
    }

    private sealed class RejectingContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback callback, object? state) => throw new InvalidOperationException("Captured UI context");
    }
}
