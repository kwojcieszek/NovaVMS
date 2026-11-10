using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using NovaVMS.Core.Localization;
using NovaVMS.Presentation.Localization;
using NovaVMS.Presentation.Models;
using Xunit;

namespace NovaVMS.App.Tests;

public sealed class InterfaceLanguageTests
{
    private static readonly Regex Placeholder = new(@"\{\d+\}", RegexOptions.Compiled);

    public static TheoryData<string> Languages()
    {
        var data = new TheoryData<string>();

        foreach (var language in InterfaceLanguage.All)
        {
            data.Add(language.Code);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void EveryLanguageTranslatesEveryTextWithTheSamePlaceholders(string code)
    {
        var english = Catalog(JsonTextCatalog.FallbackLanguage);
        var translated = Catalog(code);

        Assert.Equal(english.Keys.Order(), translated.Keys.Order());

        foreach (var (key, text) in english)
        {
            var expected = Placeholder.Matches(text).Select(match => match.Value).Order();
            var actual = Placeholder.Matches(translated[key]).Select(match => match.Value).Order();

            Assert.True(expected.SequenceEqual(actual), $"{code}: {key} has different placeholders.");
        }
    }

    [Theory]
    [MemberData(nameof(Languages))]
    public void LanguageUsesItsOwnCatalog(string code)
    {
        var language = InterfaceLanguage.Find(code);
        var culture = CultureInfo.GetCultureInfo(language.CultureName);

        Assert.Equal(code, culture.TwoLetterISOLanguageName);
        Assert.Equal(Catalog(code)["Common_Yes"], JsonTextCatalog.Shared.Get("Common_Yes", culture));
    }

    [Fact]
    public void UnknownLanguageFallsBackToEnglish()
    {
        Assert.Equal(JsonTextCatalog.FallbackLanguage, InterfaceLanguage.Find("xx").Code);
    }

    [Fact]
    public void LanguageListOffersSystemLanguageAndEveryTranslation()
    {
        Assert.Equal(string.Empty, LanguageOption.All[0].Code);
        Assert.Equal(InterfaceLanguage.All.Select(language => language.Code), LanguageOption.All.Skip(1).Select(option => option.Code));
    }

    private static Dictionary<string, string> Catalog(string code)
    {
        using var stream = typeof(JsonTextCatalog).Assembly.GetManifestResourceStream($"NovaVMS.Core.Resources.Strings.{code}.json");

        Assert.NotNull(stream);

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;
    }
}
