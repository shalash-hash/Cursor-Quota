namespace Quota.Services;

public class CursorQuotaFetchException : Exception
{
    public int? HttpStatusCode { get; }

    public string? HttpReasonPhrase { get; }

    public string? EndpointCategory { get; }

    public CursorQuotaFetchException(
        string message,
        int? httpStatusCode = null,
        string? httpReasonPhrase = null,
        string? endpointCategory = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        HttpStatusCode = httpStatusCode;
        HttpReasonPhrase = httpReasonPhrase;
        EndpointCategory = endpointCategory;
    }

    public CursorQuotaFetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
