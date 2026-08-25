using System.Text.Json.Serialization;

namespace Quota.Services.CursorApi;

internal sealed class CurrentPeriodUsageResponse
{
    [JsonPropertyName("billingCycleStart")]
    public string? BillingCycleStart { get; set; }

    [JsonPropertyName("billingCycleEnd")]
    public string? BillingCycleEnd { get; set; }

    [JsonPropertyName("planUsage")]
    public PlanUsage? PlanUsage { get; set; }
}

internal sealed class PlanUsage
{
    [JsonPropertyName("totalSpend")]
    public long? TotalSpend { get; set; }

    [JsonPropertyName("includedSpend")]
    public long? IncludedSpend { get; set; }

    [JsonPropertyName("bonusSpend")]
    public long? BonusSpend { get; set; }

    [JsonPropertyName("totalPercentUsed")]
    public double? TotalPercentUsed { get; set; }

    [JsonPropertyName("autoPercentUsed")]
    public double? AutoPercentUsed { get; set; }

    [JsonPropertyName("apiPercentUsed")]
    public double? ApiPercentUsed { get; set; }

    [JsonPropertyName("limit")]
    public long? Limit { get; set; }
}

internal sealed class PlanInfoResponse
{
    [JsonPropertyName("planInfo")]
    public PlanInfo? PlanInfo { get; set; }
}

internal sealed class PlanInfo
{
    [JsonPropertyName("planName")]
    public string? PlanName { get; set; }

    [JsonPropertyName("includedAmountCents")]
    public long? IncludedAmountCents { get; set; }
}
