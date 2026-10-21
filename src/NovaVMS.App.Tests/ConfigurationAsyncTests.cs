using NovaVMS.App.Services;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class ConfigurationAsyncTests
{
    [Fact]
    public async Task BackgroundSavesUseStableSnapshotsAndPreserveOrder()
    {
        var store = new DelayedStore();
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        configuration.Current.Devices.Add(new Device { Name = "first", Host = "primary", SecondaryHost = "secondary",
            Channels = [new Channel { Number = 1, Name = "first camera" }] });
        var camera = new CameraReference { DeviceId = configuration.Current.Devices[0].Id, ChannelNumber = 1 };
        configuration.Current.Groups.Add(new CameraGroup { Name = "first group", Cameras = [camera] });
        configuration.Current.Settings.Theme = ThemeMode.Dark;
        var first = configuration.SaveAsync();
        await store.Entered.Task.WaitAsync(TimeSpan.FromSeconds(3));
        configuration.Current.Devices[0].Name = "second";
        configuration.Current.Devices[0].Channels[0].Name = "second camera";
        configuration.Current.Groups[0].Cameras.Clear();
        configuration.Current.Settings.Theme = ThemeMode.Light;
        var second = configuration.SaveAsync();
        Assert.False(first.IsCompleted);
        Assert.False(second.IsCompleted);
        store.Release.SetResult();
        await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(2, store.Saved.Count);
        Assert.Equal("first", store.Saved[0].Devices[0].Name);
        Assert.Equal("first camera", store.Saved[0].Devices[0].Channels[0].Name);
        Assert.Single(store.Saved[0].Groups[0].Cameras);
        Assert.Equal(ThemeMode.Dark, store.Saved[0].Settings.Theme);
        Assert.Equal("second", store.Saved[1].Devices[0].Name);
        Assert.Equal("second camera", store.Saved[1].Devices[0].Channels[0].Name);
        Assert.Empty(store.Saved[1].Groups[0].Cameras);
        Assert.Equal(ThemeMode.Light, store.Saved[1].Settings.Theme);
    }

    private sealed class DelayedStore : IConfigurationStore
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<AppConfiguration> Saved { get; } = new();
        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppConfiguration());
        public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Release.Task;
            Saved.Add(configuration);
        }
    }
}
