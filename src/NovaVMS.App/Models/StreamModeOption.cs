using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.App.Localization;
using NovaVMS.Core.Models;

namespace NovaVMS.App.Models;

/// <summary>
/// Entry of the stream mode list in the settings.
/// </summary>
public sealed class StreamModeOption : ObservableObject
{
    private readonly string _labelKey;

    private StreamModeOption(GridStreamMode value, string labelKey)
    {
        Value = value;
        _labelKey = labelKey;
        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    public static IReadOnlyList<StreamModeOption> All { get; } = new List<StreamModeOption>
    {
        new(GridStreamMode.Auto, "Stream_Auto"),
        new(GridStreamMode.Main, "Stream_Main"),
        new(GridStreamMode.Sub, "Stream_Sub")
    };

    public GridStreamMode Value { get; }

    public string Label => Localizer.Text(_labelKey);

    public static StreamModeOption Find(GridStreamMode value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }
}
