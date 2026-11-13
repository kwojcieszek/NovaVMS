using System.Text;
using NovaVMS.Mobile.Services;
using Xunit;

namespace NovaVMS.Mobile.Tests;

public sealed class MobileLogTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "NovaVMS-log-" + Guid.NewGuid().ToString("N"));

    public MobileLogTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public void OldestFilesAreRemovedFirst()
    {
        File.WriteAllText(Path.Combine(_directory, "errors-20261110.log"), Entry(1) + Entry(2));
        File.WriteAllText(Path.Combine(_directory, "errors-20261112.log"), Entry(3));

        MobileLog.Trim(_directory, 500);

        Assert.Equal(["errors-20261112.log"], Directory.GetFiles(_directory).Select(Path.GetFileName));
    }

    [Fact]
    public void NewestFileKeepsWholeNewestEntriesWithinTheLimit()
    {
        var path = Path.Combine(_directory, "errors-20261112.log");

        File.WriteAllText(path, string.Concat(Enumerable.Range(1, 30).Select(Entry)));

        MobileLog.Trim(_directory, 5 * 1024);

        var text = File.ReadAllText(path);

        Assert.True(new FileInfo(path).Length <= 5 * 1024);
        Assert.StartsWith("[2026-11-12", text);
        Assert.EndsWith(Entry(30), text);
        Assert.DoesNotContain("Entry 1\n", text);
    }

    [Fact]
    public void LogFilesKeepUpToFiveMegabytes()
    {
        Assert.Equal(5 * 1024 * 1024, MobileLog.MaxBytes);
    }

    [Fact]
    public void EntryLongerThanTheLimitKeepsItsEnd()
    {
        var path = Path.Combine(_directory, "errors-20261112.log");
        var lines = string.Concat(Enumerable.Range(1, 200).Select(line => $"   at Frame{line}()\n"));

        File.WriteAllText(path, "[2026-11-12 14:05:00.000 +01:00] [PID 1, thread 2] Long\n" + lines, new UTF8Encoding(false));

        MobileLog.Trim(_directory, 1024);

        var text = File.ReadAllText(path);

        Assert.True(text.Length is > 0 and <= 1024);
        Assert.StartsWith("   at Frame", text);
        Assert.EndsWith("   at Frame200()\n", text);
    }

    private static string Entry(int number)
    {
        return $"[2026-11-12 14:05:{number % 60:00}.000 +01:00] [PID 1, thread 2] Entry {number}\n{new string('x', 300)}\n\n";
    }
}
