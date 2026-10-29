using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Quota.Services;

/// <summary>
/// Cursor stores auth tokens in state.vscdb either as plaintext or as
/// Electron safeStorage ciphertext: prefix "cursor-safe-storage:v1:" plus
/// base64(v10 AES-GCM). The AES key lives in Local State, wrapped with DPAPI.
/// </summary>
internal static class CursorAuthTokenCodec
{
    internal const string Prefix = "cursor-safe-storage:v1:";

    private static readonly object KeySync = new();
    private static byte[]? _cachedKey;
    private static string? _cachedKeyMaterial;

    /// <summary>
    /// Test seam. Production leaves this null and reads the OS key.
    /// Tests that set it share the collection cursor-auth-codec so they do not overlap.
    /// </summary>
    internal static Func<byte[]?>? OsCryptKeyProvider { get; set; }

    internal static bool IsEncrypted(string? stored) =>
        stored?.StartsWith(Prefix, StringComparison.Ordinal) == true;

    internal static string? TryDecode(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored) || !IsEncrypted(stored))
            return stored;

        try
        {
            var blob = Convert.FromBase64String(stored[Prefix.Length..]);
            if (blob.Length < 3 + 12 + 16
                || blob[0] != (byte)'v'
                || blob[1] != (byte)'1'
                || blob[2] != (byte)'0')
            {
                return null;
            }

            var key = ResolveOsCryptKey();
            if (key is null || key.Length == 0)
                return null;

            var nonce = blob.AsSpan(3, 12);
            var cipherAndTag = blob.AsSpan(15);
            var tag = cipherAndTag[^16..];
            var cipher = cipherAndTag[..^16];
            var plain = new byte[cipher.Length];
            using var aes = new AesGcm(key, tag.Length);
            aes.Decrypt(nonce, cipher, tag, plain);
            var text = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch (Exception ex) when (ex is FormatException
            or CryptographicException
            or ArgumentException
            or IOException
            or JsonException
            or InvalidOperationException
            or PlatformNotSupportedException)
        {
            return null;
        }
    }

    internal static string EncryptForTests(string plaintext, byte[] key)
    {
        var nonce = RandomNumberGenerator.GetBytes(12);
        var plain = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plain.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, tag.Length);
        aes.Encrypt(nonce, plain, cipher, tag);

        var blob = new byte[3 + nonce.Length + cipher.Length + tag.Length];
        blob[0] = (byte)'v';
        blob[1] = (byte)'1';
        blob[2] = (byte)'0';
        nonce.CopyTo(blob.AsSpan(3));
        cipher.CopyTo(blob.AsSpan(15));
        tag.CopyTo(blob.AsSpan(15 + cipher.Length));
        return Prefix + Convert.ToBase64String(blob);
    }

    private static byte[]? ResolveOsCryptKey()
    {
        if (OsCryptKeyProvider is not null)
            return OsCryptKeyProvider();

        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Cursor",
            "Local State");
        if (!File.Exists(path))
            return null;

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("os_crypt", out var osCrypt)
            || !osCrypt.TryGetProperty("encrypted_key", out var encryptedKeyElement))
        {
            return null;
        }

        var encoded = encryptedKeyElement.GetString();
        if (string.IsNullOrWhiteSpace(encoded))
            return null;

        lock (KeySync)
        {
            if (_cachedKey is not null && string.Equals(_cachedKeyMaterial, encoded, StringComparison.Ordinal))
                return _cachedKey;

            var encrypted = Convert.FromBase64String(encoded);
            ReadOnlySpan<byte> dpapiPrefix = "DPAPI"u8;
            if (!encrypted.AsSpan().StartsWith(dpapiPrefix))
                return null;

            var key = ProtectedData.Unprotect(
                encrypted[dpapiPrefix.Length..],
                null,
                DataProtectionScope.CurrentUser);
            _cachedKey = key;
            _cachedKeyMaterial = encoded;
            return key;
        }
    }
}
