using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class FavoriteCameraTests
{
    [Fact]
    public async Task FavoritesAreFirstEvenWhenEmptyAndCannotBeEditedAsAGroup()
    {
        var (_, tree, _) = await CreateAsync();
        var favorites = tree.Groups[0];
        Assert.True(favorites.IsFavorites);
        Assert.False(favorites.IsExpanded);
        Assert.Empty(favorites.Cameras);
    }

    [Fact]
    public async Task TogglingFromEitherLocationUpdatesAllCopiesAndPersists()
    {
        var (_, tree, store) = await CreateAsync();
        var original = tree.Groups.Single(group => group.IsCustom).Cameras[0];
        await tree.ToggleFavoriteCommand.ExecuteAsync(original);
        var favorites = tree.Groups[0];
        Assert.Equal(original.Reference, Assert.Single(favorites.Cameras).Reference);
        Assert.True(tree.Groups.Single(group => group.IsCustom).Cameras[0].IsFavorite);
        Assert.True((await store.LoadAsync()).Devices[0].Channels[0].IsFavorite);

        await tree.ToggleFavoriteCommand.ExecuteAsync(favorites.Cameras[0]);
        Assert.Empty(tree.Groups[0].Cameras);
        Assert.False(tree.Groups.Single(group => group.IsCustom).Cameras[0].IsFavorite);
        Assert.False((await store.LoadAsync()).Devices[0].Channels[0].IsFavorite);
    }

    [Fact]
    public async Task CamerasWithoutAGroupRemainAvailableInSettingsAndFavorites()
    {
        var (configuration, tree, _) = await CreateAsync();
        var descriptor = configuration.GetAllCameras().Single(camera => camera.Channel.Number == 2);
        Assert.DoesNotContain(tree.Groups.SelectMany(group => group.Cameras), camera => camera.Reference == descriptor.Reference);
        var camera = new CameraNodeViewModel(tree, descriptor.Reference, descriptor.DisplayName, descriptor.FullName, null);
        tree.Groups[0].IsExpanded = false;
        await tree.ToggleFavoriteCommand.ExecuteAsync(camera);
        Assert.False(tree.Groups[0].IsExpanded);
        Assert.NotNull(configuration.FindCamera(camera.Reference));
        Assert.Single(tree.GetCameras(tree.Groups[0]));

        tree.SearchText = "Office";
        Assert.Single(tree.Groups[0].Cameras);
        tree.SearchText = string.Empty;
        Assert.False(tree.Groups[0].IsExpanded);
        configuration.RemoveDevice(camera.Reference.DeviceId);
        await configuration.SaveAsync();
        Assert.Empty(tree.Groups[0].Cameras);
    }

    [Fact]
    public async Task DeviceSynchronizationPreservesFavoritesByChannelNumber()
    {
        var (configuration, tree, _) = await CreateAsync();
        var camera = tree.Groups.Single(group => group.IsCustom).Cameras[0];
        await tree.ToggleFavoriteCommand.ExecuteAsync(camera);
        var sync = new DeviceSyncService(configuration, new FakeClientFactory());
        var device = configuration.Current.Devices[0];
        Assert.True((await sync.TrySyncAsync(device)).Success);
        await configuration.SaveAsync();
        Assert.True(device.Channels.Single(channel => channel.Number == 1).IsFavorite);
        Assert.False(device.Channels.Single(channel => channel.Number == 2).IsFavorite);
        Assert.Equal(camera.Reference, Assert.Single(tree.Groups[0].Cameras).Reference);
    }

    private static async Task<(ConfigurationService, ResourceTreeViewModel, MemoryStore)> CreateAsync()
    {
        var device = new Device
        {
            Name = "Recorder", Host = "test.invalid",
            Channels = [new Channel { Number = 1, Name = "Entrance" }, new Channel { Number = 2, Name = "Office" }]
        };
        var store = new MemoryStore(new AppConfiguration
        {
            Devices = [device],
            Groups = [new CameraGroup
            {
                Name = "Custom group",
                Cameras = [new CameraReference { DeviceId = device.Id, ChannelNumber = 1 }]
            }]
        });
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        var sync = new DeviceSyncService(configuration, new FakeClientFactory());
        var session = TestSessions.Create(configuration);
        var tree = new ResourceTreeViewModel(configuration, sync, null!, session);
        await configuration.LoadAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        return (configuration, tree, store);
    }

    private sealed class MemoryStore(AppConfiguration initial) : IConfigurationStore
    {
        private readonly JsonConfigurationSerializer _serializer = new();
        private byte[]? _saved;
        public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
        {
            if (_saved is null) return initial;
            using var stream = new MemoryStream(_saved);
            return await _serializer.DeserializeAsync(stream, cancellationToken);
        }
        public async Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            using var stream = new MemoryStream();
            await _serializer.SerializeAsync(stream, configuration, cancellationToken);
            _saved = stream.ToArray();
        }
    }

    private sealed class FakeClientFactory : IDeviceClientFactory
    {
        public IDeviceClient Create(Device device) => new FakeClient();
    }

    private sealed class FakeClient : IDeviceClient
    {
        public Task<DeviceInfo> GetDeviceInfoAsync(CancellationToken cancellationToken = default) => Task.FromResult(new DeviceInfo());
        public Task<IReadOnlyList<Channel>> GetChannelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Channel>>([new Channel { Number = 2, Name = "Office" }, new Channel { Number = 1, Name = "Entrance" }]);
        public Task<byte[]> GetSnapshotAsync(int channelNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public VideoSource GetLiveSource(int channelNumber, StreamType streamType) => throw new NotSupportedException();
        public Task PtzMoveAsync(int channelNumber, int pan, int tilt, int zoom, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task PtzStopAsync(int channelNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<PtzPreset>> GetPtzPresetsAsync(int channelNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task GotoPtzPresetAsync(int channelNumber, PtzPreset preset, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RecordingSegment>> SearchRecordingsAsync(int channelNumber, DateTime from, DateTime to, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public VideoSource GetPlaybackSource(int channelNumber, DateTime start, DateTime end) => throw new NotSupportedException();
        public void Dispose() { }
    }
}
