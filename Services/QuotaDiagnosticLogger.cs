using System.IO;
using System.Globalization;
using System.Text;
using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

public class QuotaDiagnosticLogger
{
    private readonly string _logFilePath;
    private readonly object _sync = new();

    public QuotaDiagnosticLogger()
    {
        var logsDirectory = Path.Combine(AppContext.BaseDirectory, "logs");
        Directory.CreateDirectory(logsDirectory);
        _logFilePath = Path.Combine(logsDirectory, "quota.log");
    }

    public void LogRawUsage(
        double? legacyTotalPercent,
        double firstPartyPercent,
        double apiPercent,
        long? totalSpendCents,
        long? includedSpendCents,
        long? limitCents)
    {
        Write(
            $"[{DateTime.Now:HH:mm:ss}] RAW_USAGE " +
            $"legacy_total={legacyTotalPercent?.ToString("G17", CultureInfo.InvariantCulture) ?? "null"} " +
            $"models={firstPartyPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"api={apiPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"total_spend_cents={totalSpendCents?.ToString(CultureInfo.InvariantCulture) ?? "null"} " +
            $"included_spend_cents={includedSpendCents?.ToString(CultureInfo.InvariantCulture) ?? "null"} " +
            $"limit_cents={limitCents?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
    }

    public void LogSuccessfulFetch(
        string endpoint,
        int statusCode,
        DateTime periodStart,
        DateTime periodEnd,
        double totalPercent,
        double firstPartyPercent,
        double apiPercent,
        long? totalSpendCents,
        long? includedSpendCents,
        long? limitCents,
        decimal? apiIncludedUsd,
        decimal? apiUsedUsd,
        decimal? apiRemainingUsd,
        string? planName)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FETCH OK");
        builder.AppendLine($"endpoint={endpoint}");
        builder.AppendLine($"http_status={statusCode}");
        builder.AppendLine($"billing_cycle_start={periodStart:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"billing_cycle_end={periodEnd:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"total_percent={totalPercent.ToString("G17", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"first_party_percent={firstPartyPercent.ToString("G17", CultureInfo.InvariantCulture)}");
        builder.AppendLine($"api_percent={apiPercent.ToString("G17", CultureInfo.InvariantCulture)}");

        if (totalSpendCents is not null)
            builder.AppendLine($"total_spend_cents={totalSpendCents.Value}");

        if (includedSpendCents is not null)
            builder.AppendLine($"included_spend_cents={includedSpendCents.Value}");

        if (limitCents is not null)
            builder.AppendLine($"limit_cents={limitCents.Value}");

        if (apiIncludedUsd is not null)
            builder.AppendLine($"api_included_usd={FormatUsd(apiIncludedUsd.Value)}");

        if (apiUsedUsd is not null)
            builder.AppendLine($"api_used_usd={FormatUsd(apiUsedUsd.Value)}");

        if (apiRemainingUsd is not null)
            builder.AppendLine($"api_remaining_usd={FormatUsd(apiRemainingUsd.Value)}");

        if (!string.IsNullOrWhiteSpace(planName))
            builder.AppendLine($"plan_name={planName}");

        Write(builder.ToString());
    }

    public void LogResetTimeDiagnostic(
        long? usageCycleStartRaw,
        long? usageCycleEndRaw,
        long? planCycleEndRaw,
        long canonicalEndRaw,
        string canonicalSource,
        DateTimeOffset periodStartOffset,
        DateTimeOffset periodEndOffset)
    {
        var nowLocal = DateTimeOffset.Now;
        var remaining = BillingCycleTimestamp.ComputeRemaining(canonicalEndRaw);
        var builder = new StringBuilder();
        builder.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] RESET_TIME_DIAGNOSTIC");
        builder.AppendLine($"billing_cycle_start_raw={usageCycleStartRaw?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        builder.AppendLine($"billing_cycle_end_raw={usageCycleEndRaw?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        builder.AppendLine($"plan_billing_cycle_end_raw={planCycleEndRaw?.ToString(CultureInfo.InvariantCulture) ?? "null"}");
        builder.AppendLine($"canonical_reset_raw={canonicalEndRaw.ToString(CultureInfo.InvariantCulture)}");
        builder.AppendLine($"canonical_reset_source={canonicalSource}");
        builder.AppendLine($"billing_cycle_start_utc={periodStartOffset.UtcDateTime:O}");
        builder.AppendLine($"billing_cycle_end_utc={periodEndOffset.UtcDateTime:O}");
        builder.AppendLine($"billing_cycle_start_local={periodStartOffset.ToLocalTime():O}");
        builder.AppendLine($"billing_cycle_end_local={periodEndOffset.ToLocalTime():O}");
        builder.AppendLine($"now_local={nowLocal:O}");
        builder.AppendLine($"remaining_exact={FormatExactRemaining(remaining)}");
        builder.AppendLine($"timezone_id={TimeZoneInfo.Local.Id}");
        builder.AppendLine($"timezone_offset={TimeZoneInfo.Local.GetUtcOffset(nowLocal):c}");

        Write(builder.ToString());
    }

    public void LogFetchFailure(int statusCode, string endpoint, string reason)
    {
        var message =
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] FETCH FAIL{Environment.NewLine}" +
            $"endpoint={endpoint}{Environment.NewLine}" +
            $"http_status={statusCode}{Environment.NewLine}" +
            $"reason={reason}{Environment.NewLine}";

        Write(message);
    }

    public void LogMissingFields(string endpoint)
    {
        var message =
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] PARSE FAIL{Environment.NewLine}" +
            $"endpoint={endpoint}{Environment.NewLine}" +
            $"reason=missing expected quota fields{Environment.NewLine}";

        Write(message);
    }

    public void LogRefreshStart(RefreshSource source)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] REFRESH_START source={source}");
    }

