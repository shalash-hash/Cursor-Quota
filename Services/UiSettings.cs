using System.Text.Json.Serialization;

namespace Quota.Services;

public sealed class UiSettings
{
    [JsonPropertyName("percentageDecimalPlaces")]
    public int PercentageDecimalPlaces { get; set; } = 2;

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }

    [JsonPropertyName("languageChosenByUser")]
    public bool LanguageChosenByUser { get; set; }

    [JsonPropertyName("windowWidth")]
    public double? WindowWidth { get; set; }

    [JsonPropertyName("windowHeight")]
    public double? WindowHeight { get; set; }

    [JsonPropertyName("isDarkMode")]
    public bool IsDarkMode { get; set; }
}
