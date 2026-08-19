using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Quota.Services;

public class CursorAuthService
{
    private const string AccessTokenKey = "cursorAuth/accessToken";
    private const string RefreshTokenKey = "cursorAuth/refreshToken";
    private const string OAuthClientId = "KbZUR41cY7W6zRSdpSUJ7I7mLYBKOCmB";
    private const string TokenEndpoint = "https://api2.cursor.sh/oauth/token";

    private readonly HttpClient _httpClient;
    private string? _cachedAccessToken;

    public CursorAuthService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedAccessToken is not null && !IsTokenExpired(_cachedAccessToken))
            return _cachedAccessToken;

        var accessToken = await ReadValueAsync(AccessTokenKey, cancellationToken);
        var refreshToken = await ReadValueAsync(RefreshTokenKey, cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken) && string.IsNullOrWhiteSpace(refreshToken))
            throw new CursorAuthException();

        if (!string.IsNullOrWhiteSpace(accessToken) && !IsTokenExpired(accessToken))
        {
            _cachedAccessToken = accessToken;
            return accessToken;
        }

        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new CursorAuthException();

        var refreshed = await RefreshAccessTokenAsync(refreshToken, cancellationToken);
        _cachedAccessToken = refreshed;
        return refreshed;
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

    private static async Task<string?> ReadValueAsync(string key, CancellationToken cancellationToken)
    {
        var dbPath = GetStateDatabasePath();
        if (!File.Exists(dbPath))
            return null;

        await using var connection = new SqliteConnection($"Data Source={dbPath};Mode=ReadOnly");
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }

    private static string GetStateDatabasePath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cursor",
            "User",
            "globalStorage",
            "state.vscdb");
    }

    private static bool IsTokenExpired(string jwt)
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
