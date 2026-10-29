namespace Quota.Services;

internal readonly record struct CursorAuthSnapshot(
    string? AccessToken,
    string? RefreshToken,
    bool TokenDecryptFailed = false)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(AccessToken) && string.IsNullOrWhiteSpace(RefreshToken);
}
