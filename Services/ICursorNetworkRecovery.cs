using Quota.Models;

namespace Quota.Services;

public interface ICursorNetworkRecovery : IDisposable
{
    bool IsActive { get; }

    int AttemptCount { get; }

    event Func<QuotaUsage, Task>? RecoverySucceeded;

    event Action? RecoveryEnded;

    void EnterRecovery(string endpoint);

    void RequestImmediateAttempt();
}

internal sealed class NoOpCursorNetworkRecovery : ICursorNetworkRecovery
{
    public static NoOpCursorNetworkRecovery Instance { get; } = new();

    public bool IsActive => false;

    public int AttemptCount => 0;

    public event Func<QuotaUsage, Task>? RecoverySucceeded;

    public event Action? RecoveryEnded;

    public void EnterRecovery(string endpoint)
    {
    }

    public void RequestImmediateAttempt()
    {
    }

    public void Dispose()
    {
    }
}
