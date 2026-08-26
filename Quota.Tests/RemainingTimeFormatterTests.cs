using Quota.Helpers;
using Quota.Localization;
using Xunit;

namespace Quota.Tests;

public class RemainingTimeFormatterTests
{
    private readonly ILocalizationService _localization = new FakeLocalization();

    [Fact]
    public void Format_FullDays_UsesWholeDays()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromHours(47), _localization);

        Assert.Equal("1 day", text);
    }

    [Fact]
    public void Format_LessThanOneDay_UsesHours()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromHours(23.9), _localization);

        Assert.Equal("23 hours", text);
    }

    [Fact]
    public void Format_LessThanOneHour_UsesMinutes()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromMinutes(44), _localization);

        Assert.Equal("44 minutes", text);
    }

    [Fact]
    public void Format_LessThanOneMinute_UsesSeconds()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromSeconds(9), _localization);

        Assert.Equal("9 seconds", text);
    }

    private sealed class FakeLocalization : ILocalizationService
    {
        private readonly LanguageOption _language = new("en", "English");

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged
        {
            add { }
            remove { }
        }

        public IReadOnlyList<LanguageOption> SupportedLanguages => [_language];

        public LanguageOption SelectedLanguage
        {
            get => _language;
            set { }
        }

        public System.Globalization.CultureInfo CurrentCulture => _language.Culture;

        public System.Windows.FlowDirection CurrentFlowDirection => _language.FlowDirection;

        public string this[string key] => GetString(key);

        public string GetString(string key) => key switch
        {
            "DaysPatternOne" => "{0} day",
            "DaysPatternOther" => "{0} days",
            "HoursPatternOne" => "{0} hour",
            "HoursPatternOther" => "{0} hours",
            "MinutesPatternOne" => "{0} minute",
            "MinutesPatternOther" => "{0} minutes",
            "SecondsPatternOne" => "{0} second",
            "SecondsPatternOther" => "{0} seconds",
            _ => key
        };

        public string Format(string key, params object[] args) =>
            string.Format(CurrentCulture, GetString(key), args);
    }
}
