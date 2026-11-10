using CommunityToolkit.Mvvm.ComponentModel;
using NovaVMS.Presentation.Localization;

namespace NovaVMS.Presentation.Models;

/// <summary>
/// Entry of a settings list: a value with a translated label that follows language changes.
/// </summary>
public abstract class LocalizedOption<TValue> : ObservableObject
{
    private readonly string _labelKey;

    public TValue Value { get; }

    public string Label => Localizer.Text(_labelKey);

    protected LocalizedOption(TValue value, string labelKey)
    {
        Value = value;
        _labelKey = labelKey;

        Localizer.Instance.LanguageChanged += (_, _) => OnPropertyChanged(nameof(Label));
    }

    /// <summary>The entry of <paramref name="value"/>, or the first entry when the list has none.</summary>
    protected static TOption Find<TOption>(IReadOnlyList<TOption> options, TValue value)
        where TOption : LocalizedOption<TValue>
    {
        return options.FirstOrDefault(option => EqualityComparer<TValue>.Default.Equals(option.Value, value)) ?? options[0];
    }
}
