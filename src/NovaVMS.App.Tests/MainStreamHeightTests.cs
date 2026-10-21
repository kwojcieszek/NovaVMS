using System.Text;
using NovaVMS.App.Services;
using NovaVMS.App.ViewModels;
using NovaVMS.Core.Abstractions;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class MainStreamHeightTests
{
    [Theory]
    [InlineData(539, false, StreamType.Sub)]
    [InlineData(539, true, StreamType.Sub)]
    [InlineData(540, false, StreamType.Main)]
    [InlineData(540, true, StreamType.Main)]
    [InlineData(900, false, StreamType.Main)]
    [InlineData(0, false, StreamType.Sub)]
    [InlineData(0, true, StreamType.Main)]
    public void AutomaticModeUsesActualVideoAreaHeight(double height, bool single, StreamType expected)
    {
        var settings = new AppSettings { MainStreamMinimumHeight = 540 };
        Assert.Equal(expected, settings.GetLiveStreamType(height, single));
    }

    [Theory]
    [InlineData(GridStreamMode.Main, 100, StreamType.Main)]
    [InlineData(GridStreamMode.Sub, 1080, StreamType.Sub)]
    public void ManualStreamModeOverridesHeight(GridStreamMode mode, double height, StreamType expected)
    {
        var settings = new AppSettings { GridStreamMode = mode };
        Assert.Equal(expected, settings.GetLiveStreamType(height, true));
    }

    [Fact]
    public void ChangingThresholdChangesDecisionForSameVideoSize()
    {
        var settings = new AppSettings { MainStreamMinimumHeight = 720 };
        Assert.Equal(StreamType.Sub, settings.GetLiveStreamType(600, false));
        settings.MainStreamMinimumHeight = 600;
        Assert.Equal(StreamType.Main, settings.GetLiveStreamType(600, false));
        Assert.Equal(StreamType.Main, settings.GetLiveStreamType(400 * 1.5, false));
    }

    [Fact]
    public async Task SettingsControlSavesAndRestoresHeight()
    {
        var store = new MemoryStore();
        var configuration = new ConfigurationService(store, new JsonConfigurationSerializer());
        using (var settings = new GeneralSettingsViewModel(configuration, null!))
        {
            Assert.Equal(540m, settings.MainStreamMinimumHeight);
            settings.MainStreamMinimumHeight = 720;
            await configuration.FlushAsync();
            Assert.Equal(720, store.Saved!.Settings.MainStreamMinimumHeight);
        }
        var serializer = new JsonConfigurationSerializer();
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, store.Saved!);
        stream.Position = 0;
        Assert.Equal(720, (await serializer.DeserializeAsync(stream)).Settings.MainStreamMinimumHeight);
    }

    [Fact]
    public async Task OldConfigurationGetsDefaultHeight()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Settings\":{}}"));
        Assert.Equal(540, (await new JsonConfigurationSerializer().DeserializeAsync(stream)).Settings.MainStreamMinimumHeight);
    }

    private sealed class MemoryStore : IConfigurationStore
    {
        public AppConfiguration? Saved { get; private set; }
        public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult(new AppConfiguration());
        public Task SaveAsync(AppConfiguration configuration, CancellationToken cancellationToken = default)
        {
            Saved = configuration;
            return Task.CompletedTask;
        }
    }
}