    public void LogRefreshSuccess(
        RefreshSource source,
        double totalPercent,
        double firstPartyPercent,
        double apiPercent)
    {
        Write(
            $"[{DateTime.Now:HH:mm:ss}] REFRESH_SUCCESS source={source} " +
            $"total={totalPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"models={firstPartyPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"api={apiPercent.ToString("G17", CultureInfo.InvariantCulture)}");
    }

    public void LogRefreshFailed(RefreshSource source, DateTime failureTime, RefreshFailureDetails details)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"[{failureTime:yyyy-MM-dd HH:mm:ss}] REFRESH_FAILED");
        builder.AppendLine($"source={source}");
        builder.AppendLine($"time={failureTime:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"error_type={details.ErrorType}");
        builder.AppendLine($"reason={details.UserReason}");

        if (details.HttpStatus is int httpStatus)
            builder.AppendLine($"http_status={httpStatus}");

        if (!string.IsNullOrWhiteSpace(details.EndpointCategory))
            builder.AppendLine($"endpoint_category={details.EndpointCategory}");

        if (!string.IsNullOrWhiteSpace(details.TechnicalReason)
            && !string.Equals(details.TechnicalReason, details.UserReason, StringComparison.Ordinal))
        {
            builder.AppendLine($"technical_reason={details.TechnicalReason}");
        }

        Write(builder.ToString());
    }

    public virtual void LogHttpTransportReset(string reason)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] HTTP_TRANSPORT_RESET reason={reason}");
    }

    public virtual void LogHttpRetryStart()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] HTTP_RETRY_START");
    }

    public virtual void LogHttpRetrySuccess()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] HTTP_RETRY_SUCCESS");
    }

    public virtual void LogHttpRetryFailed(string error)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] HTTP_RETRY_FAILED error={error}");
    }

    public virtual void LogNetworkRecoveryEnter(string reason, string endpoint)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_ENTER reason={reason} endpoint={endpoint}");
    }

    public virtual void LogNetworkRecoveryAttempt(int number)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_ATTEMPT number={number}");
    }

    public virtual void LogNetworkRecoveryFailed(string reason)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_FAILED reason={reason}");
    }

    public virtual void LogNetworkRecoverySuccess()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_SUCCESS");
    }

    public virtual void LogNetworkRecoveryExit()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_EXIT");
    }

    public virtual void LogNetworkRecoveryBackoff(TimeSpan interval)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] NETWORK_RECOVERY_BACKOFF interval={FormatInterval(interval)}");
    }

    public void LogCursorAuthSessionChanged()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] Cursor auth session changed");
    }

    public void LogCursorAuthSessionRemoved()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] Cursor auth session removed");
    }

    public void LogAccessTokenExpiredRefreshing()
    {
        Write($"[{DateTime.Now:HH:mm:ss}] Access token expired, refreshing");
    }

    private void Write(string message)
    {
        lock (_sync)
        {
            File.AppendAllText(_logFilePath, message + Environment.NewLine, Encoding.UTF8);
        }
    }

    private static string FormatUsd(decimal value) =>
        value.ToString("0.00", CultureInfo.InvariantCulture);

    private static string FormatInterval(TimeSpan interval) =>
        interval.TotalSeconds >= 1
            ? $"{interval.TotalSeconds:0}s"
            : $"{interval.TotalMilliseconds:0}ms";

    private static string FormatExactRemaining(TimeSpan remaining)
    {
        if (remaining < TimeSpan.Zero)
            remaining = TimeSpan.Zero;

        return $"{(int)remaining.TotalHours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";
    }
}
