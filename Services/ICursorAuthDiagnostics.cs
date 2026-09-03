namespace Quota.Services;

internal interface ICursorAuthDiagnostics
{
    void LogCursorAuthSessionChanged();

    void LogCursorAuthSessionRemoved();

    void LogAccessTokenExpiredRefreshing();
}

internal sealed class NullCursorAuthDiagnostics : ICursorAuthDiagnostics
{
    public static NullCursorAuthDiagnostics Instance { get; } = new();

    public void LogCursorAuthSessionChanged() { }

    public void LogCursorAuthSessionRemoved() { }

    public void LogAccessTokenExpiredRefreshing() { }
}

internal sealed class QuotaDiagnosticLoggerAuthDiagnostics : ICursorAuthDiagnostics
{
    private readonly QuotaDiagnosticLogger _logger;

    public QuotaDiagnosticLoggerAuthDiagnostics(QuotaDiagnosticLogger logger)
    {
        _logger = logger;
    }

    public void LogCursorAuthSessionChanged() => _logger.LogCursorAuthSessionChanged();

    public void LogCursorAuthSessionRemoved() => _logger.LogCursorAuthSessionRemoved();

    public void LogAccessTokenExpiredRefreshing() => _logger.LogAccessTokenExpiredRefreshing();
}
