using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of the picture proportions list in the settings.
/// </summary>
public sealed class VideoAspectModeOption : ObservableObject
{
    private readonly string _labelKey;

    private VideoAspectModeOption(VideoAspectMode value, string labelKey)
    {
        Value = value;
        _labelKey = labelKey;
        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static IReadOnlyList<VideoAspectModeOption> All { get; } = new List<VideoAspectModeOption>
    {
        new(VideoAspectMode.Original, "Aspect_Original"),
        new(VideoAspectMode.Fit, "Aspect_Fit")
    };

    public VideoAspectMode Value { get; }

    public string Label => Localizer.Text(_labelKey);

    public static VideoAspectModeOption Find(VideoAspectMode value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }
}
