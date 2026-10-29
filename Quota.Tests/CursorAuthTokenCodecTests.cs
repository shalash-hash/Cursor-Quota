using System.Security.Cryptography;
using System.Text;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

[CollectionDefinition("cursor-auth-codec", DisableParallelization = true)]
public sealed class CursorAuthCodecCollection;

[Collection("cursor-auth-codec")]
public class CursorAuthTokenCodecTests
{
    [Fact]
    public void TryDecode_Plaintext_ReturnsOriginal()
    {
        const string token = "plain-token";

        Assert.False(CursorAuthTokenCodec.IsEncrypted(token));
        Assert.Equal(token, CursorAuthTokenCodec.TryDecode(token));
    }

    [Fact]
    public void TryDecode_V10RoundTrip_ReturnsPlaintext()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        const string token = "eyJhbGciOiJub25lIn0.eyJleHAiOjE3OTgxODk3MTd9.sig";
        var stored = CursorAuthTokenCodec.EncryptForTests(token, key);
        CursorAuthTokenCodec.OsCryptKeyProvider = () => key;
        try
        {
            Assert.True(CursorAuthTokenCodec.IsEncrypted(stored));
            Assert.Equal(token, CursorAuthTokenCodec.TryDecode(stored));
        }
        finally
        {
            CursorAuthTokenCodec.OsCryptKeyProvider = null;
        }
    }

    [Fact]
    public void TryDecode_WrongKeyOrGarbage_ReturnsNullWithoutThrowing()
    {
        var stored = CursorAuthTokenCodec.EncryptForTests("secret", RandomNumberGenerator.GetBytes(32));
        CursorAuthTokenCodec.OsCryptKeyProvider = () => RandomNumberGenerator.GetBytes(32);
        try
        {
            Assert.Null(CursorAuthTokenCodec.TryDecode(stored));
            Assert.Null(CursorAuthTokenCodec.TryDecode(CursorAuthTokenCodec.Prefix + "%%%"));
            Assert.Null(CursorAuthTokenCodec.TryDecode(CursorAuthTokenCodec.Prefix + Convert.ToBase64String(Encoding.UTF8.GetBytes("nope"))));
        }
        finally
        {
            CursorAuthTokenCodec.OsCryptKeyProvider = null;
        }
    }
}