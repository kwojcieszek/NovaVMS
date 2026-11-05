using System.Text;
using NovaVMS.App.ViewModels;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class ApplicationLogTests
{
    [Fact]
    public async Task ClearCommandEmptiesOnlyTheDisplayedFileAndAllowsFurtherWrites()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            var path = Path.Combine(directory.FullName, "errors-20261105.log");
            var other = Path.Combine(directory.FullName, "errors-20261106.log");
            File.WriteAllText(path, "old entry");
            File.WriteAllText(other, "another day");
            var vm = new ApplicationLogViewModel();
            Assert.False(vm.ClearCommand.CanExecute(null));
            vm.FilePath = path;
            vm.Entries = ApplicationLogViewModel.ParseEntries("old entry");
            vm.SelectedEntry = vm.Entries[0];
            await vm.ClearCommand.ExecuteAsync(null);
            Assert.Equal(0, new FileInfo(path).Length);
            Assert.Empty(vm.Entries);
            Assert.Null(vm.SelectedEntry);
            Assert.Equal("another day", File.ReadAllText(other));
            File.AppendAllText(path, "new entry");
            Assert.Equal("new entry", File.ReadAllText(path));
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public void GridGroupsMultilineEntriesAndKeepsIncompleteTail()
    {
        const string text = "partial stack frame\n" +
            "[2026-11-05 10:20:30.123 +01:00] [PID 120, thread 3] Camera sync\r\nError\r\n  at Connect()\r\n\r\n" +
            "[2026-11-05 10:21:30.123 +01:00] [PID 120, thread 8] Playback\r\nNext error";
        var entries = ApplicationLogViewModel.ParseEntries(text);
        Assert.Equal(3, entries.Count);
        Assert.Equal("Playback", entries[0].Source);
        Assert.Equal("3", entries[1].ThreadId);
        Assert.Equal("Error", entries[1].Summary);
        Assert.Contains("  at Connect()", entries[1].FullText);
        Assert.Equal("partial stack frame", entries[2].Details);
    }

    [Fact]
    public void PreviewUsesLatestDailyLogAndPreservesUtf8TailWhileFileIsOpen()
    {
        var directory = Directory.CreateTempSubdirectory();
        try
        {
            Assert.Empty(ApplicationLogViewModel.ReadLatest(directory.FullName).Path);
            var first = Path.Combine(directory.FullName, "errors-20261104.log");
            File.WriteAllText(first, "older");
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddDays(-1));
            var fallback = directory.CreateSubdirectory("fallback");
            var latest = Path.Combine(fallback.FullName, "errors-20261105.log");
            using var writer = new FileStream(latest, FileMode.Create, FileAccess.Write, FileShare.Read);
            var contents = "old entry" + new string('ą', 70000) + " koniec";
            writer.Write(Encoding.UTF8.GetBytes(contents));
            writer.Flush();
            var result = ApplicationLogViewModel.ReadLatest(directory.FullName, fallback.FullName);
            Assert.Equal(latest, result.Path);
            Assert.DoesNotContain("old entry", result.Text);
            Assert.DoesNotContain("\uFFFD", result.Text);
            Assert.EndsWith(" koniec", result.Text);
        }
        finally { directory.Delete(true); }
    }
}
