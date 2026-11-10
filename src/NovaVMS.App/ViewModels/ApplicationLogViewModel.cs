using System.Text;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.App.Services;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.App.ViewModels;

public sealed partial class ApplicationLogViewModel : ViewModelBase
{
    private const int PreviewBytes = 128 * 1024;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private string _status = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ApplicationLogEntry> _entries = [];

    [ObservableProperty]
    private ApplicationLogEntry? _selectedEntry;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCommand))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand))]
    private bool _isBusy;

    internal static IReadOnlyList<ApplicationLogEntry> ParseEntries(string text)
    {
        var rows = new List<ApplicationLogEntry>();
        var headers = EntryHeader().Matches(text);
        var prefix = text[..(headers.Count == 0 ? text.Length : headers[0].Index)].Trim();

        if (prefix.Length > 0)
        {
            rows.Add(new ApplicationLogEntry("", "", "", "", prefix));
        }

        for (var i = 0; i < headers.Count; i++)
        {
            var header = headers[i];
            var end = i + 1 < headers.Count ? headers[i + 1].Index : text.Length;
            var details = text[(header.Index + header.Length)..end].TrimEnd().TrimStart('\r', '\n');

            rows.Add(new ApplicationLogEntry(header.Groups[1].Value, header.Groups[2].Value,
                header.Groups[3].Value, header.Groups[4].Value, details));
        }

        rows.Reverse();

        return rows;
    }

    internal static (string Path, string Text, bool Truncated) ReadLatest(params string[] directories)
    {
        var latest = directories.Distinct().Where(Directory.Exists)
            .SelectMany(directory => new DirectoryInfo(directory).EnumerateFiles("errors-????????.log"))
            .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();

        if (latest is null)
        {
            return (string.Empty, string.Empty, false);
        }

        using var stream = new FileStream(latest.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        var count = (int)Math.Min(length, PreviewBytes);
        stream.Position = length - count;
        var bytes = new byte[count];

        stream.ReadExactly(bytes);

        var start = 0;

        if (length > count)
        {
            // The tail can start in the middle of a UTF-8 character.
            while (start < count && (bytes[start] & 0xC0) == 0x80)
            {
                start++;
            }
        }

        var text = Encoding.UTF8.GetString(bytes, start, count - start);

        return (latest.FullName, text, length > count);
    }

    [GeneratedRegex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2})\] \[PID (\d+), thread (\d+)\] ([^\r\n]*)", RegexOptions.Multiline)]
    private static partial Regex EntryHeader();

    private bool CanRefresh() => !IsBusy;

    private bool CanClear() => !IsBusy && FilePath.Length > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private async Task ClearAsync()
    {
        if (!CanClear())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await UnhandledExceptionLogger.ClearFileAsync(FilePath);

            SelectedEntry = null;
            Entries = [];
            Status = Localizer.Text("Log_Empty");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Status = Localizer.Format("Log_ClearError", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        if (!CanRefresh())
        {
            return;
        }

        IsBusy = true;

        try
        {
            await UnhandledExceptionLogger.FlushAsync();

            var result = await Task.Run(() => ReadLatest(UnhandledExceptionLogger.LogDirectory,
                UnhandledExceptionLogger.FallbackLogDirectory));
            var previous = SelectedEntry;
            FilePath = result.Path;
            Entries = ParseEntries(result.Text);
            SelectedEntry = Entries.FirstOrDefault(entry => entry == previous) ?? Entries.FirstOrDefault();
            Status = result.Truncated ? Localizer.Text("Log_Tail")
                : Entries.Count == 0 ? Localizer.Text("Log_Empty") : string.Empty;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            FilePath = string.Empty;
            Entries = [];
            SelectedEntry = null;
            Status = Localizer.Format("Log_Error", exception.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
