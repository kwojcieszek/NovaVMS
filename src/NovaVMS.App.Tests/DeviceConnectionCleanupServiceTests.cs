using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class DeviceConnectionCleanupServiceTests
{
    private readonly Device _first = new() { Name = "First", Host = "10.0.0.1" };
    private readonly Device _second = new() { Name = "Second", Host = "10.0.0.2" };
    private readonly FakeDeviceClientFactory _factory = new();
    private readonly ConfigurationService _configuration;

    public DeviceConnectionCleanupServiceTests()
    {
        _configuration = new ConfigurationService(
            new MemoryConfigurationStore(new AppConfiguration { Devices = [_first, _second] }), new JsonConfigurationSerializer());
    }

    [Fact]
    public async Task ReleasesConnectionsOnlyWhenDevicesOrTheirConnectionsChange()
    {
        using var cleanup = new DeviceConnectionCleanupService(_configuration, _factory);
        await _configuration.LoadAsync();
        await WaitForReleaseAsync();

        // Settings-only saves do not touch device connections.
        _configuration.Current.Settings.ShowTileTitles = false;
        await _configuration.SaveAsync();
        Assert.Single(_factory.Releases);

        _factory.ResetReleaseSignal();
        _configuration.RemoveDevice(_second.Id);
        await _configuration.SaveAsync();
        await WaitForReleaseAsync();
        Assert.Equal([_first.Id], _factory.Releases[^1].Select(device => device.Id));

        _factory.ResetReleaseSignal();
        _configuration.FindDevice(_first.Id)!.Password = "changed";
        await _configuration.SaveAsync();
        await WaitForReleaseAsync();
        Assert.Equal("changed", Assert.Single(_factory.Releases[^1]).Password);
        Assert.Equal(3, _factory.Releases.Count);
    }

    private async Task WaitForReleaseAsync()
    {
        await _factory.ReleaseCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ShutdownWaitsForQueuedConnectionCleanup()
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _factory.BeforeRelease = () =>
        {
            entered.TrySetResult();
            if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
        };
        using var cleanup = new DeviceConnectionCleanupService(_configuration, _factory);
        try
        {
            await _configuration.LoadAsync();
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cleanup.Dispose();
            Assert.False(cleanup.Disposal.IsCompleted);
            release.Set();
            await cleanup.Disposal.WaitAsync(TimeSpan.FromSeconds(5));
            await _configuration.SaveAsync();
            Assert.Single(_factory.Releases);
        }
        finally { release.Set(); await cleanup.Disposal; }
    }
}
