using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class MaxFrameRateTests
{
    [Fact]
    public void DefaultsMeanNoLimit()
    {
        var settings = new AppSettings();
        Assert.Equal(0, settings.GetMaxFrameRate(StreamType.Sub));
        Assert.Equal(0, settings.GetMaxFrameRate(StreamType.Main));
    }

    [Fact]
    public async Task SettingsPageSavesSeparateLimitsWithinRange()
    {
        var store = new MemoryConfigurationStore(new AppConfiguration());
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        using (var page = new GeneralSettingsViewModel(configuration, new LocalSettingsService(new MemoryLocalSettingsStore()), null!, null!))
        {
            // Each change is saved and announced before the next, as on the UI thread; the announcement
            // reloads the page and would otherwise race with setting the next value.
            await SetAndWaitAsync(configuration, () => page.SubStreamMaxFrameRate = 10);
            await SetAndWaitAsync(configuration, () => page.MainStreamMaxFrameRate = 500);
        }

        var settings = store.Stored.Settings;
        Assert.Equal(10, settings.GetMaxFrameRate(StreamType.Sub));
        Assert.Equal(60, settings.GetMaxFrameRate(StreamType.Main));
    }

    private static async Task SetAndWaitAsync(ConfigurationService configuration, Action change)
    {
        var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, EventArgs e) => changed.TrySetResult();
        configuration.Changed += OnChanged;
        try
        {
            change();
            await changed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            configuration.Changed -= OnChanged;
        }
    }
}
