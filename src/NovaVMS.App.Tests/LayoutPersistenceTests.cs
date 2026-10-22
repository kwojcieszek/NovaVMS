using Avalonia.Headless.XUnit;
using LibVLCSharp.Shared;
using NovaVMS.App.Models;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class LayoutPersistenceTests
{
    [AvaloniaFact]
    public async Task ManualLayoutChoiceIsSavedAndRestoredWithItsExactVariant()
    {
        using var vlc = new LibVLC("--vout=dummy", "--aout=dummy", "--avcodec-hw=none");
        var store = new MemoryConfigurationStore(new());
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        var live = Create(vlc, configuration);
        try
        {
            foreach (var id in new[] { "standard-16", "wide-16" })
            {
                var saved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                void OnSaved(object? sender, EventArgs args) => saved.TrySetResult();
                configuration.Saved += OnSaved;
                try
                {
                    live.SelectedLayout = GridLayoutOption.Find(id);
                    await saved.Task.WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally { configuration.Saved -= OnSaved; }
                var restored = new ConfigurationService(store, new JsonConfigurationSerializer());
                await restored.LoadAsync();
                Assert.Equal(id, restored.Current.Settings.DefaultLayoutId);
                Assert.Equal(16, restored.Current.Settings.DefaultLayoutCount);
                var next = Create(vlc, restored);
                try
                {
                    next.ApplyDefaultLayout();
                    Assert.Equal(id, next.SelectedLayout.Id);
                    Assert.Equal(16, next.Tiles.Count);
                }
                finally { next.Dispose(); await next.Disposal; }
            }

            configuration.Current.GroupLiveViews["group"] = new() { LayoutId = "standard-9", LayoutCount = 9 };
            await live.ShowGroupAsync("group", Array.Empty<CameraReference>());
            Assert.Equal("standard-9", live.SelectedLayout.Id);
            Assert.Equal("wide-16", configuration.Current.Settings.DefaultLayoutId);
            var view = new SavedCameraView { Layout = new() { LayoutId = "standard-4", LayoutCount = 4 } };
            configuration.Current.Views.Add(view);
            await live.ShowSavedViewAsync(view.Id);
            Assert.Equal("standard-4", live.SelectedLayout.Id);
            Assert.Equal("wide-16", configuration.Current.Settings.DefaultLayoutId);
        }
        finally { live.Dispose(); await live.Disposal; }
    }

    private static LiveViewViewModel Create(LibVLC vlc, ConfigurationService configuration) => new(vlc, configuration,
        new FakeDeviceClientFactory(), new StatusBarService(), null!, TestSessions.Create(configuration));
}
