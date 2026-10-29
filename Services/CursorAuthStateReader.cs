using System.IO;
using Microsoft.Data.Sqlite;

namespace Quota.Services;

internal static class CursorAuthStateReader
{
    internal const string AccessTokenKey = "cursorAuth/accessToken";
    internal const string RefreshTokenKey = "cursorAuth/refreshToken";

    public static async Task<CursorAuthSnapshot> ReadSnapshotAsync(
        string databasePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(databasePath))
            return new CursorAuthSnapshot(null, null);

        await using var connection = new SqliteConnection($"Data Source={databasePath};Mode=ReadOnly;Pooling=false");
        await connection.OpenAsync(cancellationToken);

        var accessToken = await ReadValueAsync(connection, AccessTokenKey, cancellationToken);
        var refreshToken = await ReadValueAsync(connection, RefreshTokenKey, cancellationToken);
        var decodedAccessToken = CursorAuthTokenCodec.TryDecode(accessToken);
        var decodedRefreshToken = CursorAuthTokenCodec.TryDecode(refreshToken);
        var decryptFailed =
            (CursorAuthTokenCodec.IsEncrypted(accessToken) && decodedAccessToken is null)
            || (CursorAuthTokenCodec.IsEncrypted(refreshToken) && decodedRefreshToken is null);
        return new CursorAuthSnapshot(decodedAccessToken, decodedRefreshToken, decryptFailed);
    }

    private static async Task<string?> ReadValueAsync(
        SqliteConnection connection,
        string key,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM ItemTable WHERE key = $key LIMIT 1";
        command.Parameters.AddWithValue("$key", key);

        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result as string;
    }
}
