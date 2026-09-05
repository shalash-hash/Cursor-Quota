using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Quota.Helpers;
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

    private readonly CursorHttpTransport _transport;
    private readonly CursorAuthService _authService;
    private readonly QuotaDiagnosticLogger _logger;

    public CursorQuotaUsageProvider(
        CursorHttpTransport transport,
        CursorAuthService authService,
        QuotaDiagnosticLogger logger)
    {
        _transport = transport;
        _authService = authService;
        _logger = logger;
    }

    public Task<QuotaUsage> GetUsageAsync() =>
        CursorHttpRetry.ExecuteAsync(
            _transport,
            _logger,
            FetchUsageAsync);

    private async Task<QuotaUsage> FetchUsageAsync(HttpClient httpClient, CancellationToken cancellationToken)
    {
        var accessToken = await _authService.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var usageRequest = CreateRpcRequest(UsageEndpoint, accessToken);
        using var usageResponse = await httpClient.SendAsync(usageRequest, cancellationToken).ConfigureAwait(false);

        var usageBody = await usageResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!usageResponse.IsSuccessStatusCode)
        {
            _logger.LogFetchFailure((int)usageResponse.StatusCode, UsageEndpoint, "usage request failed");
            if ((int)usageResponse.StatusCode == CursorApiPathFailure.PathFailureStatusCode)
            {
                _transport.Reset();
                _logger.LogHttpTransportReset("HTTP_403");
            }

            throw new CursorQuotaFetchException(
                "Не удалось получить данные квоты Cursor.",
                (int)usageResponse.StatusCode,
                usageResponse.ReasonPhrase,
                "usage");
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
        long? planBillingCycleEndRaw = null;

        try
        {
            using var planRequest = CreateRpcRequest(PlanInfoEndpoint, accessToken);
            using var planResponse = await httpClient.SendAsync(planRequest, cancellationToken).ConfigureAwait(false);
            var planBody = await planResponse.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (planResponse.IsSuccessStatusCode)
            {
                var planInfo = JsonSerializer.Deserialize<PlanInfoResponse>(planBody, JsonOptions);
                planName = planInfo?.PlanInfo?.PlanName;
                includedAmountCents ??= planInfo?.PlanInfo?.IncludedAmountCents;
                planBillingCycleEndRaw = TryParseUnixMilliseconds(planInfo?.PlanInfo?.BillingCycleEnd);
            }
        }
        catch (Exception ex) when (CursorNetworkFailure.IsTransportFailure(ex, cancellationToken))
        {
            throw;
        }
        catch
        {
            // Plan info is optional for quota display.
        }

        var usageCycleStartRaw = ParseUnixMillisecondsOrThrow(usage.BillingCycleStart);
        var usageCycleEndRaw = ParseUnixMillisecondsOrThrow(usage.BillingCycleEnd);
        var canonicalEndRaw = usageCycleEndRaw ?? planBillingCycleEndRaw
            ?? throw new CursorQuotaFetchException("Не удалось определить billing cycle в ответе Cursor.");
        var canonicalSource = usageCycleEndRaw is not null
            ? "GetCurrentPeriodUsage.billingCycleEnd"
            : "GetPlanInfo.billingCycleEnd";

        var periodStartOffset = BillingCycleTimestamp.ToDateTimeOffset(usageCycleStartRaw!.Value);
        var periodEndOffset = BillingCycleTimestamp.ToDateTimeOffset(canonicalEndRaw);
        var periodStart = periodStartOffset.LocalDateTime;
        var periodEnd = periodEndOffset.LocalDateTime;

        _logger.LogResetTimeDiagnostic(
            usageCycleStartRaw,
            usageCycleEndRaw,
            planBillingCycleEndRaw,
            canonicalEndRaw,
            canonicalSource,
            periodStartOffset,
            periodEndOffset);

        var result = CursorPlanUsageMapper.Map(
            usage.PlanUsage,
            periodStart,
            periodEnd,
            canonicalEndRaw,
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

    private static long? TryParseUnixMilliseconds(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, out var milliseconds))
            return null;

        return milliseconds;
    }

    private static long? ParseUnixMillisecondsOrThrow(string? value)
    {
        try
        {
            return BillingCycleTimestamp.ParseUnixMilliseconds(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
