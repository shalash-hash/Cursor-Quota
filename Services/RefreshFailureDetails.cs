namespace Quota.Services;

public sealed class RefreshFailureDetails
{
    public required string UserReason { get; init; }

    public required string ErrorType { get; init; }

    public int? HttpStatus { get; init; }

    public string? EndpointCategory { get; init; }

    public string? TechnicalReason { get; init; }
}
