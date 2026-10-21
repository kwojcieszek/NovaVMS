using System.Security.Cryptography;
using System.Text;
using NovaVMS.Infrastructure.Sync;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class SettingsPayloadCipherTests
{
    private static readonly byte[] Salt = RandomNumberGenerator.GetBytes(32);

    [Fact]
    public void EncryptedPayloadRoundTripsAndHidesPlaintext()
    {
        using var cipher = new SettingsPayloadCipher("shared-password", Salt, SettingsPayloadCipher.MinimumIterations);
        var plaintext = Encoding.UTF8.GetBytes("{\"Password\":\"unique-password-marker\"}");

        var encrypted = cipher.Encrypt(plaintext);

        Assert.DoesNotContain("unique-password-marker", Encoding.UTF8.GetString(encrypted));
        Assert.Equal(plaintext, cipher.Decrypt(encrypted));
    }

    [Fact]
    public void AnotherPasswordCannotDecrypt()
    {
        using var cipher = new SettingsPayloadCipher("shared-password", Salt, SettingsPayloadCipher.MinimumIterations);
        using var other = new SettingsPayloadCipher("other-password", Salt, SettingsPayloadCipher.MinimumIterations);

        var encrypted = cipher.Encrypt("settings"u8);

        Assert.Null(other.Decrypt(encrypted));
    }

    [Fact]
    public void ModifiedPayloadIsRejected()
    {
        using var cipher = new SettingsPayloadCipher("shared-password", Salt, SettingsPayloadCipher.MinimumIterations);
        var encrypted = cipher.Encrypt("settings"u8);
        encrypted[^1] ^= 0x01;

        Assert.Null(cipher.Decrypt(encrypted));
    }

    [Fact]
    public void HashDependsOnPasswordAndContent()
    {
        using var cipher = new SettingsPayloadCipher("shared-password", Salt, SettingsPayloadCipher.MinimumIterations);
        using var other = new SettingsPayloadCipher("other-password", Salt, SettingsPayloadCipher.MinimumIterations);

        Assert.Equal(cipher.ComputeHash("a"u8), cipher.ComputeHash("a"u8));
        Assert.NotEqual(cipher.ComputeHash("a"u8), cipher.ComputeHash("b"u8));
        Assert.NotEqual(cipher.ComputeHash("a"u8), other.ComputeHash("a"u8));
    }

    [Fact]
    public void IterationCountOutsideBoundsIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettingsPayloadCipher("password", Salt, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SettingsPayloadCipher("password", Salt, int.MaxValue));
    }
}
