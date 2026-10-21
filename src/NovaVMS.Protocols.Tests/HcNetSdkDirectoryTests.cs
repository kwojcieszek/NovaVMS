using NovaVMS.Core.Models;
using NovaVMS.Protocols.HcNetSdk;
using Xunit;

namespace NovaVMS.Protocols.Tests;

public sealed class HcNetSdkDirectoryTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("hcnetsdk")]
    [InlineData(" hcnetsdk ")]
    public void DefaultFolderIsResolvedBesideApplication(string configured)
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "hcnetsdk"),
            HcNetSdkRuntime.ResolveDirectory(configured));
    }

    [Fact]
    public void NewSettingsUsePortableDefault()
    {
        Assert.Equal("hcnetsdk", new AppSettings().HcNetSdkDirectory);
    }

    [Fact]
    public void ExplicitAbsoluteFolderIsPreserved()
    {
        var directory = Path.Combine(Path.GetTempPath(), "custom-hikvision-sdk");
        Assert.Equal(directory, HcNetSdkRuntime.ResolveDirectory(directory));
    }

    [Fact]
    public void CustomRelativeFolderIsAlsoBasedOnApplicationDirectory()
    {
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "sdk", "hikvision"),
            HcNetSdkRuntime.ResolveDirectory(Path.Combine("sdk", "hikvision")));
    }
}
