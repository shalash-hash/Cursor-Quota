namespace Quota.Services;

internal readonly record struct CursorAuthSnapshot(string? AccessToken, string? RefreshToken)
{
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(AccessToken) && string.IsNullOrWhiteSpace(RefreshToken);
}
