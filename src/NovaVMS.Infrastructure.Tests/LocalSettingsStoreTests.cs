using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class LocalSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-local-settings-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "local.json");

    [Fact]
    public void MissingFileGivesDefaults()
    {
        Assert.False(new LocalSettingsStore(FilePath).Load().StartFullScreen);
    }

    [Fact]
    public async Task SavedSettingsRoundTrip()
    {
        await new LocalSettingsStore(FilePath).SaveAsync(new LocalSettings { StartFullScreen = true });

        Assert.True(new LocalSettingsStore(FilePath).Load().StartFullScreen);
    }

    [Fact]
    public void DamagedFileGivesDefaults()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(FilePath, "{ not json");

        Assert.False(new LocalSettingsStore(FilePath).Load().StartFullScreen);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
