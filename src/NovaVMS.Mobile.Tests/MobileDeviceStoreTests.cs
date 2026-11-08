using System.Security.Cryptography;
using System.Text;
using NovaVMS.Core.Models;
using NovaVMS.Mobile.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class MobileDeviceStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-Mobile-Tests", Guid.NewGuid().ToString("N"));
    private readonly TestProtection _protection = new();

    [Fact]
    public async Task RoundTripPreservesCredentialsChannelsAndFavoritesWithoutPlaintextOnDisk()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new MobileDeviceStore(_directory, _protection);
        var device = new Device
        {
            Host = "camera.local",
            Password = "unique-secret-password",
            Protocol = DeviceProtocol.Onvif,
            Channels = [new Channel { Number = 3, Name = "Door", ProfileToken = "profile-3", IsFavorite = true }]
        };

        await store.SaveAsync([device], cancellationToken);

        var loaded = Assert.Single(await store.LoadAsync(cancellationToken));

        Assert.Equal(device.Id, loaded.Id);
        Assert.Equal(device.Password, loaded.Password);
        Assert.Equal("profile-3", Assert.Single(loaded.Channels).ProfileToken);
        Assert.True(loaded.Channels[0].IsFavorite);

        var bytes = await File.ReadAllBytesAsync(Path.Combine(_directory, "devices.enc"), cancellationToken);

        Assert.DoesNotContain(device.Password, Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task TamperedConfigurationIsRejectedInsteadOfResettingDevices()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new MobileDeviceStore(_directory, _protection);

        await store.SaveAsync([new Device { Host = "camera.local" }], cancellationToken);

        var path = Path.Combine(_directory, "devices.enc");
        var bytes = await File.ReadAllBytesAsync(path, cancellationToken);

        bytes[^1] ^= 1;

        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => store.LoadAsync(cancellationToken));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(path, cancellationToken));
    }

    [Fact]
    public async Task FailedProtectionPreservesPreviousConfiguration()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var store = new MobileDeviceStore(_directory, _protection);

        await store.SaveAsync([new Device { Name = "Original" }], cancellationToken);

        _protection.FailWrites = true;

        await Assert.ThrowsAsync<CryptographicException>(() => store.SaveAsync([new Device { Name = "Replacement" }], cancellationToken));

        Assert.Equal("Original", Assert.Single(await store.LoadAsync(cancellationToken)).Name);
    }

    [Theory]
    [InlineData("https://camera.local/path", 80, DeviceProtocol.Isapi)]
    [InlineData("camera.local", 0, DeviceProtocol.Isapi)]
    [InlineData("camera.local", 65536, DeviceProtocol.Onvif)]
    [InlineData("camera.local", 80, DeviceProtocol.HikvisionSdk)]
    public void InvalidConnectionIsRejected(string host, int port, DeviceProtocol protocol)
    {
        var device = new Device
        {
            Host = host,
            HttpPort = port,
            Protocol = protocol
        };

        Assert.NotNull(MobileDevices.Validate(device));
    }

    [Theory]
    [InlineData("192.168.1.20")]
    [InlineData("camera.local")]
    [InlineData("2001:db8::1")]
    public void HostNamesAndIpAddressesAreAccepted(string host)
    {
        Assert.Null(MobileDevices.Validate(new Device { Host = host }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
