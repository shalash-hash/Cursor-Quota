using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using Quota.Helpers;
using Quota.Models;

namespace Quota.Services;

public class QuotaSnapshotRepository
{
    private readonly string _databasePath;
    private bool _initialized;

    public QuotaSnapshotRepository()
        : this(null)
    {
    }

    internal QuotaSnapshotRepository(string? databasePathOverride)
    {
        if (databasePathOverride is not null)
        {
            var directory = Path.GetDirectoryName(databasePathOverride)!;
            Directory.CreateDirectory(directory);
            _databasePath = databasePathOverride;
            return;
        }

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
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            ) VALUES (
                $retrievedAt,
                $periodStart,
                $periodEnd,
                $totalPercent,
                $firstPartyPercent,
                $apiPercent,
                $totalSpendCents,
                $includedSpendCents,
                $limitCents
            );
            """;

        command.Parameters.AddWithValue("$retrievedAt", usage.RetrievedAt.ToString("O"));
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(usage.PeriodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(usage.PeriodEnd));
        command.Parameters.AddWithValue("$totalPercent", usage.TotalUsedPercent);
        command.Parameters.AddWithValue("$firstPartyPercent", usage.FirstPartyUsedPercent);
        command.Parameters.AddWithValue("$apiPercent", usage.ApiUsedPercent);
        command.Parameters.AddWithValue("$totalSpendCents", (object?)usage.TotalSpendCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$includedSpendCents", (object?)usage.IncludedSpendCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$limitCents", (object?)usage.LimitCents ?? DBNull.Value);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<QuotaUsage> EnrichWithTodayUsageAsync(QuotaUsage current)
    {
        var now = current.RetrievedAt == default ? DateTime.Now : current.RetrievedAt;
        var todayStart = BillingCycleCalendar.GetDayStart(now, current.PeriodStart);
        var yesterdayStart = BillingCycleCalendar.GetPreviousDayStart(now, current.PeriodStart);

        var priorSnapshots = await GetSnapshotsForDayAsync(
            todayStart,
            current.PeriodStart,
            current.PeriodEnd);

        await SaveSnapshotAsync(current);

        var currentSnapshot = ToQuotaSnapshot(ToSnapshotRecord(current));
        var today = QuotaSnapshotAnalytics.ComputeSummedDayUsage(
            priorSnapshots,
            currentSnapshot);

        if (today.Total < 0.001 && priorSnapshots.Count > 0)
        {
            var legacyBaseline = priorSnapshots[0];
            var legacyDelta = ComputeDayDelta(
                ToSnapshotRecordFromQuota(legacyBaseline),
                ToSnapshotRecord(current));
            if (legacyDelta.Total > today.Total)
                today = legacyDelta;
        }

        if (today.Total < 0.001)
        {
            var yesterdayLast = await GetLastSnapshotBeforeDayAsync(
                todayStart,
                current.PeriodStart,
                current.PeriodEnd);
            if (yesterdayLast is not null)
            {
                var carryOver = ComputeDayDelta(
                    ToSnapshotRecordFromQuota(yesterdayLast),
                    ToSnapshotRecord(current));
                if (carryOver.Total > today.Total)
                    today = carryOver;
            }
        }

        var todaySpendCents = QuotaSnapshotAnalytics.ComputeSummedSpendCentsDelta(
            priorSnapshots,
            currentSnapshot);

        var yesterdaySpend = await GetYesterdaySpendCentsAsync(current, yesterdayStart);

        return CopyUsage(
            current,
            QuotaMonetaryHelper.ResolveCombinedTodayPercentFromParts(
                todaySpendCents,
                today.FirstParty,
                today.Api,
                current.ModelsEstimatedLimitUsd,
                current.ApiIncludedAmountUsd),
            today.FirstParty,
            today.Api,
            todaySpendCents,
            await GetYesterdayUsageAsync(current, yesterdayStart),
            yesterdaySpend);
    }

    private async Task<long?> GetYesterdaySpendCentsAsync(QuotaUsage current, DateTime yesterdayStart)
    {
        var snapshots = await GetSnapshotsForDayAsync(
            yesterdayStart,
            current.PeriodStart,
            current.PeriodEnd);

        if (snapshots.Count < 2)
            return null;

        var prior = snapshots.Take(snapshots.Count - 1).ToList();
        var delta = QuotaSnapshotAnalytics.ComputeSummedSpendCentsDelta(prior, snapshots[^1]);
        return delta > 0 ? delta : null;
    }

    public async Task<PoolDaySpent?> GetDaySpentForDateAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        var first = await GetFirstSnapshotOfDayAsync(day, periodStart, periodEnd);
        if (first is null)
            return null;

        var last = await GetLastSnapshotOfDayAsync(day, periodStart, periodEnd) ?? first;

        return ComputeDayDelta(first, last);
    }

    public async Task<IReadOnlyList<QuotaSnapshot>> GetSnapshotsAsync(
        DateTime? fromInclusive,
        DateTime? toExclusive)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                retrieved_at,
                period_start,
                period_end,
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            FROM quota_snapshots
            WHERE ($fromInclusive IS NULL OR retrieved_at >= $fromInclusive)
              AND ($toExclusive IS NULL OR retrieved_at < $toExclusive)
            ORDER BY retrieved_at ASC;
            """;

