using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Mobile.Models;

/// <summary>
/// Number of cameras in one row of the live preview grid; 0 chooses the count giving the largest pictures.
/// </summary>
public sealed class PreviewColumnsOption : ObservableObject
{
    public const int Automatic = 0;
    public const int MaximumColumns = 6;

    public static IReadOnlyList<PreviewColumnsOption> All { get; } = Enumerable.Range(Automatic, MaximumColumns + 1)
        .Select(value => new PreviewColumnsOption(value))
        .ToList()
        .AsReadOnly();

    public int Value { get; }

    public bool IsAutomatic => Value == Automatic;

    /// <summary>Full name for the settings list.</summary>
    public string Label => IsAutomatic ? Localizer.Text("Mobile_PreviewColumnsAuto") : Value.ToString();

    /// <summary>Short name for the preview header button.</summary>
    public string ShortLabel => IsAutomatic ? Localizer.Text("Mobile_PreviewColumnsAutoShort") : Value.ToString();

    private PreviewColumnsOption(int value)
    {
        Value = value;
        Localizer.Instance.LanguageChanged += (_, _) => OnLanguageChanged();
    }

    public static PreviewColumnsOption Find(int value)
    {
        return All.FirstOrDefault(option => option.Value == value) ?? All[0];
    }

    private void OnLanguageChanged()
    {
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(ShortLabel));
    }
}
