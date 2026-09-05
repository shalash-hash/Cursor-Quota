using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace Quota.Services;

public static class CursorRefreshFailureDescriber
{
    public static RefreshFailureDetails Describe(Exception exception)
    {
        if (exception is CursorQuotaFetchException fetch)
            return DescribeFetchFailure(fetch);

        if (exception is CursorAuthException auth)
            return DescribeAuthFailure(auth);

        if (exception is HttpRequestException httpRequest)
            return DescribeHttpRequest(httpRequest);

        if (exception is TaskCanceledException)
            return TimeoutFailure();

        if (exception is IOException io)
            return IoFailure(io);

        if (exception is JsonException)
            return ParseFailure(exception);

        if (exception.InnerException is not null)
            return Describe(exception.InnerException);

        return new RefreshFailureDetails
        {
            UserReason = exception.GetType().Name,
            ErrorType = exception.GetType().Name,
            TechnicalReason = exception.GetType().Name
        };
    }

    private static RefreshFailureDetails DescribeFetchFailure(CursorQuotaFetchException exception)
    {
        if (exception.HttpStatusCode is int statusCode)
        {
            var phrase = ResolveHttpReasonPhrase(statusCode, exception.HttpReasonPhrase);
            var categorySuffix = FormatEndpointSuffix(exception.EndpointCategory);
            return new RefreshFailureDetails
            {
                UserReason = $"HTTP {statusCode} {phrase}{categorySuffix}",
                ErrorType = nameof(CursorQuotaFetchException),
                HttpStatus = statusCode,
                EndpointCategory = exception.EndpointCategory,
                TechnicalReason = exception.Message
            };
        }

        return new RefreshFailureDetails
        {
            UserReason = "Data error",
            ErrorType = nameof(CursorQuotaFetchException),
            TechnicalReason = exception.Message
        };
    }

    private static RefreshFailureDetails DescribeAuthFailure(CursorAuthException exception)
    {
        if (exception.HttpStatusCode is int statusCode)
        {
            var phrase = ResolveHttpReasonPhrase(statusCode, exception.HttpReasonPhrase);
            return new RefreshFailureDetails
            {
                UserReason = $"HTTP {statusCode} {phrase} (auth)",
                ErrorType = nameof(CursorAuthException),
                HttpStatus = statusCode,
                EndpointCategory = "auth",
                TechnicalReason = "Cursor authorization failed"
            };
        }

        return new RefreshFailureDetails
        {
            UserReason = "Cursor authorization failed",
            ErrorType = nameof(CursorAuthException),
            TechnicalReason = "Cursor authorization failed"
        };
    }

    private static RefreshFailureDetails DescribeHttpRequest(HttpRequestException exception)
    {
        var chain = CursorNetworkFailure.Describe(exception);
        var userReason = exception.InnerException switch
        {
            SocketException socket => $"Network error: HttpRequestException → SocketException ({socket.SocketErrorCode})",
            _ => $"Network error: {chain}"
        };

        return new RefreshFailureDetails
        {
            UserReason = userReason,
            ErrorType = nameof(HttpRequestException),
            TechnicalReason = chain
        };
    }

    private static RefreshFailureDetails TimeoutFailure() =>
        new()
        {
            UserReason = "Request timeout",
            ErrorType = "Timeout",
            TechnicalReason = nameof(TaskCanceledException)
        };

    private static RefreshFailureDetails IoFailure(IOException exception) =>
        new()
        {
            UserReason = $"Network error: {exception.GetType().Name}",
            ErrorType = nameof(IOException),
            TechnicalReason = exception.GetType().Name
        };

    private static RefreshFailureDetails ParseFailure(Exception exception) =>
        new()
        {
            UserReason = "Data parse error",
            ErrorType = exception.GetType().Name,
            TechnicalReason = exception.GetType().Name
        };

    private static string ResolveHttpReasonPhrase(int statusCode, string? reasonPhrase)
    {
        if (!string.IsNullOrWhiteSpace(reasonPhrase))
            return reasonPhrase;

        return Enum.IsDefined(typeof(HttpStatusCode), statusCode)
            ? ((HttpStatusCode)statusCode).ToString()
            : "Unknown";
    }

    private static string FormatEndpointSuffix(string? endpointCategory) =>
        string.IsNullOrWhiteSpace(endpointCategory) ? string.Empty : $" ({endpointCategory})";
}
