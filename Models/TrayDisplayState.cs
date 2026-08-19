namespace Quota.Models;

public sealed class TrayDisplayState
{
    public TrayDataState DataState { get; init; }

    public string TooltipText { get; init; } = string.Empty;

    public IReadOnlyList<string> InfoMenuLines { get; init; } = Array.Empty<string>();
}
