using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Quota.Services;
using Xunit;

namespace Quota.Tests;

public sealed class CursorRefreshFailureDescriberTests
{
    [Fact]
    public void Describe_Http403_ReturnsReadableStatus()
    {
        var details = CursorRefreshFailureDescriber.Describe(
            new CursorQuotaFetchException("hidden", 403, "Forbidden", "usage"));

        Assert.Equal("HTTP 403 Forbidden (usage)", details.UserReason);
        Assert.Equal(403, details.HttpStatus);
        Assert.Equal(nameof(CursorQuotaFetchException), details.ErrorType);
    }

    [Fact]
    public void Describe_Http401_ReturnsReadableStatus()
    {
        var details = CursorRefreshFailureDescriber.Describe(
            new CursorAuthException(401, "Unauthorized"));

        Assert.Equal("HTTP 401 Unauthorized (auth)", details.UserReason);
        Assert.Equal(401, details.HttpStatus);
    }

    [Fact]
    public void Describe_Http429_ReturnsReadableStatus()
    {
        var details = CursorRefreshFailureDescriber.Describe(
            new CursorQuotaFetchException("hidden", 429, "Too Many Requests", "usage"));

        Assert.Equal("HTTP 429 Too Many Requests (usage)", details.UserReason);
    }

    [Fact]
    public void Describe_Http500_ReturnsReadableStatus()
    {
        var details = CursorRefreshFailureDescriber.Describe(
            new CursorQuotaFetchException("hidden", 500, "Internal Server Error", "usage"));

        Assert.Equal("HTTP 500 Internal Server Error (usage)", details.UserReason);
    }

    [Fact]
    public void Describe_HttpRequestExceptionWithSocket_IncludesSocketError()
    {
        var details = CursorRefreshFailureDescriber.Describe(
            new HttpRequestException(
                "connection failed",
                new SocketException((int)SocketError.HostNotFound)));

        Assert.Contains("HttpRequestException", details.UserReason);
        Assert.Contains("SocketException", details.UserReason);
        Assert.Contains("HostNotFound", details.UserReason);
    }

    [Fact]
    public void Describe_Timeout_ReturnsRequestTimeout()
    {
        var details = CursorRefreshFailureDescriber.Describe(new TaskCanceledException());

        Assert.Equal("Request timeout", details.UserReason);
        Assert.Equal("Timeout", details.ErrorType);
    }

    [Fact]
    public void Describe_JsonException_ReturnsParseError()
    {
        var details = CursorRefreshFailureDescriber.Describe(new JsonException("bad json"));

        Assert.Equal("Data parse error", details.UserReason);
    }

    [Fact]
    public void Describe_DoesNotExposeAuthorizationSecrets()
    {
        var secret = "Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.secret";
        var details = CursorRefreshFailureDescriber.Describe(
            new CursorQuotaFetchException(secret, 403, "Forbidden", "usage"));

        Assert.DoesNotContain("Bearer", details.UserReason);
        Assert.DoesNotContain("eyJ", details.UserReason);
        Assert.Equal("HTTP 403 Forbidden (usage)", details.UserReason);
    }
}
