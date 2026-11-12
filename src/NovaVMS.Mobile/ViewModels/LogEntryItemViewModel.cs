using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovaVMS.Presentation.Models;

namespace NovaVMS.Mobile.ViewModels;

/// <summary>One log entry; a tap shows or hides the details.</summary>
public sealed partial class LogEntryItemViewModel(ApplicationLogEntry entry) : ViewModelBase
{
    [ObservableProperty]
    private bool _isExpanded;

    /// <summary>Date and time without milliseconds and time zone, which a phone screen has no room for.</summary>
    public string Time => entry.Timestamp.Length >= 19 ? entry.Timestamp[..19] : entry.Timestamp;

    public string Source => entry.Source.Length > 0 ? entry.Source : entry.Summary;

    public string Summary => entry.Source.Length > 0 ? entry.Summary : string.Empty;

    public bool HasSummary => Summary.Length > 0;

    public string Details => entry.Details;

    public bool HasDetails => Details.Length > 0;

    public string FullText => entry.FullText;

    [RelayCommand]
    private void Toggle()
    {
        IsExpanded = !IsExpanded;
    }
}
