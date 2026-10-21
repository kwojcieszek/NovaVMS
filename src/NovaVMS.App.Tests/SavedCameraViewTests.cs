using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class SavedCameraViewTests
{
    [Fact]
    public async Task ViewsAppearUnderFavoritesWithoutChangingGroupMembership()
    {
        var (configuration, tree) = await CreateAsync();
        Assert.True(tree.Groups[0].IsFavorites);
        var folder = tree.Groups[1];
        Assert.True(folder.IsViewsFolder);
        var view = Assert.Single(folder.Views);
        Assert.True(view.IsSavedView);
        Assert.Equal(configuration.Current.Views[0].Id, view.Id);
        Assert.Equal(configuration.Current.Views[0].Layout.Cameras.OfType<CameraReference>(), view.Cameras.Select(camera => camera.Reference));
        Assert.Single(tree.Groups.Single(group => group.IsUnassigned).Cameras);
        Assert.Empty(configuration.Current.Groups);
        Assert.Single(folder.Children);
    }

    [Fact]
    public async Task SavedViewsPreserveLayoutEmptySlotsAndMaximizedTileAcrossSerialization()
    {
        var (configuration, _) = await CreateAsync();
        var snapshot = configuration.Current.CreateSnapshot();
        configuration.Current.Views[0].Layout.Cameras.Clear();
        configuration.Current.Views[0].Layout.EmptySlots.Clear();
        configuration.Current.Views[0].Name = "changed";
        using var stream = new MemoryStream();
        var serializer = new JsonConfigurationSerializer();
        await serializer.SerializeAsync(stream, snapshot);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);
        var view = Assert.Single(restored.Views);
        Assert.Equal("Entrance", view.Name);
        Assert.Equal("standard-6", view.Layout.LayoutId);
        Assert.Equal(6, view.Layout.LayoutCount);
        Assert.Null(view.Layout.Cameras[0]);
        Assert.NotNull(view.Layout.Cameras[1]);
        Assert.Contains(0, view.Layout.EmptySlots);
        Assert.Equal(1, view.MaximizedTileIndex);
    }

    [Fact]
    public async Task RenameAndDeleteViewDoNotChangeDevicesOrGroups()
    {
        var (configuration, tree) = await CreateAsync();
        var id = configuration.Current.Views[0].Id;
        await tree.RenameViewCommand.ExecuteAsync(tree.Groups[1].Views[0]);
        Assert.Equal(id, configuration.Current.Views[0].Id);
        Assert.Equal("Renamed view", configuration.Current.Views[0].Name);
        await tree.DeleteViewCommand.ExecuteAsync(tree.Groups[1].Views[0]);
        Assert.Empty(configuration.Current.Views);
        Assert.Empty(tree.Groups[1].Views);
        Assert.Single(configuration.Current.Devices);
        Assert.Empty(configuration.Current.Groups);
    }

    [Fact]
    public async Task SearchingCanFindSavedViewsByCameraName()
    {
        var (_, tree) = await CreateAsync();
        tree.SearchText = "camera";
        Assert.Single(tree.Groups.Single(group => group.IsViewsFolder).Views);
        tree.SearchText = "no match";
        Assert.Empty(tree.Groups.Single(group => group.IsViewsFolder).Views);
    }

    [Fact]
    public async Task RemovingViewCameraPreservesEmptySlotOtherOccurrencesAndDevice()
    {
        var (configuration, tree) = await CreateAsync();
        var view = configuration.Current.Views[0];
        var camera = view.Layout.Cameras[1]!;
        view.Layout.Cameras.Add(camera);
        await configuration.SaveAsync();
        var node = tree.Groups[1].Views[0].Cameras[0];
        Assert.True(node.CanRemoveFromView);
        CameraNodeViewModel? removed = null;
        tree.ViewCameraRemoved += (_, item) => removed = item;

        await tree.RemoveFromViewCommand.ExecuteAsync(node);

        Assert.Same(node, removed);
        Assert.Null(view.Layout.Cameras[1]);
        Assert.Contains(1, view.Layout.EmptySlots);
        Assert.Equal(camera, view.Layout.Cameras[2]);
        Assert.Equal(6, view.Layout.LayoutCount);
        Assert.Single(tree.Groups[1].Views[0].Cameras);
        Assert.NotNull(configuration.FindCamera(camera));
        // A stale menu action must not remove another occurrence.
        await tree.RemoveFromViewCommand.ExecuteAsync(node);
        Assert.Equal(camera, view.Layout.Cameras[2]);
    }

    private static async Task<(ConfigurationService, ResourceTreeViewModel)> CreateAsync()
    {
        var device = new Device { Host = "test.invalid", Channels = [new Channel { Number = 1, Name = "camera" }] };
        var reference = new CameraReference { DeviceId = device.Id, ChannelNumber = 1 };
        var config = new AppConfiguration { Devices = [device], Views = [new SavedCameraView { Name = "Entrance",
            MaximizedTileIndex = 1, Layout = new GroupLiveView { LayoutId = "standard-6", LayoutCount = 6,
                Cameras = [null, reference], EmptySlots = [0] } }] };
        var configuration = new ConfigurationService(new Store(config), new JsonConfigurationSerializer());
        var session = TestSessions.Create(configuration);
        var tree = new ResourceTreeViewModel(configuration, new DeviceSyncService(configuration, null!), new Dialogs(), session);
        await configuration.LoadAsync();
        TestSessions.LogInAsDefaultAdmin(session);
        return (configuration, tree);
    }
    private sealed class Store(AppConfiguration initial) : IConfigurationStore
    {
        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(initial);
        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
    private sealed class Dialogs : IDialogService
    {
        public Task<string?> PromptTextAsync(string title, string message, string initialValue) => Task.FromResult<string?>("Renamed view");
        public Task<bool> ConfirmAsync(string title, string message) => Task.FromResult(true);
        public Task<bool> LoginAsync(UserSessionService session, string userName) => throw new NotSupportedException();
        public Task<Stream?> OpenJsonFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<Stream?> OpenCsvFileAsync(string title) => throw new NotSupportedException();
        public Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<string?> PickFolderAsync(string title) => throw new NotSupportedException();
    }
}
