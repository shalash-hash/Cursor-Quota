using Quota.Helpers;
using Quota.Localization;
using Xunit;

namespace Quota.Tests;

public class RemainingTimeFormatterTests
{
    private readonly ILocalizationService _localization = new FakeLocalization();

    [Fact]
    public void Format_FullDays_CountsPartialLastDay()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromHours(47), _localization);

        Assert.Equal("2 days", text);
    }

    [Fact]
    public void Format_TwoDaysThreeHours_ShowsThreeDays()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromDays(2) + TimeSpan.FromHours(3), _localization);

        Assert.Equal("3 days", text);
    }

    [Fact]
    public void Format_TenDaysAndChange_ShowsElevenDays()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromDays(10) + TimeSpan.FromHours(11), _localization);

        Assert.Equal("11 days", text);
    }

    [Fact]
    public void Format_ExactlyOneDay_ShowsOneDay()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromDays(1), _localization);

        Assert.Equal("1 day", text);
    }

    [Fact]
    public void Format_ExactlyTwentyFourHours_ShowsOneDay()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromHours(24), _localization);

        Assert.Equal("1 day", text);
    }

    [Fact]
    public void Format_TwentyThreeHoursFiftyNineMinutes_ShowsHoursAndMinutes()
    {
        var text = RemainingTimeFormatter.Format(
            TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59),
            _localization);

        Assert.Equal("23 h 59 m", text);
    }

    [Fact]
    public void Format_FifteenHoursNineteenMinutes_FloorsSeconds()
    {
        var text = RemainingTimeFormatter.Format(
            TimeSpan.FromHours(15) + TimeSpan.FromMinutes(19) + TimeSpan.FromSeconds(48),
            _localization);

        Assert.Equal("15 h 19 m", text);
    }

    [Fact]
    public void Format_ExactlyOneHour_ShowsHourOnly()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromHours(1), _localization);

        Assert.Equal("1 hour", text);
    }

    [Fact]
    public void Format_FiftyNineMinutesFiftyNineSeconds_ShowsMinutes()
    {
        var text = RemainingTimeFormatter.Format(
            TimeSpan.FromMinutes(59) + TimeSpan.FromSeconds(59),
            _localization);

        Assert.Equal("59 minutes", text);
    }

    [Fact]
    public void Format_ExactlyOneMinute_ShowsOneMinute()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromMinutes(1), _localization);

        Assert.Equal("1 minute", text);
    }

    [Fact]
    public void Format_FiftyNineSeconds_ShowsSeconds()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromSeconds(59), _localization);

        Assert.Equal("59 seconds", text);
    }

    [Fact]
    public void Format_OneSecond_ShowsOneSecond()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromSeconds(1), _localization);

        Assert.Equal("1 second", text);
    }

    [Fact]
    public void Format_Zero_ShowsZeroSeconds()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.Zero, _localization);

        Assert.Equal("0 seconds", text);
    }

    [Fact]
    public void Format_Negative_ClampedToZeroSeconds()
    {
        var text = RemainingTimeFormatter.Format(TimeSpan.FromMinutes(-5), _localization);

        Assert.Equal("0 seconds", text);
    }

    [Fact]
    public void SuggestedRefreshInterval_AtLeastTwentyFourHours_UsesMinuteTimer()
    {
        var interval = RemainingTimeFormatter.SuggestedRefreshInterval(TimeSpan.FromHours(24));

        Assert.Equal(TimeSpan.FromMinutes(1), interval);
    }

    [Fact]
    public void SuggestedRefreshInterval_UnderTwentyFourHours_UsesSecondTimer()
    {
        var interval = RemainingTimeFormatter.SuggestedRefreshInterval(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(59));

        Assert.Equal(TimeSpan.FromSeconds(1), interval);
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
            "HoursMinutesAbbreviatedFormat" => "{0} h {1} m",
            _ => key
        };

        public string Format(string key, params object[] args) =>
            string.Format(CurrentCulture, GetString(key), args);
    }
}
