using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quota.Models;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class CursorNetworkRecoveryTests
{
    [Fact]
    public async Task EnterRecovery_On403_StartsRecoveryAndResetsTransport()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () => Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage")));

        recovery.EnterRecovery("usage");

        await WaitUntilAsync(() => transport.ResetCount > 0, TimeSpan.FromSeconds(2));

        Assert.True(recovery.IsActive);
        Assert.Contains(logger.Messages, m => m.Contains("NETWORK_RECOVERY_ENTER", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_TRANSPORT_RESET reason=HTTP_403", StringComparison.Ordinal));

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_403ThenSuccess_StopsRecovery()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var attempts = 0;
        QuotaUsage? recovered = null;
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () =>
            {
                attempts++;
                if (attempts < 3)
                {
                    return Task.FromException<QuotaUsage>(
                        new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage"));
                }

                var usage = CreateUsage();
                recovered = usage;
                return Task.FromResult(usage);
            });

        var successTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        recovery.RecoverySucceeded += usage =>
        {
            recovered = usage;
            successTcs.TrySetResult();
            return Task.CompletedTask;
        };

        recovery.EnterRecovery("usage");
        await successTcs.Task.WaitAsync(TimeSpan.FromSeconds(3));

        Assert.False(recovery.IsActive);
        Assert.Equal(3, attempts);
        Assert.NotNull(recovered);
        Assert.Contains(logger.Messages, m => m.Contains("NETWORK_RECOVERY_SUCCESS", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("NETWORK_RECOVERY_EXIT", StringComparison.Ordinal));

        await Task.Delay(100);
        Assert.Equal(3, attempts);

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_Repeated403_IncrementsTransportResetEachAttempt()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () => Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage")));

        recovery.EnterRecovery("usage");
        await WaitUntilAsync(() => transport.ResetCount >= 2, TimeSpan.FromSeconds(2));

        Assert.True(transport.ResetCount >= 2);
        Assert.True(recovery.AttemptCount >= 2);

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_ParseError_DoesNotKeepRetryingAfterExit()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var attempts = 0;
        var endedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () =>
            {
                attempts++;
                return Task.FromException<QuotaUsage>(new JsonException("bad json"));
            });

        recovery.RecoveryEnded += () => endedTcs.TrySetResult();
        recovery.EnterRecovery("usage");

        await endedTcs.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(recovery.IsActive);
        Assert.Equal(1, attempts);

        await Task.Delay(100);
        Assert.Equal(1, attempts);

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_WhenActive_SecondEnterIsIgnored()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () => Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage")));

        recovery.EnterRecovery("usage");
        recovery.EnterRecovery("usage");

        await Task.Delay(100);

        Assert.Equal(1, logger.Messages.Count(m => m.Contains("NETWORK_RECOVERY_ENTER", StringComparison.Ordinal)));

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_WhenLockHeld_SkipsConcurrentAttempt()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        await refreshLock.WaitAsync();

        var attempts = 0;
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () =>
            {
                attempts++;
                return Task.FromException<QuotaUsage>(
                    new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage"));
            },
            fastInterval: TimeSpan.FromMilliseconds(20));

        recovery.EnterRecovery("usage");
        await Task.Delay(150);

        Assert.Equal(0, attempts);

        refreshLock.Release();
        await WaitUntilAsync(() => attempts > 0, TimeSpan.FromSeconds(2));

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task Recovery_RequestImmediateAttempt_TriggersAttemptWithoutWaitingFullInterval()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var attempts = 0;
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () =>
            {
                attempts++;
                return Task.FromException<QuotaUsage>(
                    new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage"));
            },
            fastInterval: TimeSpan.FromSeconds(5));

        recovery.EnterRecovery("usage");
        recovery.RequestImmediateAttempt();

        await WaitUntilAsync(() => attempts > 0, TimeSpan.FromSeconds(2));
        Assert.True(attempts > 0);

        recovery.Dispose();
        refreshLock.Dispose();
    }

    [Fact]
    public async Task AuthTokenHttp403_ResetsTransport()
    {
        var transport = new CursorHttpTransport(() => new StatusHandler(HttpStatusCode.Forbidden));
        var logger = new RecordingRecoveryLogger();
        var databasePath = CreateAuthDatabase();
        WriteTokens(databasePath, TestJwtFactory.CreateExpired("expired"), "refresh-token");

        var authService = new CursorAuthService(
            transport,
            () => databasePath,
            new RecordingAuthDiagnostics(logger));

        await Assert.ThrowsAsync<CursorAuthException>(() => authService.GetAccessTokenAsync());

        Assert.Equal(1, transport.ResetCount);
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_TRANSPORT_RESET reason=HTTP_403", StringComparison.Ordinal));

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public void Dispose_StopsRecoveryLoop()
    {
        var transport = new CursorHttpTransport();
        var logger = new RecordingRecoveryLogger();
        var refreshLock = new SemaphoreSlim(1, 1);
        var recovery = CreateRecovery(
            transport,
            logger,
            refreshLock,
            () => Task.FromException<QuotaUsage>(
                new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage")));

        recovery.EnterRecovery("usage");
        recovery.Dispose();

        Assert.False(recovery.IsActive);
        refreshLock.Dispose();
    }

    [Fact]
    public async Task RefreshScheduler_WhenPaused_DoesNotInvokeRefresh()
    {
        var invocations = 0;
        var scheduler = new QuotaRefreshScheduler(_ =>
        {
            invocations++;
            return Task.CompletedTask;
        }, TimeSpan.FromMilliseconds(30));

        scheduler.Pause();
        await Task.Delay(120);
        scheduler.Resume();
        scheduler.Dispose();

        Assert.Equal(0, invocations);
    }

    private static CursorNetworkRecoveryService CreateRecovery(
        CursorHttpTransport transport,
        RecordingRecoveryLogger logger,
        SemaphoreSlim refreshLock,
        Func<Task<QuotaUsage>> fetch,
        TimeSpan? fastInterval = null) =>
        new(
            transport,
            logger,
            fetch,
            refreshLock,
            fastInterval ?? TimeSpan.FromMilliseconds(30),
            TimeSpan.FromMilliseconds(100),
            TimeSpan.FromMilliseconds(80));

    private static QuotaUsage CreateUsage() =>
        new()
        {
            PeriodStart = new DateTime(2026, 8, 6, 12, 0, 0),
            PeriodEnd = new DateTime(2026, 9, 6, 12, 0, 0),
            RetrievedAt = DateTime.Now,
            TotalUsedPercent = 25,
            FirstPartyUsedPercent = 25,
            ApiUsedPercent = 0,
            TotalSpendCents = 5000,
            IncludedSpendCents = 2000,
            LimitCents = 2000
        };

    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
                return;

            await Task.Delay(20);
        }

        throw new TimeoutException("Condition was not met in time.");
    }

    private static string CreateAuthDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quota-recovery-{Guid.NewGuid():N}.vscdb");
        using var connection = new SqliteConnection($"Data Source={path};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT)";
        command.ExecuteNonQuery();
        return path;
    }

    private static void WriteTokens(string databasePath, string accessToken, string refreshToken)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=false");
        connection.Open();

        Upsert(connection, CursorAuthStateReader.AccessTokenKey, accessToken);
        Upsert(connection, CursorAuthStateReader.RefreshTokenKey, refreshToken);
    }

    private static void Upsert(SqliteConnection connection, string key, string value)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ItemTable WHERE key = $key";
        delete.Parameters.AddWithValue("$key", key);
        delete.ExecuteNonQuery();

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($key, $value)";
        insert.Parameters.AddWithValue("$key", key);
        insert.Parameters.AddWithValue("$value", value);
        insert.ExecuteNonQuery();
    }

    private sealed class RecordingRecoveryLogger : QuotaDiagnosticLogger
    {
        public List<string> Messages { get; } = [];

        public override void LogHttpTransportReset(string reason) =>
            Messages.Add($"HTTP_TRANSPORT_RESET reason={reason}");

        public override void LogNetworkRecoveryEnter(string reason, string endpoint) =>
            Messages.Add($"NETWORK_RECOVERY_ENTER reason={reason} endpoint={endpoint}");

        public override void LogNetworkRecoveryAttempt(int number) =>
            Messages.Add($"NETWORK_RECOVERY_ATTEMPT number={number}");

        public override void LogNetworkRecoveryFailed(string reason) =>
            Messages.Add($"NETWORK_RECOVERY_FAILED reason={reason}");

        public override void LogNetworkRecoverySuccess() =>
            Messages.Add("NETWORK_RECOVERY_SUCCESS");

        public override void LogNetworkRecoveryExit() =>
            Messages.Add("NETWORK_RECOVERY_EXIT");

        public override void LogNetworkRecoveryBackoff(TimeSpan interval) =>
            Messages.Add($"NETWORK_RECOVERY_BACKOFF interval={interval.TotalSeconds:0}s");
    }

    private sealed class RecordingAuthDiagnostics(RecordingRecoveryLogger logger) : ICursorAuthDiagnostics
    {
        public void LogCursorAuthSessionChanged() { }

        public void LogCursorAuthSessionRemoved() { }

        public void LogAccessTokenExpiredRefreshing() { }

        public void LogHttpTransportReset(string reason) => logger.LogHttpTransportReset(reason);
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private static class TestJwtFactory
    {
        public static string CreateValid(string marker) =>
            Create(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(), marker);

        public static string CreateExpired(string marker) =>
            Create(DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(), marker);

        private static string Create(long exp, string marker)
        {
            var header = Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
            var payloadJson = $"{{\"exp\":{exp},\"m\":\"{marker}\"}}";
            var payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payloadJson));
            return $"{header}.{payload}.sig";
        }
    }
}
