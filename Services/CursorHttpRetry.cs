using System.Net.Http;

namespace Quota.Services;

internal static class CursorHttpRetry
{
    public static async Task<T> ExecuteAsync<T>(
        CursorHttpTransport transport,
        QuotaDiagnosticLogger logger,
        Func<HttpClient, CancellationToken, Task<T>> action,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return await action(transport.GetClient(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (CursorNetworkFailure.IsTransportFailure(ex, cancellationToken))
        {
            logger.LogHttpTransportReset(CursorNetworkFailure.Describe(ex));
            transport.Reset();
            logger.LogHttpRetryStart();

            try
            {
                var result = await action(transport.GetClient(), cancellationToken).ConfigureAwait(false);
                logger.LogHttpRetrySuccess();
                return result;
            }
            catch (Exception retryEx)
            {
                logger.LogHttpRetryFailed(CursorNetworkFailure.Describe(retryEx));
                throw;
            }
        }
    }
}
