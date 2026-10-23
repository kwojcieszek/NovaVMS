using System.Net;
using System.Text;
using NovaVMS.App.Services;
using NovaVMS.Core.Models;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class UpdateCatalogTests
{
    [Theory]
    [InlineData("2026.10.21", "2026.10.20", true)]
    [InlineData("2026.10.21", "2026.10.21", false)]
    [InlineData("2026.09.30", "2026.10.01", false)]
    public async Task ManifestComparesReleaseDatesAndUsesConventionalFilename(string latest, string current, bool expected)
    {
        using var client = Client(_ => Json($$"""{"version":"{{latest}}"}"""));
        var update = await UpdateCatalog.CheckAsync(client, "https://example.invalid/releases/", current, default);
        Assert.Equal(expected, update is not null);
        if (update is not null) Assert.Equal($"https://example.invalid/releases/novavms_{latest.Replace('.', '_')}.zip", update.PackageUri.AbsoluteUri);
    }

    [Fact]
    public async Task DirectoryListingFindsNewestValidPackageWithoutManifest()
    {
        using var client = Client(request => request.RequestUri!.AbsolutePath.EndsWith("latest.json")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("novavms_2026_10_20.zip novavms_2026_10_23.zip novavms_2026_10_22.zip novavms_2026_99_99.zip") });
        var update = await UpdateCatalog.CheckAsync(client, "https://example.invalid/releases", "2026.10.21", default);
        Assert.Equal("2026.10.23", update!.VersionText);
    }

    [Theory]
    [InlineData("https://other.invalid/novavms_2026_10_22.zip")]
    [InlineData("novavms_2026_10_23.zip")]
    [InlineData("../app.exe")]
    public async Task ManifestRejectsDifferentServerAndMismatchedPackage(string file)
    {
        using var client = Client(_ => Json($$"""{"version":"2026.10.22","file":"{{file}}"}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateCatalog.CheckAsync(client, "https://example.invalid/releases/", "2026.10.21", default));
    }

    [Fact]
    public async Task ManifestRetainsChecksumAndRejectsMalformedChecksum()
    {
        var hash = new string('a', 64);
        using var client = Client(_ => Json($$"""{"version":"2026.10.22","sha256":"{{hash}}"}"""));
        Assert.Equal(hash, (await UpdateCatalog.CheckAsync(client, "https://example.invalid/releases/", "2026.10.21", default))!.Sha256);
        using var invalid = Client(_ => Json("""{"version":"2026.10.22","sha256":"wrong"}"""));
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateCatalog.CheckAsync(invalid, "https://example.invalid/releases/", "2026.10.21", default));
    }

    [Fact]
    public async Task ServerFailureDoesNotClaimApplicationIsCurrent()
    {
        using var client = Client(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        await Assert.ThrowsAsync<HttpRequestException>(() => UpdateCatalog.CheckAsync(client, "https://example.invalid/releases/", "2026.10.21", default));
    }

    [Fact]
    public async Task DownloadLimitIsEnforcedWhileStreaming()
    {
        using var source = new MemoryStream(new byte[4096]);
        using var target = new MemoryStream();
        await Assert.ThrowsAsync<InvalidDataException>(() => UpdateCatalog.CopyLimitedAsync(source, target, 1024, null, default));
        Assert.Equal(0, target.Length);
    }

    [Fact]
    public async Task UpdatePreferencesSurviveSnapshotAndSerialization()
    {
        var config = new AppConfiguration();
        config.Settings.AutoCheckForUpdates = true;
        config.Settings.AutoDownloadUpdates = true;
        config.Settings.UpdateServerUrl = "https://example.invalid/releases/";
        var snapshot = config.CreateSnapshot();
        config.Settings.UpdateServerUrl = "changed";
        using var stream = new MemoryStream();
        var serializer = new JsonConfigurationSerializer();
        await serializer.SerializeAsync(stream, snapshot);
        stream.Position = 0;
        var restored = await serializer.DeserializeAsync(stream);
        Assert.True(restored.Settings.AutoCheckForUpdates);
        Assert.True(restored.Settings.AutoDownloadUpdates);
        Assert.Equal("https://example.invalid/releases/", restored.Settings.UpdateServerUrl);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, Encoding.UTF8, "application/json") };
    private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> handle) => new(new Handler(handle));
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(handle(request));
    }
}
