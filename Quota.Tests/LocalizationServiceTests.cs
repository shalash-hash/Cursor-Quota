using System.Globalization;
using Quota.Localization;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class LocalizationServiceTests
{
    [Fact]
    public void ResolveInitialLanguage_FirstLaunch_UsesSystemCulture()
    {
        var settings = new UiSettings();

        var language = LocalizationService.ResolveInitialLanguage(settings);

        var expected = LocalizationService.FindBestMatch(System.Globalization.CultureInfo.CurrentUICulture)
            ?? LocalizationService.FindBestMatch(
                Quota.Helpers.SystemCultureHelper.GetPrimaryCulture());

        Assert.NotNull(expected);
        Assert.Equal(expected.Culture.Name, language.Culture.Name);
    }

    [Fact]
    public void ResolveInitialLanguage_RemembersUserChoice()
    {
        var settings = new UiSettings
        {
            PreferredLanguage = "de",
            LanguageChosenByUser = true
        };

        var language = LocalizationService.ResolveInitialLanguage(settings);

        Assert.Equal("de", language.Culture.Name);
    }

    [Fact]
    public void ResolveInitialLanguage_LegacySavedLanguage_IsPreserved()
    {
        var settings = new UiSettings
        {
            PreferredLanguage = "fr"
        };

        var language = LocalizationService.ResolveInitialLanguage(settings);

        Assert.Equal("fr", language.Culture.Name);
    }

    [Theory]
    [InlineData("ru-RU", "ru")]
    [InlineData("en-US", "en")]
    [InlineData("zh-CN", "zh-CN")]
    [InlineData("pt-PT", "pt-BR")]
    public void FindBestMatch_MapsSystemCultureToSupportedLanguage(string systemCulture, string expectedCulture)
    {
        var match = LocalizationService.FindBestMatch(CultureInfo.GetCultureInfo(systemCulture));

        Assert.NotNull(match);
        Assert.Equal(expectedCulture, match.Culture.Name);
    }
}
