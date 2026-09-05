using System.IO;
using System.Net.Http;

namespace Quota.Services;

internal static class CursorNetworkFailure
{
    public static bool IsTransportFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return false;

        for (var current = exception; current is not null; current = current.InnerException)
        {
            if (current is HttpRequestException or IOException)
                return true;

            if (current is TaskCanceledException)
                return !cancellationToken.IsCancellationRequested;
        }

        return false;
    }

    public static string Describe(Exception exception)
    {
        var parts = new List<string>();
        for (var current = exception; current is not null; current = current.InnerException)
            parts.Add(current.GetType().Name);

        return parts.Count == 0 ? exception.GetType().Name : string.Join(" > ", parts);
    }
}
