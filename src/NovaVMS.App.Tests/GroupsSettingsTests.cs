using Avalonia.Headless.XUnit;
using LibVLCSharp.Shared;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class GroupsSettingsTests
{
    [AvaloniaFact]
    public async Task OfflineCamerasCanBeAddedAndReorderedWithSelectionAndOrderPreserved()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var device = new Device { Channels = [new Channel { Number = 1 },
            new Channel { Number = 2, Enabled = false, Online = false }, new Channel { Number = 3 }] };
        var store = new MemoryConfigurationStore(new AppConfiguration { Devices = [device] });
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var group = configuration.CreateCustomGroup("Test");
        var preview = new GroupCameraPreviewViewModel(vlc, new FakeDeviceClientFactory(), configuration);
        var settings = new GroupsSettingsViewModel(configuration, preview);
        try
        {
            settings.SelectedGroup = group;
            Assert.Equal(3, settings.AvailableCameras.Count);
            foreach (var camera in settings.AvailableCameras.ToArray())
            {
                settings.SelectedAvailableCamera = camera;
                await settings.AddCameraCommand.ExecuteAsync(null);
            }
            settings.SelectedGroupCamera = settings.GroupCameras[1];
            var selected = settings.SelectedGroupCamera.Reference;
            await settings.MoveCameraUpCommand.ExecuteAsync(null);
            Assert.Equal(new[] { 2, 1, 3 }, settings.GroupCameras.Select(camera => camera.Channel.Number));
            Assert.Equal(selected, settings.SelectedGroupCamera?.Reference);
            Assert.False(settings.MoveCameraUpCommand.CanExecute(null));
            await settings.MoveCameraDownCommand.ExecuteAsync(null);
            await settings.MoveCameraDownCommand.ExecuteAsync(null);
            Assert.Equal(new[] { 1, 3, 2 }, settings.GroupCameras.Select(camera => camera.Channel.Number));
            Assert.False(settings.MoveCameraDownCommand.CanExecute(null));
            await configuration.LoadAsync();
            Assert.Equal(new[] { 1, 3, 2 }, settings.GroupCameras.Select(camera => camera.Channel.Number));
            Assert.Equal(selected, settings.SelectedGroupCamera?.Reference);
        }
        finally { settings.Dispose(); await preview.Disposal; }
    }
}
