using System.Text.Json.Serialization;

namespace Quota.Services;

public sealed class UiSettings
{
    [JsonPropertyName("percentageDecimalPlaces")]
    public int PercentageDecimalPlaces { get; set; } = 1;

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }
}
