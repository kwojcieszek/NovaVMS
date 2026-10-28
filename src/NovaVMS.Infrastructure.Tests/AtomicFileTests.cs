using NovaVMS.Infrastructure.Storage;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

public sealed class AtomicFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-atomic-file-tests", Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_directory, "settings.json");

    [Fact]
    public async Task CreatesDirectoryAndReplacesEntireFileWithoutTemporaryFiles()
    {
        await AtomicFile.WriteAsync(FilePath, "original contents"u8.ToArray(), CancellationToken.None);
        await AtomicFile.WriteAsync(FilePath, "new"u8.ToArray(), CancellationToken.None);

        Assert.Equal("new", await File.ReadAllTextAsync(FilePath));
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task CancelledWritePreservesExistingFile()
    {
        Directory.CreateDirectory(_directory);
        await File.WriteAllTextAsync(FilePath, "original");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            AtomicFile.WriteAsync(FilePath, "replacement"u8.ToArray(), cancellation.Token));

        Assert.Equal("original", await File.ReadAllTextAsync(FilePath));
        Assert.Equal(new[] { FilePath }, Directory.GetFiles(_directory));
    }

    [Fact]
    public async Task FailedReplacementRemovesTemporaryFile()
    {
        // A directory at the destination deterministically prevents replacement.
        Directory.CreateDirectory(FilePath);
        var marker = Path.Combine(FilePath, "marker");
        await File.WriteAllTextAsync(marker, "original");

        var error = await Record.ExceptionAsync(() =>
            AtomicFile.WriteAsync(FilePath, "replacement"u8.ToArray(), CancellationToken.None));

        Assert.True(error is IOException or UnauthorizedAccessException, error?.ToString());
        Assert.Equal("original", await File.ReadAllTextAsync(marker));
        Assert.Empty(Directory.GetFiles(_directory));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