        command.Parameters.AddWithValue(
            "$fromInclusive",
            fromInclusive is null ? DBNull.Value : fromInclusive.Value.ToString("O"));
        command.Parameters.AddWithValue(
            "$toExclusive",
            toExclusive is null ? DBNull.Value : toExclusive.Value.ToString("O"));

        var snapshots = new List<QuotaSnapshot>();

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            snapshots.Add(new QuotaSnapshot
            {
                RetrievedAt = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind),
                PeriodStart = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
                PeriodEnd = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
                TotalPercent = reader.GetDouble(3),
                FirstPartyPercent = reader.GetDouble(4),
                ApiPercent = reader.GetDouble(5),
                TotalSpendCents = reader.IsDBNull(6) ? null : reader.GetInt64(6),
                IncludedSpendCents = reader.IsDBNull(7) ? null : reader.GetInt64(7),
                LimitCents = reader.IsDBNull(8) ? null : reader.GetInt64(8)
            });
        }

        return snapshots;
    }

    private async Task<PoolDaySpent?> GetYesterdayUsageAsync(QuotaUsage current, DateTime yesterdayStart)
    {
        return await GetDaySpentForDateAsync(
            yesterdayStart,
            current.PeriodStart,
            current.PeriodEnd);
    }

    private static QuotaUsage CopyUsage(
        QuotaUsage source,
        double todayTotal,
        double todayFirstParty,
        double todayApi,
        long todayModelsSpendCents,
        PoolDaySpent? yesterday,
        long? yesterdayModelsSpendCents)
    {
        var yesterdayTotal = yesterday is PoolDaySpent spent
            ? QuotaMonetaryHelper.ResolveCombinedDayPercent(
                spent.FirstParty,
                spent.Api,
                source.ModelsEstimatedLimitUsd,
                source.ApiIncludedAmountUsd)
            : 0;

        return new QuotaUsage
        {
            TotalUsedPercent = source.TotalUsedPercent,
            FirstPartyUsedPercent = source.FirstPartyUsedPercent,
            ApiUsedPercent = source.ApiUsedPercent,
            TodayTotalUsedPercent = todayTotal,
            TodayFirstPartyUsedPercent = todayFirstParty,
            TodayApiUsedPercent = todayApi,
            YesterdayTotalUsedPercent = yesterdayTotal,
            YesterdayFirstPartyUsedPercent = yesterday?.FirstParty ?? 0,
            YesterdayApiUsedPercent = yesterday?.Api ?? 0,
            HasYesterdayUsageData = yesterday is not null,
            PeriodStart = source.PeriodStart,
            PeriodEnd = source.PeriodEnd,
            RetrievedAt = source.RetrievedAt,
            PlanName = source.PlanName,
            ApiIncludedAmountUsd = source.ApiIncludedAmountUsd,
            ApiUsedAmountUsd = source.ApiUsedAmountUsd,
            ApiRemainingAmountUsd = source.ApiRemainingAmountUsd,
            TotalSpendCents = source.TotalSpendCents,
            IncludedSpendCents = source.IncludedSpendCents,
            LimitCents = source.LimitCents,
            ModelsUsedUsd = source.ModelsUsedUsd,
            ModelsEstimatedLimitUsd = source.ModelsEstimatedLimitUsd,
            ModelsEstimatedRemainingUsd = source.ModelsEstimatedRemainingUsd,
            TodayModelsSpendCents = todayModelsSpendCents > 0 ? todayModelsSpendCents : null,
            YesterdayModelsSpendCents = yesterdayModelsSpendCents,
            HasYesterdaySpendData = yesterdayModelsSpendCents is not null
        };
    }

    private static SnapshotRecord ToSnapshotRecord(QuotaUsage usage) =>
        new(
            usage.TotalUsedPercent,
            usage.FirstPartyUsedPercent,
            usage.ApiUsedPercent,
            usage.TotalSpendCents,
            usage.IncludedSpendCents,
            usage.LimitCents);

    private static PoolDaySpent ComputeDayDelta(SnapshotRecord first, SnapshotRecord last) =>
        QuotaSnapshotAnalytics.ComputeDayDelta(ToQuotaSnapshot(first), ToQuotaSnapshot(last));

    private static QuotaSnapshot ToQuotaSnapshot(SnapshotRecord record) =>
        new()
        {
            TotalPercent = record.TotalPercent,
            FirstPartyPercent = record.FirstPartyPercent,
            ApiPercent = record.ApiPercent,
            TotalSpendCents = record.TotalSpendCents,
            IncludedSpendCents = record.IncludedSpendCents,
            LimitCents = record.LimitCents
        };

    private static SnapshotRecord ToSnapshotRecordFromQuota(QuotaSnapshot snapshot) =>
        new(
            snapshot.TotalPercent,
            snapshot.FirstPartyPercent,
            snapshot.ApiPercent,
            snapshot.TotalSpendCents,
            snapshot.IncludedSpendCents,
            snapshot.LimitCents);

    private async Task<IReadOnlyList<QuotaSnapshot>> GetSnapshotsForDayAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd)
    {
        var dayStart = day;
        var dayEnd = dayStart.AddDays(1);
        var all = await GetSnapshotsAsync(dayStart, dayEnd);

        var periodStartKey = FormatPeriodParameter(periodStart);
        var periodEndKey = FormatPeriodParameter(periodEnd);

        return all
            .Where(s =>
                FormatPeriodParameter(s.PeriodStart) == periodStartKey
                && FormatPeriodParameter(s.PeriodEnd) == periodEndKey)
            .ToList();
    }

    private async Task<QuotaSnapshot?> GetLastSnapshotBeforeDayAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        var dayStart = day;

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                retrieved_at,
                period_start,
                period_end,
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            FROM quota_snapshots
            WHERE retrieved_at < $dayStart
              AND period_start = $periodStart
              AND period_end = $periodEnd
            ORDER BY retrieved_at DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$dayStart", dayStart.ToString("O"));
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return new QuotaSnapshot
        {
            RetrievedAt = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind),
            PeriodStart = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
            PeriodEnd = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
            TotalPercent = reader.GetDouble(3),
            FirstPartyPercent = reader.GetDouble(4),
            ApiPercent = reader.GetDouble(5),
            TotalSpendCents = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            IncludedSpendCents = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            LimitCents = reader.IsDBNull(8) ? null : reader.GetInt64(8)
        };
    }

    private async Task<SnapshotRecord?> GetLastSnapshotOfDayAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        var dayStart = day;
        var dayEnd = dayStart.AddDays(1);

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            FROM quota_snapshots
            WHERE retrieved_at >= $dayStart
              AND retrieved_at < $dayEnd
              AND period_start = $periodStart
              AND period_end = $periodEnd
            ORDER BY retrieved_at DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$dayStart", dayStart.ToString("O"));
        command.Parameters.AddWithValue("$dayEnd", dayEnd.ToString("O"));
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return ReadSnapshotRecord(reader);
    }

    private async Task<SnapshotRecord?> GetFirstSnapshotOfDayAsync(
        DateTime day,
        DateTime? periodStart = null,
        DateTime? periodEnd = null)
    {
        await EnsureInitializedAsync();

        var dayStart = day;
        var dayEnd = dayStart.AddDays(1);

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = periodStart is null || periodEnd is null
            ? """
            SELECT
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            FROM quota_snapshots
            WHERE retrieved_at >= $dayStart
              AND retrieved_at < $dayEnd
            ORDER BY retrieved_at ASC
            LIMIT 1;
            """
            : """
            SELECT
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
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
        if (periodStart is not null && periodEnd is not null)
        {
            command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart.Value));
            command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd.Value));
        }

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return null;

        return ReadSnapshotRecord(reader);
    }

    private async Task<SnapshotRecord?> GetFirstSnapshotOfDayAsync(
        DateTime day,
        DateTime periodStart,
        DateTime periodEnd) =>
        await GetFirstSnapshotOfDayAsync(day, (DateTime?)periodStart, (DateTime?)periodEnd);

    private async Task<SnapshotRecord?> GetFirstSnapshotOfDayIgnoringPeriodAsync(DateTime day) =>
        await GetFirstSnapshotOfDayAsync(day);

    private async Task<bool> IsDuplicateOfLastAsync(QuotaUsage usage)
    {
        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT
                total_percent,
                first_party_percent,
                api_percent,
                total_spend_cents,
                included_spend_cents,
                limit_cents
            FROM quota_snapshots
            WHERE period_start = $periodStart
              AND period_end = $periodEnd
            ORDER BY id DESC
            LIMIT 1;
            """;

        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(usage.PeriodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(usage.PeriodEnd));

        await using var reader = await command.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
            return false;

        var last = ReadSnapshotRecord(reader);

        if (usage.TotalSpendCents is long currentSpend
            && last.TotalSpendCents is long lastSpend
            && currentSpend == lastSpend)
        {
            const double tolerance = 0.001;
            return Math.Abs(last.TotalPercent - usage.TotalUsedPercent) < tolerance
                && Math.Abs(last.FirstPartyPercent - usage.FirstPartyUsedPercent) < tolerance
                && Math.Abs(last.ApiPercent - usage.ApiUsedPercent) < tolerance;
        }

        const double percentTolerance = 0.001;
        return Math.Abs(last.TotalPercent - usage.TotalUsedPercent) < percentTolerance
            && Math.Abs(last.FirstPartyPercent - usage.FirstPartyUsedPercent) < percentTolerance
            && Math.Abs(last.ApiPercent - usage.ApiUsedPercent) < percentTolerance;
    }

    private static string FormatPeriodParameter(DateTime value)
    {
        var normalized = value.Kind switch
        {
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local),
            _ => value
        };

        return new DateTimeOffset(normalized).ToString("O");
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

        await EnsureColumnAsync(connection, "total_spend_cents", "INTEGER");
        await EnsureColumnAsync(connection, "included_spend_cents", "INTEGER");
        await EnsureColumnAsync(connection, "limit_cents", "INTEGER");

        _initialized = true;
    }

    private static async Task EnsureColumnAsync(
        SqliteConnection connection,
        string columnName,
        string columnType)
    {
        await using var check = connection.CreateCommand();
        check.CommandText = "PRAGMA table_info(quota_snapshots);";

        await using var reader = await check.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
                return;
        }

        await using var alter = connection.CreateCommand();
        alter.CommandText = $"ALTER TABLE quota_snapshots ADD COLUMN {columnName} {columnType};";
        await alter.ExecuteNonQueryAsync();
    }

    private static SnapshotRecord ReadSnapshotRecord(SqliteDataReader reader) =>
        new(
            reader.GetDouble(0),
            reader.GetDouble(1),
            reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetInt64(3),
            reader.IsDBNull(4) ? null : reader.GetInt64(4),
            reader.IsDBNull(5) ? null : reader.GetInt64(5));

    private sealed record SnapshotRecord(
        double TotalPercent,
        double FirstPartyPercent,
        double ApiPercent,
        long? TotalSpendCents,
        long? IncludedSpendCents,
        long? LimitCents);
}
