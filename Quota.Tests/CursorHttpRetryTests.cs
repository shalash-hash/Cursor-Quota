using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Data.Sqlite;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class CursorHttpRetryTests
{
    [Fact]
    public async Task ExecuteAsync_NetworkFailure_ResetsOnceAndRetriesSuccessfully()
    {
        var transport = CreateTransportWithSequence(
            new ThrowingHandler(new HttpRequestException("connect failed")),
            new OkHandler());
        var logger = new RecordingDiagnosticLogger();

        var attempts = 0;
        var result = await CursorHttpRetry.ExecuteAsync(
            transport,
            logger,
            async (client, cancellationToken) =>
            {
                attempts++;
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return attempts;
            });

        Assert.Equal(2, result);
        Assert.Equal(1, transport.ResetCount);
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_TRANSPORT_RESET", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_RETRY_START", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_RETRY_SUCCESS", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_NetworkFailureTwice_DoesNotRetryAgain()
    {
        var transport = CreateTransportWithSequence(
            new ThrowingHandler(new HttpRequestException("connect failed")),
            new ThrowingHandler(new HttpRequestException("still blocked")));
        var logger = new RecordingDiagnosticLogger();

        var attempts = 0;
        await Assert.ThrowsAsync<HttpRequestException>(async () =>
        {
            await CursorHttpRetry.ExecuteAsync(
                transport,
                logger,
                async (client, cancellationToken) =>
                {
                    attempts++;
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
                    using var response = await client.SendAsync(request, cancellationToken);
                    response.EnsureSuccessStatusCode();
                    return true;
                });
        });

        Assert.Equal(2, attempts);
        Assert.Equal(1, transport.ResetCount);
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_RETRY_FAILED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_Success_DoesNotResetOrRetry()
    {
        var transport = CreateTransportWithSequence(new OkHandler());
        var logger = new RecordingDiagnosticLogger();

        var attempts = 0;
        await CursorHttpRetry.ExecuteAsync(
            transport,
            logger,
            async (client, cancellationToken) =>
            {
                attempts++;
                using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
                using var response = await client.SendAsync(request, cancellationToken);
                response.EnsureSuccessStatusCode();
                return true;
            });

        Assert.Equal(1, attempts);
        Assert.Equal(0, transport.ResetCount);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("HTTP_RETRY", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ExecuteAsync_LogicalError_DoesNotResetOrRetry()
    {
        var transport = CreateTransportWithSequence(new StatusHandler(HttpStatusCode.Forbidden));
        var logger = new RecordingDiagnosticLogger();

        var attempts = 0;
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await CursorHttpRetry.ExecuteAsync(
                transport,
                logger,
                async (client, cancellationToken) =>
                {
                    attempts++;
                    using var request = new HttpRequestMessage(HttpMethod.Get, "https://example.com/");
                    using var response = await client.SendAsync(request, cancellationToken);
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("logical failure");

                    return true;
                });
        });

        Assert.Equal(1, attempts);
        Assert.Equal(0, transport.ResetCount);
    }

    [Fact]
    public async Task GetUsageAsync_NetworkFailureThenSuccess_RecoversWithoutThirdAttempt()
    {
        var databasePath = CreateAuthDatabase();
        WriteTokens(databasePath, TestJwtFactory.CreateValid("access"), "refresh-token");

        CursorUsageHandler.ResetCounters();
        var transport = CreateTransportWithSequence(
            new ThrowingHandler(new HttpRequestException("dns failure")),
            new CursorUsageHandler());
        var logger = new RecordingDiagnosticLogger();
        var provider = new CursorQuotaUsageProvider(
            transport,
            new CursorAuthService(transport, () => databasePath, NullCursorAuthDiagnostics.Instance),
            logger);

        var usage = await provider.GetUsageAsync();

        Assert.Equal(12.5, usage.FirstPartyUsedPercent, precision: 3);
        Assert.Equal(1, transport.ResetCount);
        Assert.Equal(2, CursorUsageHandler.TotalSendCount);
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_RETRY_SUCCESS", StringComparison.Ordinal));

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task GetUsageAsync_Http403_ResetsTransport()
    {
        var databasePath = CreateAuthDatabase();
        WriteTokens(databasePath, TestJwtFactory.CreateValid("access"), "refresh-token");

        var transport = new CursorHttpTransport(() => new StatusHandler(HttpStatusCode.Forbidden));
        var logger = new RecordingDiagnosticLogger();
        var provider = new CursorQuotaUsageProvider(
            transport,
            new CursorAuthService(transport, () => databasePath, NullCursorAuthDiagnostics.Instance),
            logger);

        var exception = await Assert.ThrowsAsync<CursorQuotaFetchException>(() => provider.GetUsageAsync());

        Assert.Equal(403, exception.HttpStatusCode);
        Assert.Equal(1, transport.ResetCount);
        Assert.Contains(logger.Messages, m => m.Contains("HTTP_TRANSPORT_RESET reason=HTTP_403", StringComparison.Ordinal));
        Assert.DoesNotContain(logger.Messages, m => m.Contains("HTTP_RETRY", StringComparison.Ordinal));

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task GetUsageAsync_ParseError_DoesNotRetry()
    {
        var databasePath = CreateAuthDatabase();
        WriteTokens(databasePath, TestJwtFactory.CreateValid("access"), "refresh-token");

        var handler = new CursorUsageHandler(usageBody: "{ not-json");
        var transport = new CursorHttpTransport(() => handler);
        var logger = new RecordingDiagnosticLogger();
        var provider = new CursorQuotaUsageProvider(
            transport,
            new CursorAuthService(transport, () => databasePath, NullCursorAuthDiagnostics.Instance),
            logger);

        await Assert.ThrowsAnyAsync<Exception>(() => provider.GetUsageAsync());

        Assert.Equal(0, transport.ResetCount);
        Assert.Equal(1, handler.SendCount);
        Assert.DoesNotContain(logger.Messages, m => m.Contains("HTTP_RETRY", StringComparison.Ordinal));

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    [Fact]
    public async Task GetUsageAsync_ExpiredToken_UsesNewTransportAfterResetOnOAuthFailure()
    {
        var databasePath = CreateAuthDatabase();
        var refreshedToken = TestJwtFactory.CreateValid("refreshed");
        WriteTokens(databasePath, TestJwtFactory.CreateExpired("expired"), "refresh-token");

        OAuthFailOnceThenUsageHandler.ResetCounters();
        var oauthState = new OAuthFailOnceState();
        var transport = new CursorHttpTransport(
            () => new OAuthFailOnceThenUsageHandler(refreshedToken, oauthState));
        var logger = new RecordingDiagnosticLogger();
        var authService = new CursorAuthService(transport, () => databasePath, NullCursorAuthDiagnostics.Instance);
        var provider = new CursorQuotaUsageProvider(transport, authService, logger);

        var usage = await provider.GetUsageAsync();

        Assert.Equal(12.5, usage.FirstPartyUsedPercent, precision: 3);
        Assert.Equal(1, transport.ResetCount);
        Assert.Equal(2, OAuthFailOnceThenUsageHandler.TotalOAuthAttempts);
        Assert.Equal(refreshedToken, await authService.GetAccessTokenAsync());

        SqliteConnection.ClearAllPools();
        File.Delete(databasePath);
    }

    private static CursorHttpTransport CreateTransportWithSequence(params HttpMessageHandler[] handlers)
    {
        var queue = new Queue<HttpMessageHandler>(handlers);
        return new CursorHttpTransport(() => queue.Dequeue());
    }

    private static string CreateAuthDatabase()
    {
        var path = Path.Combine(Path.GetTempPath(), $"quota-retry-test-{Guid.NewGuid():N}.vscdb");
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

    private sealed class RecordingDiagnosticLogger : QuotaDiagnosticLogger
    {
        public List<string> Messages { get; } = [];

        public override void LogHttpTransportReset(string reason) =>
            Messages.Add($"HTTP_TRANSPORT_RESET reason={reason}");

        public override void LogHttpRetryStart() => Messages.Add("HTTP_RETRY_START");

        public override void LogHttpRetrySuccess() => Messages.Add("HTTP_RETRY_SUCCESS");

        public override void LogHttpRetryFailed(string error) =>
            Messages.Add($"HTTP_RETRY_FAILED error={error}");

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

    private sealed class ThrowingHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }

    private sealed class OkHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode));
    }

    private sealed class CursorUsageHandler : HttpMessageHandler
    {
        private readonly string _usageBody;
        public int SendCount { get; private set; }
        public static int TotalSendCount { get; private set; }

        public static void ResetCounters() => TotalSendCount = 0;

        public CursorUsageHandler(string? usageBody = null)
        {
            _usageBody = usageBody ?? """
                {
                  "billingCycleStart": "1722928422000",
                  "billingCycleEnd": "1725606822000",
                  "planUsage": {
                    "totalSpend": 10000,
                    "includedSpend": 2000,
                    "limit": 2000,
                    "autoPercentUsed": 12.5,
                    "apiPercentUsed": 4.0
                  }
                }
                """;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            SendCount++;
            TotalSendCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_usageBody, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class OAuthFailOnceState
    {
        public int FailedAttempts;
    }

    private sealed class OAuthFailOnceThenUsageHandler : HttpMessageHandler
    {
        private readonly string _accessToken;
        private readonly OAuthFailOnceState _state;
        public static int TotalOAuthAttempts { get; private set; }

        public static void ResetCounters() => TotalOAuthAttempts = 0;

        public OAuthFailOnceThenUsageHandler(string accessToken, OAuthFailOnceState state)
        {
            _accessToken = accessToken;
            _state = state;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("oauth/token", StringComparison.OrdinalIgnoreCase) == true)
            {
                TotalOAuthAttempts++;
                if (Interlocked.Increment(ref _state.FailedAttempts) == 1)
                    throw new HttpRequestException("oauth blocked");

                var body = $"{{\"access_token\":\"{_accessToken}\"}}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                });
            }

            var usage = """
                {
                  "billingCycleStart": "1722928422000",
                  "billingCycleEnd": "1725606822000",
                  "planUsage": {
                    "totalSpend": 10000,
                    "includedSpend": 2000,
                    "limit": 2000,
                    "autoPercentUsed": 12.5,
                    "apiPercentUsed": 4.0
                  }
                }
                """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(usage, Encoding.UTF8, "application/json")
            });
        }
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
