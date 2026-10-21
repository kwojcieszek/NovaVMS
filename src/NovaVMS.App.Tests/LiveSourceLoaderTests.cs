using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class LiveSourceLoaderTests
{
    [Fact]
    public async Task BlockedTileDoesNotBlockAnotherTileOrCaller()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var slowDevice = new Device();
        var factory = new BlockingFactory(slowDevice.Id, entered, release);
        var slow = LiveSourceLoader.OpenAsync(factory, slowDevice, 1, StreamType.Sub, CancellationToken.None);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.False(slow.IsCompleted);
            var fast = LiveSourceLoader.OpenAsync(factory, new Device(), 1, StreamType.Sub, CancellationToken.None);
            await Assert.ThrowsAsync<ProbeException>(() => fast.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(slow.IsCompleted);
        }
        finally
        {
            release.Set();
            await Assert.ThrowsAsync<ProbeException>(() => slow.WaitAsync(TimeSpan.FromSeconds(5)));
        }
    }

    [Fact]
    public async Task CancelledOpeningDoesNotCreateClient()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var device = new Device();
        var factory = new BlockingFactory(device.Id, entered, release);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            LiveSourceLoader.OpenAsync(factory, device, 1, StreamType.Sub, cancellation.Token));
        Assert.False(entered.Task.IsCompleted);
    }

    private sealed class ProbeException : Exception { }

    // Uses the interface's synchronous CreateAsync fallback, the case that previously
    // could keep the UI loop from dispatching the next tile's opening.
    private sealed class BlockingFactory(Guid slowId, TaskCompletionSource entered,
        ManualResetEventSlim release) : IDeviceClientFactory
    {
        public IDeviceClient Create(Device device)
        {
            if (device.Id == slowId)
            {
                entered.TrySetResult();
                if (!release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            }
            throw new ProbeException();
        }
    }
}
