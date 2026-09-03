using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Quota.Services;

public class CursorAuthService
{
    private const string OAuthClientId = "KbZUR41cY7W6zRSdpSUJ7I7mLYBKOCmB";
    private const string TokenEndpoint = "https://api2.cursor.sh/oauth/token";

    private readonly HttpClient _httpClient;
    private readonly Func<string> _stateDatabasePathProvider;
    private readonly ICursorAuthDiagnostics _diagnostics;
    private string? _cachedAccessToken;
    private string? _cachedRefreshToken;

    public CursorAuthService(
        HttpClient httpClient,
        QuotaDiagnosticLogger? logger = null,
        Func<string>? stateDatabasePathProvider = null)
        : this(
            httpClient,
            stateDatabasePathProvider
                ?? (() => Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "Cursor",
                    "User",
                    "globalStorage",
                    "state.vscdb")),
            logger is not null
                ? new QuotaDiagnosticLoggerAuthDiagnostics(logger)
                : NullCursorAuthDiagnostics.Instance)
    {
    }

    internal CursorAuthService(
        HttpClient httpClient,
        Func<string> stateDatabasePathProvider,
        ICursorAuthDiagnostics diagnostics)
    {
        _httpClient = httpClient;
        _stateDatabasePathProvider = stateDatabasePathProvider;
        _diagnostics = diagnostics;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await CursorAuthStateReader.ReadSnapshotAsync(
            _stateDatabasePathProvider(),
            cancellationToken);

        if (snapshot.IsEmpty)
        {
            ClearAuthCache();
            _diagnostics.LogCursorAuthSessionRemoved();
            throw new CursorAuthException();
        }

        var accessToken = NormalizeToken(snapshot.AccessToken);
        var refreshToken = NormalizeToken(snapshot.RefreshToken);

        if (HasSessionChanged(accessToken, refreshToken))
        {
            _diagnostics.LogCursorAuthSessionChanged();
            _cachedAccessToken = null;
        }

        _cachedRefreshToken = refreshToken;

        if (accessToken is not null && !IsTokenExpired(accessToken))
        {
            _cachedAccessToken = accessToken;
            return accessToken;
        }

        if (refreshToken is null)
        {
            ClearAuthCache();
            throw new CursorAuthException();
        }

        _diagnostics.LogAccessTokenExpiredRefreshing();
        var refreshedAccessToken = await RefreshAccessTokenAsync(refreshToken, cancellationToken);
        _cachedAccessToken = refreshedAccessToken;
        return refreshedAccessToken;
    }

    internal void ClearAuthCache()
    {
        _cachedAccessToken = null;
        _cachedRefreshToken = null;
    }

    internal string? CachedAccessToken => _cachedAccessToken;

    internal string? CachedRefreshToken => _cachedRefreshToken;

    private bool HasSessionChanged(string? accessToken, string? refreshToken)
    {
        if (_cachedAccessToken is null && _cachedRefreshToken is null)
            return false;

        return !string.Equals(accessToken, _cachedAccessToken, StringComparison.Ordinal)
            || !string.Equals(refreshToken, _cachedRefreshToken, StringComparison.Ordinal);
    }

    private async Task<string> RefreshAccessTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
        request.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                grant_type = "refresh_token",
                client_id = OAuthClientId,
                refresh_token = refreshToken
            }),
            Encoding.UTF8,
            "application/json");

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
            throw new CursorAuthException();

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        if (root.TryGetProperty("shouldLogout", out var shouldLogout) && shouldLogout.GetBoolean())
            throw new CursorAuthException();

        if (!root.TryGetProperty("access_token", out var accessTokenElement))
            throw new CursorAuthException();

        var newAccessToken = accessTokenElement.GetString();
        if (string.IsNullOrWhiteSpace(newAccessToken))
            throw new CursorAuthException();

        return newAccessToken;
    }

    private static string? NormalizeToken(string? token) =>
        string.IsNullOrWhiteSpace(token) ? null : token;

    internal static bool IsTokenExpired(string jwt)
    {
        try
        {
            var parts = jwt.Split('.');
            if (parts.Length < 2)
                return true;

            var payload = parts[1];
            var padding = payload.Length % 4;
            if (padding > 0)
                payload += new string('=', 4 - padding);

            var json = Encoding.UTF8.GetString(Convert.FromBase64String(payload));
            using var document = JsonDocument.Parse(json);

            if (!document.RootElement.TryGetProperty("exp", out var expElement))
                return false;

            var exp = expElement.GetInt64();
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return now >= exp - 60;
        }
        catch
        {
            return false;
        }
    }
}
