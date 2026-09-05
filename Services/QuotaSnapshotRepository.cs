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
                limit_cents,
                bonus_spend_cents,
                remaining_bonus,
                models_base_limit_cents,
                bonus_source
            ) VALUES (
                $retrievedAt,
                $periodStart,
                $periodEnd,
                $totalPercent,
                $firstPartyPercent,
                $apiPercent,
                $totalSpendCents,
                $includedSpendCents,
                $limitCents,
                $bonusSpendCents,
                $remainingBonus,
                $modelsBaseLimitCents,
                $bonusSource
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
        command.Parameters.AddWithValue("$bonusSpendCents", (object?)usage.BonusSpendCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$remainingBonus", usage.RemainingBonus is bool rb ? rb : DBNull.Value);
        command.Parameters.AddWithValue("$modelsBaseLimitCents", (object?)usage.ModelsBaseLimitCents ?? DBNull.Value);
        command.Parameters.AddWithValue("$bonusSource", usage.BonusSource == BonusSource.None ? DBNull.Value : (int)usage.BonusSource);

        await command.ExecuteNonQueryAsync();
    }

    public async Task<QuotaUsage> EnrichWithBonusBaselineAsync(QuotaUsage raw)
    {
        await EnsureInitializedAsync();

        var baseLimitCents = await ModelsBaseLimitResolver.ResolveBaseLimitCentsAsync(this, raw);
        return QuotaUsageEnricher.ApplyBonusBreakdown(
            raw,
            baseLimitCents,
            raw.BonusSource);
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

        var todaySpend = QuotaSpendResolver.ComputeSummedDaySpendUsd(
            priorSnapshots,
            currentSnapshot);

        var yesterdaySpend = await GetYesterdaySpendAsync(current, yesterdayStart);

        return CopyUsage(
            current,
            QuotaMonetaryHelper.ResolveCombinedTodayPercentFromParts(
                ToCents(todaySpend.CombinedUsd),
                ToCents(todaySpend.ModelsUsd),
                ToCents(todaySpend.ApiUsd),
                today.FirstParty,
                today.Api,
                current.ModelsEstimatedLimitUsd,
                current.ApiIncludedAmountUsd),
            today.FirstParty,
            today.Api,
            ToCents(todaySpend.CombinedUsd),
            ToCents(todaySpend.ModelsUsd),
            ToCents(todaySpend.ApiUsd),
            await GetYesterdayUsageAsync(current, yesterdayStart),
            yesterdaySpend);
    }

    private static long ToCents(decimal usd) =>
        (long)Math.Round(usd * 100m, MidpointRounding.AwayFromZero);

    private async Task<DaySpendUsd?> GetYesterdaySpendAsync(QuotaUsage current, DateTime yesterdayStart)
    {
        var snapshots = await GetSnapshotsForDayAsync(
            yesterdayStart,
            current.PeriodStart,
            current.PeriodEnd);

        if (snapshots.Count < 2)
            return null;

        var prior = snapshots.Take(snapshots.Count - 1).ToList();
        var spend = QuotaSpendResolver.ComputeSummedDaySpendUsd(prior, snapshots[^1]);
        if (spend.CombinedUsd <= 0m)
            return null;

        return spend;
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
                limit_cents,
                bonus_spend_cents,
                remaining_bonus,
                models_base_limit_cents,
                bonus_source
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
            snapshots.Add(ReadQuotaSnapshot(reader));
        }

        return snapshots;
    }

    public async Task<long?> GetFrozenModelsBaseLimitCentsAsync(DateTime periodStart, DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT frozen_models_base_limit_cents
            FROM billing_cycle_state
            WHERE period_start = $periodStart
              AND period_end = $periodEnd
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));

        var result = await command.ExecuteScalarAsync();
        return result is long value && value > 0 ? value : null;
    }

    public async Task UpsertFrozenModelsBaseLimitCentsAsync(
        DateTime periodStart,
        DateTime periodEnd,
        long frozenModelsBaseLimitCents)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO billing_cycle_state (
                period_start,
                period_end,
                frozen_models_base_limit_cents,
                updated_at
            ) VALUES (
                $periodStart,
                $periodEnd,
                $frozenLimit,
                $updatedAt
            )
            ON CONFLICT(period_start, period_end) DO UPDATE SET
                frozen_models_base_limit_cents = $frozenLimit,
                updated_at = $updatedAt;
            """;
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));
        command.Parameters.AddWithValue("$frozenLimit", frozenModelsBaseLimitCents);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.Now.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<long?> RecoverModelsBaseLimitCentsFromHistoryAsync(
        DateTime periodStart,
        DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT total_spend_cents, first_party_percent, models_base_limit_cents
            FROM quota_snapshots
            WHERE period_start = $periodStart
              AND period_end = $periodEnd
              AND first_party_percent < 99.999
              AND total_spend_cents > 0
            ORDER BY retrieved_at DESC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));

        await using var reader = await command.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(2))
            {
                var stored = reader.GetInt64(2);
                if (stored > 0)
                    return stored;
            }

            if (!reader.IsDBNull(0) && !reader.IsDBNull(1))
            {
                var spend = reader.GetInt64(0);
                var percent = reader.GetDouble(1);
                return QuotaMonetaryHelper.EstimateLimitCents(spend, percent);
            }
        }

        return null;
    }

    public async Task<long?> GetFirstModels100SpendCentsAsync(DateTime periodStart, DateTime periodEnd)
    {
        await EnsureInitializedAsync();

        await using var connection = new SqliteConnection($"Data Source={_databasePath}");
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT total_spend_cents
            FROM quota_snapshots
            WHERE period_start = $periodStart
              AND period_end = $periodEnd
              AND first_party_percent >= 99.999
              AND total_spend_cents > 0
            ORDER BY retrieved_at ASC
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$periodStart", FormatPeriodParameter(periodStart));
        command.Parameters.AddWithValue("$periodEnd", FormatPeriodParameter(periodEnd));

        var result = await command.ExecuteScalarAsync();
        return result is long value && value > 0 ? value : null;
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
        long todayTotalSpendCents,
        long todayModelsSpendCents,
        long todayApiSpendCents,
        PoolDaySpent? yesterday,
        DaySpendUsd? yesterdaySpend)
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
            PeriodEndUnixMilliseconds = source.PeriodEndUnixMilliseconds,
            RetrievedAt = source.RetrievedAt,
            PlanName = source.PlanName,
            ApiIncludedAmountUsd = source.ApiIncludedAmountUsd,
            ApiUsedAmountUsd = source.ApiUsedAmountUsd,
            ApiRemainingAmountUsd = source.ApiRemainingAmountUsd,
            TotalSpendCents = source.TotalSpendCents,
            IncludedSpendCents = source.IncludedSpendCents,
            LimitCents = source.LimitCents,
            AutoSpendCents = source.AutoSpendCents,
            ApiSpendCents = source.ApiSpendCents,
            AutoLimitCents = source.AutoLimitCents,
            ApiLimitCents = source.ApiLimitCents,
            ModelsActualUsedUsd = source.ModelsActualUsedUsd,
            ModelsUsedUsd = source.ModelsUsedUsd,
            ModelsEstimatedLimitUsd = source.ModelsEstimatedLimitUsd,
            ModelsEstimatedRemainingUsd = source.ModelsEstimatedRemainingUsd,
            TodayTotalSpendCents = todayTotalSpendCents > 0 ? todayTotalSpendCents : null,
            TodayModelsSpendCents = todayModelsSpendCents > 0 ? todayModelsSpendCents : null,
            TodayApiSpendCents = todayApiSpendCents > 0 ? todayApiSpendCents : null,
            YesterdayTotalSpendCents = yesterdaySpend is { CombinedUsd: > 0 } yTotal
                ? ToCents(yTotal.CombinedUsd)
                : null,
            YesterdayModelsSpendCents = yesterdaySpend is { ModelsUsd: > 0 } yModels
                ? ToCents(yModels.ModelsUsd)
                : null,
            YesterdayApiSpendCents = yesterdaySpend is { ApiUsd: > 0 } yApi
                ? ToCents(yApi.ApiUsd)
                : null,
            HasYesterdaySpendData = yesterdaySpend is not null,
            BonusSpendCents = source.BonusSpendCents,
            RemainingBonus = source.RemainingBonus,
            BonusTooltip = source.BonusTooltip,
            BonusSource = source.BonusSource,
            BonusAvailability = source.BonusAvailability,
            ModelsBaseLimitUsd = source.ModelsBaseLimitUsd,
            ModelsBonusUsedUsd = source.ModelsBonusUsedUsd,
            ModelsBaseLimitCents = source.ModelsBaseLimitCents,
            ApiBonusSource = source.ApiBonusSource,
            ApiBonusUsedUsd = source.ApiBonusUsedUsd,
            ApiKnownBonusAllowanceUsd = source.ApiKnownBonusAllowanceUsd,
            RawTotalPercentUsed = source.RawTotalPercentUsed
        };
    }

    private static QuotaSnapshot ReadQuotaSnapshot(SqliteDataReader reader) =>
        new()
        {
            RetrievedAt = DateTime.Parse(reader.GetString(0), null, DateTimeStyles.RoundtripKind),
            PeriodStart = DateTime.Parse(reader.GetString(1), null, DateTimeStyles.RoundtripKind),
            PeriodEnd = DateTime.Parse(reader.GetString(2), null, DateTimeStyles.RoundtripKind),
            TotalPercent = reader.GetDouble(3),
            FirstPartyPercent = reader.GetDouble(4),
            ApiPercent = reader.GetDouble(5),
            TotalSpendCents = reader.IsDBNull(6) ? null : reader.GetInt64(6),
            IncludedSpendCents = reader.IsDBNull(7) ? null : reader.GetInt64(7),
            LimitCents = reader.IsDBNull(8) ? null : reader.GetInt64(8),
            BonusSpendCents = reader.FieldCount > 9 && !reader.IsDBNull(9) ? reader.GetInt64(9) : null,
            RemainingBonus = reader.FieldCount > 10 && !reader.IsDBNull(10) ? reader.GetBoolean(10) : null,
            ModelsBaseLimitCents = reader.FieldCount > 11 && !reader.IsDBNull(11) ? reader.GetInt64(11) : null,
            BonusSource = reader.FieldCount > 12 && !reader.IsDBNull(12)
                ? (BonusSource)reader.GetInt32(12)
                : BonusSource.None
        };

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
                limit_cents,
                bonus_spend_cents,
                remaining_bonus,
                models_base_limit_cents,
                bonus_source
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

        return ReadQuotaSnapshot(reader);
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
        await EnsureColumnAsync(connection, "bonus_spend_cents", "INTEGER");
        await EnsureColumnAsync(connection, "remaining_bonus", "INTEGER");
        await EnsureColumnAsync(connection, "models_base_limit_cents", "INTEGER");
        await EnsureColumnAsync(connection, "bonus_source", "INTEGER");

        await using var cycleCommand = connection.CreateCommand();
        cycleCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS billing_cycle_state (
                period_start TEXT NOT NULL,
                period_end TEXT NOT NULL,
                frozen_models_base_limit_cents INTEGER,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (period_start, period_end)
            );
            """;
        await cycleCommand.ExecuteNonQueryAsync();

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
