namespace Quota.Services;

public class CursorAuthException : Exception
{
    public int? HttpStatusCode { get; }

    public string? HttpReasonPhrase { get; }

    public CursorAuthException(int? httpStatusCode = null, string? httpReasonPhrase = null)
        : base("Cursor authorization failed")
    {
        HttpStatusCode = httpStatusCode;
        HttpReasonPhrase = httpReasonPhrase;
    }
}
