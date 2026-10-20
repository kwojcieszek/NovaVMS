using System.Collections.Concurrent;
using NovaVMS.Infrastructure.Diagnostics;
using Xunit;

namespace NovaVMS.Infrastructure.Tests;

[CollectionDefinition("Process exception handlers", DisableParallelization = true)]
public sealed class ProcessExceptionHandlersCollection;

[Collection("Process exception handlers")]
public sealed class ExceptionLoggingTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-logging-tests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CaughtAndAwaitedBackgroundExceptionsAreWrittenWithStackTraces()
    {
        var writer = new ExceptionLogWriter(() => _directory);
        using (var capture = new FirstChanceExceptionCapture(writer.Write))
        {
            capture.Install();
            try { ThrowForLog("caught-marker"); } catch (InvalidOperationException) { }
            try { await Task.Run(() => ThrowForLog("background-marker")); } catch (InvalidOperationException) { }
        }
        var text = ReadLog();
        Assert.Contains("caught-marker", text);
        Assert.Contains("background-marker", text);
        Assert.Contains(nameof(ThrowForLog), text);
        Assert.Contains("First-chance exception", text);
        Assert.Contains("PID ", text);
        Assert.Contains("thread ", text);
    }

    [Fact]
    public void InstallingTwiceDoesNotDuplicateHandlersAndDisposalUnsubscribes()
    {
        var captured = new ConcurrentQueue<Exception>();
        var expected = new InvalidOperationException("capture-marker");
        using (var capture = new FirstChanceExceptionCapture((_, exception) => captured.Enqueue(exception)))
        {
            capture.Install();
            capture.Install();
            try { throw expected; } catch (InvalidOperationException) { }
        }
        try { throw expected; } catch (InvalidOperationException) { }
        Assert.Single(captured, exception => ReferenceEquals(exception, expected));
    }

    [Fact]
    public void FailingLogCallbackDoesNotRecurseOrReplaceOriginalException()
    {
        var calls = 0;
        Exception? caught = null;
        var expected = new InvalidOperationException("original-marker");
        using (var capture = new FirstChanceExceptionCapture((_, _) =>
               {
                   Interlocked.Increment(ref calls);
                   throw new IOException("logging-marker");
               }))
        {
            capture.Install();
            try { throw expected; } catch (InvalidOperationException exception) { caught = exception; }
        }
        Assert.Same(expected, caught);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UnwritablePrimaryDirectoryFallsBackWithoutRecursiveLogging()
    {
        Directory.CreateDirectory(_directory);
        var blocked = Path.Combine(_directory, "blocked");
        File.WriteAllText(blocked, "This file prevents creation of the log directory.");
        var fallback = Path.Combine(_directory, "fallback");
        var writer = new ExceptionLogWriter(() => blocked, () => fallback);
        using (var capture = new FirstChanceExceptionCapture(writer.Write))
        {
            capture.Install();
            try { ThrowForLog("fallback-marker"); } catch (InvalidOperationException) { }
        }
        var log = File.ReadAllText(Assert.Single(Directory.GetFiles(fallback, "errors-*.log")));
        Assert.Contains("fallback-marker", log);
        Assert.True(log.Length < 32_768);
    }

    [Fact]
    public void InvalidConfiguredDirectoryAlsoUsesFallback()
    {
        var writer = new ExceptionLogWriter(() => throw new ArgumentException("Invalid configured path"), () => _directory);
        writer.Write("path failure", new IOException("original-marker"));
        Assert.Contains("original-marker", ReadLog());
    }

    [Fact]
    public void ParallelErrorsAreWrittenAsCompleteEntries()
    {
        var writer = new ExceptionLogWriter(() => _directory);
        Parallel.For(0, 100, index => writer.Write($"operation-{index:D3}", new IOException($"message-{index:D3}")));
        var text = ReadLog();
        for (var index = 0; index < 100; index++)
            Assert.Contains($"operation-{index:D3}{Environment.NewLine}System.IO.IOException: message-{index:D3}{Environment.NewLine}", text);
    }

    private string ReadLog() => File.ReadAllText(Assert.Single(Directory.GetFiles(_directory, "errors-*.log")));
    private static void ThrowForLog(string message) => throw new InvalidOperationException(message);

    public void Dispose()
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "NovaVMS-logging-tests")) + Path.DirectorySeparatorChar;
        var directory = Path.GetFullPath(_directory);
        if (directory.StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);
    }
}
