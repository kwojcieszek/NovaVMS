using System.Text;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using NovaVMS.Protocols;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class OptionalProtocolTests
{
    private static Device CreateDevice() => new()
    {
        Host = "primary", Protocol = DeviceProtocol.Isapi, Password = "primary-password",
        Channels = [new Channel { Number = 1, ProfileToken = "primary-profile" }],
        OptionalConnection = new()
        {
            Protocol = DeviceProtocol.HikvisionSdk, Host = "optional", SecondaryHost = "optional-secondary",
            HttpPort = 81, RtspPort = 555, SdkPort = 8001, SecondaryPort = 18001, SecondaryRtspPort = 1555,
            UseHttps = true, Username = "optional-user", Password = "optional-password", StreamEncryptionKey = "optional-key",
            Channels = [new Channel { Number = 1, ProfileToken = "optional-profile" }]
        }
    };

    [Fact]
    public void LocalChoiceUsesEntireIndependentConnectionWithoutChangingPrimary()
    {
        var device = CreateDevice();
        var factory = new DeviceClientFactory(new DeviceReachability(), useOptionalProtocol: true);
        var selected = factory.GetConfiguredConnection(device);
        Assert.True(factory.UsesOptionalProtocol(device));
        Assert.Equal(device.Id, selected.Id);
        Assert.Equal(DeviceProtocol.HikvisionSdk, selected.Protocol);
        Assert.Equal("optional", selected.Host);
        Assert.Equal("optional-secondary", selected.SecondaryHost);
        Assert.Equal(81, selected.HttpPort);
        Assert.Equal(555, selected.RtspPort);
        Assert.Equal(8001, selected.ConnectionPort);
        Assert.Equal(18001, selected.SecondaryPort);
        Assert.Equal(1555, selected.SecondaryRtspPort);
        Assert.True(selected.UseHttps);
        Assert.Equal("optional-user", selected.Username);
        Assert.Equal("optional-password", selected.Password);
        Assert.Equal("optional-key", selected.StreamEncryptionKey);
        Assert.Equal("optional-profile", Assert.Single(selected.Channels).ProfileToken);
        Assert.Equal("primary", device.Host);
        Assert.Equal("primary-password", device.Password);
        Assert.Equal("primary-profile", Assert.Single(device.Channels).ProfileToken);
        Assert.Null(selected.OptionalConnection);
        Assert.Equal("primary", new DeviceClientFactory().GetConfiguredConnection(device).Host);
        device.OptionalConnection = null;
        Assert.Equal("primary", factory.GetConfiguredConnection(device).Host);
        Assert.False(factory.UsesOptionalProtocol(device));
    }

    [Fact]
    public async Task BothConnectionsRoundTripAndSnapshotIsIndependent()
    {
        var device = CreateDevice();
        var configuration = new AppConfiguration { Devices = [device] };
        var copy = configuration.CreateSnapshot();
        copy.Devices[0].OptionalConnection!.Password = "changed";
        copy.Devices[0].OptionalConnection!.Channels[0].ProfileToken = "changed";
        Assert.Equal("optional-password", device.OptionalConnection!.Password);
        Assert.Equal("optional-profile", device.OptionalConnection.Channels[0].ProfileToken);
        using var stream = new MemoryStream();
        var serializer = new JsonConfigurationSerializer();
        await serializer.SerializeAsync(stream, configuration);
        stream.Position = 0;
        var restored = Assert.Single((await serializer.DeserializeAsync(stream)).Devices);
        Assert.Equal("primary-password", restored.Password);
        Assert.Equal("optional-password", restored.OptionalConnection!.Password);
        Assert.Equal(8001, restored.OptionalConnection.SdkPort);
        Assert.Equal("optional-profile", restored.OptionalConnection.Channels[0].ProfileToken);
        using var legacy = new MemoryStream(Encoding.UTF8.GetBytes("{\"Devices\":[{\"Host\":\"primary\"}]}"));
        Assert.Null(Assert.Single((await serializer.DeserializeAsync(legacy)).Devices).OptionalConnection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExportRespectsPasswordChoiceForBothConnections(bool includePasswords)
    {
        var device = CreateDevice();
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()), new JsonConfigurationSerializer());
        configuration.Current.Devices.Add(device);
        using var stream = new MemoryStream();
        await configuration.ExportAsync(stream, includePasswords);
        stream.Position = 0;
        var exported = Assert.Single((await new JsonConfigurationSerializer().DeserializeAsync(stream)).Devices);
        Assert.Equal(includePasswords ? "primary-password" : "", exported.Password);
        Assert.Equal(includePasswords ? "optional-password" : "", exported.OptionalConnection!.Password);
        Assert.Equal(includePasswords ? "optional-key" : "", exported.OptionalConnection.StreamEncryptionKey);
        Assert.Equal("optional-password", device.OptionalConnection!.Password);
    }
}
