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
            page.SubStreamMaxFrameRate = 10;
            page.MainStreamMaxFrameRate = 500;
            await configuration.FlushAsync();
        }

        var settings = store.Stored.Settings;
        Assert.Equal(10, settings.GetMaxFrameRate(StreamType.Sub));
        Assert.Equal(60, settings.GetMaxFrameRate(StreamType.Main));
    }
}
