namespace Quota.Services;

internal static class CursorApiPathFailure
{
    public const int PathFailureStatusCode = 403;

    public static bool IsPathFailure(Exception exception) =>
        exception switch
        {
            CursorQuotaFetchException { HttpStatusCode: PathFailureStatusCode } => true,
            CursorAuthException { HttpStatusCode: PathFailureStatusCode } => true,
            _ => false
        };

    public static string DescribeEndpoint(Exception exception) =>
        exception switch
        {
            CursorQuotaFetchException fetch when !string.IsNullOrWhiteSpace(fetch.EndpointCategory)
                => fetch.EndpointCategory!,
            CursorQuotaFetchException => "usage",
            CursorAuthException => "auth",
            _ => "unknown"
        };
}
