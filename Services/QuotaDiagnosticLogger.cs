using System.IO;
using System.Globalization;
using System.Text;
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

    public void LogRawUsage(double totalPercent, double firstPartyPercent, double apiPercent)
    {
        Write(
            $"[{DateTime.Now:HH:mm:ss}] RAW_USAGE " +
            $"total={totalPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"models={firstPartyPercent.ToString("G17", CultureInfo.InvariantCulture)} " +
            $"api={apiPercent.ToString("G17", CultureInfo.InvariantCulture)}");
    }

    public void LogSuccessfulFetch(
        string endpoint,
        int statusCode,
        DateTime periodStart,
        DateTime periodEnd,
        double totalPercent,
        double firstPartyPercent,
        double apiPercent,
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

    public void LogRefreshFailed(RefreshSource source, string error)
    {
        Write($"[{DateTime.Now:HH:mm:ss}] REFRESH_FAILED source={source} error={error}");
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
}
