using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Quota.Models;
using Quota.Services.CursorApi;

namespace Quota.Services;

public class CursorQuotaUsageProvider : IQuotaUsageProvider
{
    public const string UsageEndpoint =
        "https://api2.cursor.sh/aiserver.v1.DashboardService/GetCurrentPeriodUsage";

    public const string PlanInfoEndpoint =
        "https://api2.cursor.sh/aiserver.v1.DashboardService/GetPlanInfo";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly CursorAuthService _authService;
    private readonly QuotaDiagnosticLogger _logger;

    public CursorQuotaUsageProvider(
        HttpClient httpClient,
        CursorAuthService authService,
        QuotaDiagnosticLogger logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _logger = logger;
    }

    public async Task<QuotaUsage> GetUsageAsync()
    {
        var accessToken = await _authService.GetAccessTokenAsync();

        using var usageRequest = CreateRpcRequest(UsageEndpoint, accessToken);
        using var usageResponse = await _httpClient.SendAsync(usageRequest);

        var usageBody = await usageResponse.Content.ReadAsStringAsync();
        if (!usageResponse.IsSuccessStatusCode)
        {
            _logger.LogFetchFailure((int)usageResponse.StatusCode, UsageEndpoint, "usage request failed");
            throw new CursorQuotaFetchException("Не удалось получить данные квоты Cursor.");
        }

        var usage = JsonSerializer.Deserialize<CurrentPeriodUsageResponse>(usageBody, JsonOptions)
            ?? throw new CursorQuotaFetchException("Не удалось разобрать ответ Cursor.");

        if (usage.PlanUsage is null || !CursorPlanUsageMapper.HasRequiredFields(usage.PlanUsage))
        {
            _logger.LogMissingFields(UsageEndpoint);
            throw new CursorQuotaFetchException("Ответ Cursor не содержит ожидаемых полей квоты.");
        }

        _logger.LogRawUsage(
            usage.PlanUsage.TotalPercentUsed,
            usage.PlanUsage.AutoPercentUsed!.Value,
            usage.PlanUsage.ApiPercentUsed!.Value,
            usage.PlanUsage.TotalSpend,
            usage.PlanUsage.IncludedSpend,
            usage.PlanUsage.Limit);

        string? planName = null;
        long? includedAmountCents = usage.PlanUsage.Limit;

        try
        {
            using var planRequest = CreateRpcRequest(PlanInfoEndpoint, accessToken);
            using var planResponse = await _httpClient.SendAsync(planRequest);
            var planBody = await planResponse.Content.ReadAsStringAsync();

            if (planResponse.IsSuccessStatusCode)
            {
                var planInfo = JsonSerializer.Deserialize<PlanInfoResponse>(planBody, JsonOptions);
                planName = planInfo?.PlanInfo?.PlanName;
                includedAmountCents ??= planInfo?.PlanInfo?.IncludedAmountCents;
            }
        }
        catch
        {
            // Plan info is optional for quota display.
        }

        var periodStart = ParseUnixMilliseconds(usage.BillingCycleStart);
        var periodEnd = ParseUnixMilliseconds(usage.BillingCycleEnd);

        var result = CursorPlanUsageMapper.Map(
            usage.PlanUsage,
            periodStart,
            periodEnd,
            planName,
            includedAmountCents);

        _logger.LogSuccessfulFetch(
            endpoint: UsageEndpoint,
            statusCode: (int)usageResponse.StatusCode,
            periodStart: periodStart,
            periodEnd: periodEnd,
            totalPercent: result.TotalUsedPercent,
            firstPartyPercent: result.FirstPartyUsedPercent,
            apiPercent: result.ApiUsedPercent,
            totalSpendCents: result.TotalSpendCents,
            includedSpendCents: result.IncludedSpendCents,
            limitCents: result.LimitCents,
            apiIncludedUsd: result.ApiIncludedAmountUsd,
            apiUsedUsd: result.ApiUsedAmountUsd,
            apiRemainingUsd: result.ApiRemainingAmountUsd,
            planName: planName);

        return result;
    }

    private static HttpRequestMessage CreateRpcRequest(string endpoint, string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.TryAddWithoutValidation("Connect-Protocol-Version", "1");
        request.Content = new StringContent("{}", Encoding.UTF8, "application/json");
        return request;
    }

    private static DateTime ParseUnixMilliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, out var milliseconds))
            throw new CursorQuotaFetchException("Не удалось определить billing cycle в ответе Cursor.");

        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).LocalDateTime;
    }
}
