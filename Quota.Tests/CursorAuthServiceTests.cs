using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class CursorAuthServiceTests : IDisposable
{
    private readonly string _databasePath;
    private readonly RecordingAuthDiagnostics _diagnostics = new();
    private readonly List<HttpRequestMessage> _httpRequests = [];

    public CursorAuthServiceTests()
    {
        _databasePath = Path.Combine(Path.GetTempPath(), $"quota-auth-test-{Guid.NewGuid():N}.vscdb");
        InitializeDatabase();
    }

    [Fact]
    public async Task GetAccessToken_ValidUnchangedToken_DoesNotRefresh()
    {
        var token = TestJwtFactory.CreateValid("token-a");
        WriteTokens(token, "refresh-a");
        var service = CreateService();

        var first = await service.GetAccessTokenAsync();
        var second = await service.GetAccessTokenAsync();

        Assert.Equal(token, first);
        Assert.Equal(token, second);
        Assert.Empty(_httpRequests);
    }

    [Fact]
    public async Task GetAccessToken_LogoutAfterCachedToken_ClearsCacheAndThrows()
    {
        var token = TestJwtFactory.CreateValid("token-a");
        WriteTokens(token, "refresh-a");
        var service = CreateService();
        _ = await service.GetAccessTokenAsync();

        WriteTokens(null, null);

        await Assert.ThrowsAsync<CursorAuthException>(() => service.GetAccessTokenAsync());
        Assert.Null(service.CachedAccessToken);
        Assert.Null(service.CachedRefreshToken);
        Assert.Contains(_diagnostics.Messages, m => m.Contains("session removed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAccessToken_NewAccessTokenInDatabase_SwitchesToNewToken()
    {
        var tokenA = TestJwtFactory.CreateValid("token-a");
        var tokenB = TestJwtFactory.CreateValid("token-b");
        WriteTokens(tokenA, "refresh-a");
        var service = CreateService();
        Assert.Equal(tokenA, await service.GetAccessTokenAsync());

        WriteTokens(tokenB, "refresh-a");

        Assert.Equal(tokenB, await service.GetAccessTokenAsync());
        Assert.Equal(tokenB, service.CachedAccessToken);
    }

    [Fact]
    public async Task GetAccessToken_RefreshTokenChanged_InvalidatesOldSession()
    {
        var tokenA = TestJwtFactory.CreateValid("account-a");
        var tokenB = TestJwtFactory.CreateValid("account-b");
        WriteTokens(tokenA, "refresh-a");
        var service = CreateService();
        _ = await service.GetAccessTokenAsync();

        WriteTokens(tokenB, "refresh-b");

        Assert.Equal(tokenB, await service.GetAccessTokenAsync());
        Assert.Equal("refresh-b", service.CachedRefreshToken);
        Assert.Contains(_diagnostics.Messages, m => m.Contains("session changed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAccessToken_AccountSwitchWithoutRestart_UsesNewAccountToken()
    {
        var accountA = TestJwtFactory.CreateValid("account-a");
        var accountB = TestJwtFactory.CreateValid("account-b");
        WriteTokens(accountA, "refresh-a");
        var service = CreateService();
        Assert.Equal(accountA, await service.GetAccessTokenAsync());

        WriteTokens(null, null);
        await Assert.ThrowsAsync<CursorAuthException>(() => service.GetAccessTokenAsync());

        WriteTokens(accountB, "refresh-b");
        Assert.Equal(accountB, await service.GetAccessTokenAsync());
    }

    [Fact]
    public async Task GetAccessToken_DatabaseExistsWithTokens_WorksWithoutCursorProcess()
    {
        var token = TestJwtFactory.CreateValid("offline-token");
        WriteTokens(token, "offline-refresh");
        var service = CreateService();

        var result = await service.GetAccessTokenAsync();

        Assert.Equal(token, result);
        Assert.True(File.Exists(_databasePath));
    }

    [Fact]
    public async Task GetAccessToken_ExpiredAccessWithRefresh_RefreshesAccessToken()
    {
        var expired = TestJwtFactory.CreateExpired("expired-token");
        WriteTokens(expired, "refresh-live");
        var refreshed = TestJwtFactory.CreateValid("refreshed-token");
        var service = CreateService(new RefreshingHttpHandler(refreshed, _httpRequests));

        var result = await service.GetAccessTokenAsync();

        Assert.Equal(refreshed, result);
        Assert.Single(_httpRequests);
        Assert.Contains(_diagnostics.Messages, m => m.Contains("expired, refreshing", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task GetAccessToken_ExpiredAccessWithoutLocalRefresh_DoesNotUseStaleRefresh()
    {
        var expired = TestJwtFactory.CreateExpired("expired-token");
        WriteTokens(expired, "refresh-live");
        var service = CreateService(new RefreshingHttpHandler(TestJwtFactory.CreateValid("new"), _httpRequests));
        _ = await service.GetAccessTokenAsync();

        WriteTokens(expired, null);

        await Assert.ThrowsAsync<CursorAuthException>(() => service.GetAccessTokenAsync());
        Assert.Single(_httpRequests);
    }

    [Fact]
    public async Task Diagnostics_DoNotContainTokenValues()
    {
        var token = TestJwtFactory.CreateValid("super-secret-access-token-value");
        var refresh = "super-secret-refresh-token-value";
        WriteTokens(token, refresh);
        var service = CreateService();
        _ = await service.GetAccessTokenAsync();

        WriteTokens(TestJwtFactory.CreateValid("another-access-token"), "another-refresh-token");
        _ = await service.GetAccessTokenAsync();

        foreach (var message in _diagnostics.Messages)
        {
            Assert.DoesNotContain("super-secret-access-token-value", message);
            Assert.DoesNotContain("super-secret-refresh-token-value", message);
            Assert.DoesNotContain("another-access-token", message);
            Assert.DoesNotContain("another-refresh-token", message);
        }
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private CursorAuthService CreateService(HttpMessageHandler? handler = null)
    {
        handler ??= new NoopHttpHandler();
        var transport = new CursorHttpTransport(() => handler);
        return new CursorAuthService(
            transport,
            () => _databasePath,
            _diagnostics);
    }

    private void InitializeDatabase()
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=false");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE ItemTable (key TEXT PRIMARY KEY, value TEXT)";
        command.ExecuteNonQuery();
    }

    private void WriteTokens(string? accessToken, string? refreshToken)
    {
        using var connection = new SqliteConnection($"Data Source={_databasePath};Pooling=false");
        connection.Open();

        UpsertToken(connection, CursorAuthStateReader.AccessTokenKey, accessToken);
        UpsertToken(connection, CursorAuthStateReader.RefreshTokenKey, refreshToken);
    }

    private static void UpsertToken(SqliteConnection connection, string key, string? value)
    {
        using var delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM ItemTable WHERE key = $key";
        delete.Parameters.AddWithValue("$key", key);
        delete.ExecuteNonQuery();

        if (string.IsNullOrWhiteSpace(value))
            return;

        using var insert = connection.CreateCommand();
        insert.CommandText = "INSERT INTO ItemTable (key, value) VALUES ($key, $value)";
        insert.Parameters.AddWithValue("$key", key);
        insert.Parameters.AddWithValue("$value", value);
        insert.ExecuteNonQuery();
    }

    private sealed class RecordingAuthDiagnostics : ICursorAuthDiagnostics
    {
        public List<string> Messages { get; } = [];

        public void LogCursorAuthSessionChanged() => Messages.Add("Cursor auth session changed");

        public void LogCursorAuthSessionRemoved() => Messages.Add("Cursor auth session removed");

        public void LogAccessTokenExpiredRefreshing() => Messages.Add("Access token expired, refreshing");

        public void LogHttpTransportReset(string reason) => Messages.Add($"HTTP_TRANSPORT_RESET reason={reason}");
    }

    private sealed class NoopHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    private sealed class RefreshingHttpHandler : HttpMessageHandler
    {
        private readonly string _accessToken;
        private readonly List<HttpRequestMessage> _requests;

        public RefreshingHttpHandler(string accessToken, List<HttpRequestMessage> requests)
        {
            _accessToken = accessToken;
            _requests = requests;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            _requests.Add(request);
            var body = $"{{\"access_token\":\"{_accessToken}\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            });
        }
    }

    private static class TestJwtFactory
    {
        public static string CreateValid(string marker) =>
            Create(DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds(), marker);

        public static string CreateExpired(string marker) =>
            Create(DateTimeOffset.UtcNow.AddHours(-2).ToUnixTimeSeconds(), marker);

        private static string Create(long expiresAtUnixSeconds, string marker)
        {
            var header = Base64(Encoding.UTF8.GetBytes("{\"alg\":\"none\"}"));
            var payload = Base64(Encoding.UTF8.GetBytes(
                $"{{\"exp\":{expiresAtUnixSeconds},\"marker\":\"{marker}\"}}"));
            return $"{header}.{payload}.signature";
        }

        private static string Base64(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=');
    }
}
