using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class DeviceAddressSerializationTests
{
    [Fact]
    public async Task SavesBothAddressesWithoutOverwritingPrimary()
    {
        var serializer = new JsonConfigurationSerializer();
        var configuration = new AppConfiguration { Devices = [new Device { Host = "local", SecondaryHost = "remote" }] };
        using var stream = new MemoryStream();
        await serializer.SerializeAsync(stream, configuration);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);
        var device = Assert.Single(restored.Devices);
        Assert.Equal("local", device.Host);
        Assert.Equal("remote", device.SecondaryHost);
    }

    [Fact]
    public async Task OlderConfigurationsHaveNoSecondaryAddress()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"Devices\":[{\"Host\":\"local\"}]}"));
        var configuration = await new JsonConfigurationSerializer().DeserializeAsync(stream);
        Assert.Empty(Assert.Single(configuration.Devices).SecondaryHost);
    }
}
