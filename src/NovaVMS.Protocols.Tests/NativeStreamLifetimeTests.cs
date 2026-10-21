using NovaVMS.Protocols.Common;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class NativeStreamLifetimeTests
{
    [Fact]
    public async Task ShutdownBeforeRegistrationFinishesDoesNotStartNativeWork()
    {
        var opened = false;
        var cleaned = false;
        using var lifetime = new NativeStreamLifetime(_ =>
        {
            opened = true;
            return Task.FromResult(42);
        }, _ => throw new InvalidOperationException("No handle should be opened"), () => { }, () => cleaned = true,
            startImmediately: false);
        await Task.Run(lifetime.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        lifetime.Start();
        Assert.False(opened);
        Assert.True(cleaned);
    }

    [Fact]
    public async Task DisposalDuringOpeningWaitsForHandleAndNativeStopBeforeReleasingDecoder()
    {
        var opening = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var releaseStop = new ManualResetEventSlim();
        var complete = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var stops = 0;
        var lifetime = new NativeStreamLifetime(_ => { entered.SetResult(); return opening.Task; }, handle =>
        {
            Assert.Equal(42, handle);
            Assert.True(complete.Task.IsCompleted);
            Interlocked.Increment(ref stops);
            stopping.SetResult();
            if (!releaseStop.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        }, () => complete.SetResult(), () => cleaned = true);
        Task? first = null, second = null;
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            first = Task.Run(lifetime.Dispose);
            await complete.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(first.IsCompleted);
            Assert.False(cleaned);
            opening.SetResult(42); // Native open ignored cancellation and returned a valid handle.
            await stopping.Task.WaitAsync(TimeSpan.FromSeconds(5));
            second = Task.Run(lifetime.Dispose);
            Assert.False(first.IsCompleted);
            Assert.False(cleaned);
            releaseStop.Set();
            await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(cleaned);
            Assert.Equal(1, stops);
            lifetime.Dispose();
        }
        finally
        {
            opening.TrySetResult(42);
            releaseStop.Set();
            if (first is not null) await first;
            if (second is not null) await second;
            lifetime.Dispose();
        }
    }

    [Fact]
    public async Task FailedOpenStillReleasesDecoderAndDoesNotStopInvalidHandle()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaned = false;
        var stopped = false;
        var lifetime = new NativeStreamLifetime(_ =>
        {
            entered.SetResult();
            return Task.FromException<int>(new IOException("Open failed"));
        }, _ => stopped = true, () => { }, () => cleaned = true);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAsync<IOException>(() => Task.Run(lifetime.Dispose));
        Assert.True(cleaned);
        Assert.False(stopped);
    }

    [Fact]
    public async Task ShutdownSignalsCancellationAndCleansUpWithoutAHandle()
    {
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var released = false;
        var stopped = false;
        using var lifetime = new NativeStreamLifetime(async token =>
        {
            entered.SetResult(token);
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            return -1;
        }, _ => stopped = true, () => { }, () => released = true);
        var token = await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.Run(lifetime.Dispose).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(token.IsCancellationRequested);
        Assert.True(released);
        Assert.False(stopped);
    }
}
