using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using Avalonia.Headless.XUnit;
using NovaVMS.App.Services;
using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.App.Tests;

[Collection("Tile attachment")]
public sealed class AutomaticUpdateDownloadTests
{
    [AvaloniaFact]
    public async Task AutomaticDownloadPromptsImmediatelyAndDoesNotRepeatAfterDeclining()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = DirectoryPath();
        var handler = new Server(Package());
        var configuration = await ConfigurationAsync(autoDownload: true);
        var dialogs = new Dialogs();
        using (var updates = new ApplicationUpdateService(configuration, dialogs, new HttpClient(handler, false), directory))
        {
            await updates.CheckCommand.ExecuteAsync(null);
            Assert.True(updates.HasUpdate);
            Assert.True(updates.IsDownloaded);
            Assert.Equal(100, updates.Progress);
            Assert.False(updates.IsBusy);
            Assert.True(updates.InstallCommand.CanExecute(null));
            Assert.Equal(1, handler.Downloads);
            Assert.Equal(1, dialogs.Confirmations);
            await updates.CheckCommand.ExecuteAsync(null);
            Assert.Equal(1, dialogs.Confirmations);
            Assert.Equal(1, handler.Downloads);
        }
        // Automatic download may be disabled later; the complete cached package stays usable.
        configuration.Current.Settings.AutoDownloadUpdates = false;
        using var restarted = new ApplicationUpdateService(configuration, dialogs, new HttpClient(handler, false), directory);
        await restarted.CheckCommand.ExecuteAsync(null);
        Assert.True(restarted.IsDownloaded);
        Assert.Equal(1, handler.Downloads);
        Assert.Equal(1, dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task DisabledOptionChecksManifestWithoutDownloading()
    {
        if (!OperatingSystem.IsWindows()) return;
        var handler = new Server(Package());
        var dialogs = new Dialogs();
        using var updates = new ApplicationUpdateService(await ConfigurationAsync(false), dialogs, new HttpClient(handler), DirectoryPath());
        await updates.CheckCommand.ExecuteAsync(null);
        Assert.True(updates.HasUpdate);
        Assert.False(updates.IsDownloaded);
        Assert.Equal(0, handler.Downloads);
        Assert.Equal(0, dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task DownloadOptionAloneStartsAutomaticChecksAndDownloads()
    {
        if (!OperatingSystem.IsWindows()) return;
        var configuration = await ConfigurationAsync(true);
        Assert.False(configuration.Current.Settings.AutoCheckForUpdates);
        var handler = new Server(Package());
        var dialogs = new Dialogs();
        using var updates = new ApplicationUpdateService(configuration, dialogs, new HttpClient(handler), DirectoryPath());
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        updates.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(updates.IsBusy) && !updates.IsBusy && updates.IsDownloaded) ready.TrySetResult();
        };
        updates.Start();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(8));
        Assert.Equal(1, handler.Downloads);
        Assert.Equal(1, dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task ChecksumFailureDoesNotExposeOrCacheAnInstallableDownload()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = DirectoryPath();
        var handler = new Server(Package()) { AdvertisedHash = new string('0', 64) };
        var dialogs = new Dialogs();
        using var updates = new ApplicationUpdateService(await ConfigurationAsync(true), dialogs, new HttpClient(handler), directory);
        await updates.CheckCommand.ExecuteAsync(null);
        Assert.False(updates.IsDownloaded);
        Assert.False(updates.IsBusy);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Equal(0, dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task CancelledDownloadRemovesPartialFileAndCanBeRetried()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = DirectoryPath();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new Server(Package()) { SlowDownload = entered };
        var dialogs = new Dialogs();
        using var updates = new ApplicationUpdateService(await ConfigurationAsync(true), dialogs, new HttpClient(handler), directory);
        var checking = updates.CheckCommand.ExecuteAsync(null);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        updates.CancelCommand.Execute(null);
        await checking.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(updates.IsDownloaded);
        Assert.False(updates.IsBusy);
        Assert.Empty(Directory.GetFiles(directory));
        Assert.Equal(0, dialogs.Confirmations);
        handler.SlowDownload = null;
        await updates.CheckCommand.ExecuteAsync(null);
        Assert.True(updates.IsDownloaded);
        Assert.Equal(2, handler.Downloads);
        Assert.Equal(1, dialogs.Confirmations);
    }

    [AvaloniaFact]
    public async Task PromptAppearsAsSoonAsPackageIsReadyWithoutPressingInstall()
    {
        if (!OperatingSystem.IsWindows()) return;
        var reply = new TaskCompletionSource<bool>();
        var dialogs = new Dialogs { Reply = reply.Task };
        var handler = new Server(Package());
        using var updates = new ApplicationUpdateService(await ConfigurationAsync(true), dialogs, new HttpClient(handler), DirectoryPath());
        var checking = updates.CheckCommand.ExecuteAsync(null);
        try
        {
            await dialogs.Shown.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(updates.IsDownloaded);
            Assert.Equal(100, updates.Progress);
            Assert.False(checking.IsCompleted);
            Assert.Equal(1, dialogs.Confirmations);
            reply.SetResult(false);
            await checking;
            Assert.True(updates.IsDownloaded);
            Assert.True(updates.InstallCommand.CanExecute(null));
        }
        finally { reply.TrySetResult(false); await checking; }
    }

    [AvaloniaFact]
    public async Task RestartPromptsForCachedPackageWithoutDownloadingAgain()
    {
        if (!OperatingSystem.IsWindows()) return;
        var directory = DirectoryPath();
        var configuration = await ConfigurationAsync(true);
        var handler = new Server(Package());
        var dialogs = new Dialogs();
        for (var start = 0; start < 2; start++)
        {
            using var updates = new ApplicationUpdateService(configuration, dialogs, new HttpClient(handler, false), directory);
            await updates.CheckCommand.ExecuteAsync(null);
            Assert.True(updates.IsDownloaded);
            Assert.Equal(start + 1, dialogs.Confirmations);
        }
        Assert.Equal(1, handler.Downloads);
    }

    [Fact]
    public async Task ModifiedCachedArchiveIsDownloadedAgain()
    {
        var handler = new Server(Package());
        using var client = new HttpClient(handler);
        var cache = new UpdateDownloadCache(client, DirectoryPath());
        var update = new ApplicationUpdate(new DateOnly(2099, 1, 1), new Uri("https://updates.invalid/novavms_2099_01_01.zip"), null);
        var first = await cache.GetAsync(update, null, default);
        await File.AppendAllTextAsync(first.PackagePath, "modified");
        Assert.Null(await cache.TryGetAsync(update, default));
        var second = await cache.GetAsync(update, null, default);
        Assert.Equal(first.Sha256, second.Sha256);
        Assert.Equal(2, handler.Downloads);
    }

    [Fact]
    public async Task InvalidArchiveIsRejectedWithoutLeavingCachedOrPartialFiles()
    {
        var directory = DirectoryPath();
        using var client = new HttpClient(new Server(Encoding.UTF8.GetBytes("not a ZIP")));
        var cache = new UpdateDownloadCache(client, directory);
        var update = new ApplicationUpdate(new DateOnly(2099, 1, 1), new Uri("https://updates.invalid/novavms_2099_01_01.zip"), null);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync(update, null, default));
        Assert.Empty(Directory.GetFiles(directory));
    }

    private static string DirectoryPath() => Path.Combine(Path.GetTempPath(), "NovaVMS-update-tests", Guid.NewGuid().ToString("N"));

    private static async Task<ConfigurationService> ConfigurationAsync(bool autoDownload)
    {
        var configuration = new ConfigurationService(new MemoryConfigurationStore(new()), new JsonConfigurationSerializer());
        await configuration.LoadAsync();
        configuration.Current.Settings.AutoDownloadUpdates = autoDownload;
        configuration.Current.Settings.UpdateServerUrl = "https://updates.invalid/";
        return configuration;
    }

    private static byte[] Package()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            foreach (var name in new[] { "NovaVMS.exe", "NovaVMS.dll", "NovaVMS.deps.json", "NovaVMS.runtimeconfig.json",
                "updater/NovaVMS.Updater.exe", "updater/NovaVMS.Updater.dll", "updater/NovaVMS.Updater.deps.json", "updater/NovaVMS.Updater.runtimeconfig.json" })
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write("test package");
            }
        return output.ToArray();
    }

    private sealed class Server(byte[] package) : HttpMessageHandler
    {
        public int Downloads { get; private set; }
        public string? AdvertisedHash { get; init; }
        public TaskCompletionSource? SlowDownload { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("latest.json"))
            {
                var hash = AdvertisedHash ?? Convert.ToHexString(SHA256.HashData(package));
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                    { Content = new StringContent($$"""{"version":"2099.01.01","sha256":"{{hash}}"}""", Encoding.UTF8, "application/json") });
            }
            Downloads++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = SlowDownload is { } entered ? new StreamContent(new WaitingStream(entered)) : new ByteArrayContent(package)
            });
        }
    }

    private sealed class WaitingStream(TaskCompletionSource entered) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken token = default)
        {
            entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class Dialogs : IDialogService
    {
        public Task<bool> Reply { get; init; } = Task.FromResult(false);
        public TaskCompletionSource Shown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Confirmations { get; private set; }
        public Task<bool> ConfirmAsync(string title, string message)
        {
            Confirmations++;
            Shown.TrySetResult();
            return Reply;
        }
        public Task<string?> PromptTextAsync(string title, string message, string initialValue) => throw new NotSupportedException();
        public Task<bool> LoginAsync(UserSessionService session, string userName) => throw new NotSupportedException();
        public Task<Stream?> OpenJsonFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveJsonFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<Stream?> OpenCsvFileAsync(string title) => throw new NotSupportedException();
        public Task<(Stream Content, Stream? Setup)?> OpenIvmsFileAsync(string title) => throw new NotSupportedException();
        public Task<Stream?> SaveCsvFileAsync(string title, string suggestedFileName) => throw new NotSupportedException();
        public Task<string?> PickFolderAsync(string title) => throw new NotSupportedException();
    }
}
