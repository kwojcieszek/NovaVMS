using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Presentation.Models;
using NovaVMS.Presentation.ViewModels;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>One log entry; a tap shows or hides the details.</summary>
public sealed partial class LogEntryItemViewModel : ViewModelBase
{
    private readonly ApplicationLogEntry _entry;
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Date and time without milliseconds and time zone, which a phone screen has no room for.</summary>
    public string Time => _entry.Timestamp.Length >= 19 ? _entry.Timestamp[..19] : _entry.Timestamp;

    public string Source => _entry.Source.Length > 0 ? _entry.Source : _entry.Summary;

    public string Summary => _entry.Source.Length > 0 ? _entry.Summary : string.Empty;

    public bool HasSummary => Summary.Length > 0;

    public string Details => _entry.Details;

    public bool HasDetails => Details.Length > 0;

    public string FullText => _entry.FullText;

    public LogEntryItemViewModel(ApplicationLogEntry entry)
    {
        _entry = entry;
    }

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
