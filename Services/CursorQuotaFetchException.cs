namespace Quota.Services;

public class CursorQuotaFetchException : Exception
{
    public CursorQuotaFetchException(string message) : base(message)
    {
    }

    public CursorQuotaFetchException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
