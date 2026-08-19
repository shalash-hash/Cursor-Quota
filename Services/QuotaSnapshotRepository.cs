using System.IO;
using Microsoft.Data.Sqlite;
using Quota.Models;

namespace Quota.Services;

public class QuotaSnapshotRepository
{
    private readonly string _databasePath;
    private bool _initialized;

    public QuotaSnapshotRepository()
    {
        var dataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Quota");

        Directory.CreateDirectory(dataDirectory);
        _databasePath = Path.Combine(dataDirectory, "quota.db");
    }

    public async Task SaveSnapshotAsync(QuotaUsage usage)
    {
        await EnsureInitializedAsync();

        if (await IsDuplicateOfLastAsync(usage))
            return;

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO quota_snapshots (
                retrieved_at,
                period_start,
                period_end,
                total_percent,
                first_party_percent,
                api_percent
            ) VALUES (
                $retrievedAt,
                $periodStart,
                $periodEnd,
                $totalPercent,
                $firstPartyPercent,
                $apiPercent
            );
            """;

        command.Parameters.AddWithValue("$retrievedAt", usage.RetrievedAt.ToString("O"));
        command.Parameters.AddWithValue("$periodStart", usage.PeriodStart.ToString("O"));
        command.Parameters.AddWithValue("$periodEnd", usage.PeriodEnd.ToString("O"));
        command.Parameters.AddWithValue("$totalPercent", usage.TotalUsedPercent);
        command.Parameters.AddWithValue("$firstPartyPercent", usage.FirstPartyUsedPercent);
        command.Parameters.AddWithValue("$apiPercent", usage.ApiUsedPercent);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<QuotaUsage> EnrichWithTodayUsageAsync(QuotaUsage current)
    {
        await SaveSnapshotAsync(current);

        var baseline = await GetFirstSnapshotOfDayAsync(
            DateTime.Today,
            current.PeriodStart,
            current.PeriodEnd);

        if (baseline is null)
        {
            return CopyUsage(current, 0, 0, 0);
        }

        return CopyUsage(
            current,
            PositiveDelta(current.TotalUsedPercent, baseline.TotalPercent),
            PositiveDelta(current.FirstPartyUsedPercent, baseline.FirstPartyPercent),
            PositiveDelta(current.ApiUsedPercent, baseline.ApiPercent));
    }

    private static QuotaUsage CopyUsage(
        QuotaUsage source,
        double todayTotal,
        double todayFirstParty,
        double todayApi)
    {
        return new QuotaUsage
        {
            TotalUsedPercent = source.TotalUsedPercent,
            FirstPartyUsedPercent = source.FirstPartyUsedPercent,
            ApiUsedPercent = source.ApiUsedPercent,
            TodayTotalUsedPercent = todayTotal,
            TodayFirstPartyUsedPercent = todayFirstParty,
            TodayApiUsedPercent = todayApi,
            PeriodStart = source.PeriodStart,
            PeriodEnd = source.PeriodEnd,
            RetrievedAt = source.RetrievedAt,
            PlanName = source.PlanName,
            ApiIncludedAmountUsd = source.ApiIncludedAmountUsd,
            ApiUsedAmountUsd = source.ApiUsedAmountUsd,
            ApiRemainingAmountUsd = source.ApiRemainingAmountUsd
        };
    }

    private async Task<SnapshotRecord?> GetFirstSnapshotOfDayAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        var dayStart = day.Date;
        var dayEnd = dayStart.AddDays(1);

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT total_percent, first_party_percent, api_percent
            FROM quota_snapshots
            WHERE retrieved_at >= $dayStart
              AND retrieved_at < $dayEnd
              AND period_start = $periodStart
              AND period_end = $periodEnd
            ORDER BY retrieved_at ASC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$dayStart", dayStart.ToString("O"));
        command.Parameters.AddWithValue("$dayEnd", dayEnd.ToString("O"));
        command.Parameters.AddWithValue("$periodStart", periodStart.ToString("O"));
        command.Parameters.AddWithValue("$periodEnd", periodEnd.ToString("O"));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new SnapshotRecord(
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2));
    }

    private async Task<bool> IsDuplicateOfLastAsync(QuotaUsage usage)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT total_percent, first_party_percent, api_percent
            FROM quota_snapshots
            WHERE period_start = $periodStart
              AND period_end = $periodEnd
            ORDER BY id DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$periodStart", usage.PeriodStart.ToString("O"));
        command.Parameters.AddWithValue("$periodEnd", usage.PeriodEnd.ToString("O"));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return false;

        const double tolerance = 0.001;
        return Math.Abs(reader.GetDouble(0) - usage.TotalUsedPercent) < tolerance
            && Math.Abs(reader.GetDouble(1) - usage.FirstPartyUsedPercent) < tolerance
            && Math.Abs(reader.GetDouble(2) - usage.ApiUsedPercent) < tolerance;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized)
            return;

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS quota_snapshots (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                retrieved_at TEXT NOT NULL,
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                total_percent REAL NOT NULL,
                first_party_percent REAL NOT NULL,
                api_percent REAL NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_quota_snapshots_day_period
            ON quota_snapshots (retrieved_at, period_start, period_end);
            """;

        await command.ExecuteNonQueryAsync();
        _initialized = true;
    }

    private static double PositiveDelta(double current, double baseline) =>
        Math.Max(0, current - baseline);

    private sealed record SnapshotRecord(
        double TotalPercent,
        double FirstPartyPercent,
        double ApiPercent);
}
