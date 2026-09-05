using Quota.Models;

namespace Quota.Services;

public sealed class CursorNetworkRecoveryService : ICursorNetworkRecovery
{
    private readonly CursorHttpTransport _transport;
    private readonly QuotaDiagnosticLogger _logger;
    private readonly Func<Task<QuotaUsage>> _fetchUsage;
    private readonly SemaphoreSlim _refreshLock;
    private readonly TimeSpan _fastInterval;
    private readonly TimeSpan _slowInterval;
    private readonly TimeSpan _fastPhaseDuration;

    private readonly object _stateSync = new();
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private TaskCompletionSource _wakeSignal = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private DateTimeOffset _enteredAt;
    private int _attemptNumber;
    private bool _backoffLogged;

    public CursorNetworkRecoveryService(
        CursorHttpTransport transport,
        QuotaDiagnosticLogger logger,
        Func<Task<QuotaUsage>> fetchUsage,
        SemaphoreSlim refreshLock,
        TimeSpan? fastInterval = null,
        TimeSpan? slowInterval = null,
        TimeSpan? fastPhaseDuration = null)
    {
        _transport = transport;
        _logger = logger;
        _fetchUsage = fetchUsage;
        _refreshLock = refreshLock;
        _fastInterval = fastInterval ?? TimeSpan.FromSeconds(1);
        _slowInterval = slowInterval ?? TimeSpan.FromSeconds(10);
        _fastPhaseDuration = fastPhaseDuration ?? TimeSpan.FromSeconds(30);
    }

    public bool IsActive { get; private set; }

    public int AttemptCount => Volatile.Read(ref _attemptNumber);

    public event Func<QuotaUsage, Task>? RecoverySucceeded;

    public event Action? RecoveryEnded;

    public void EnterRecovery(string endpoint)
    {
        lock (_stateSync)
        {
            if (IsActive)
                return;

            IsActive = true;
            _attemptNumber = 0;
            _backoffLogged = false;
            _enteredAt = DateTimeOffset.UtcNow;
            _wakeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _loopCts = new CancellationTokenSource();
            _loopTask = RunRecoveryLoopAsync(_loopCts.Token);
        }

        _logger.LogNetworkRecoveryEnter("HTTP_403", endpoint);
    }

    public void RequestImmediateAttempt()
    {
        lock (_stateSync)
        {
            _wakeSignal.TrySetResult();
        }
    }

    public void Dispose()
    {
        CancellationTokenSource? cts = null;
        Task? task = null;
        var wasActive = false;

        lock (_stateSync)
        {
            if (_loopCts is null)
                return;

            wasActive = IsActive;
            IsActive = false;
            cts = _loopCts;
            task = _loopTask;
            _loopCts = null;
            _loopTask = null;
        }

        cts.Cancel();
        try
        {
            task?.Wait(TimeSpan.FromSeconds(2));
        }
        catch
        {
            // Best effort shutdown.
        }

        cts.Dispose();

        if (wasActive)
            RecoveryEnded?.Invoke();
    }

    private async Task RunRecoveryLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await WaitForNextAttemptAsync(cancellationToken).ConfigureAwait(false);
                if (cancellationToken.IsCancellationRequested)
                    break;

                await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);

                try
                {
                    var attempt = Interlocked.Increment(ref _attemptNumber);
                    _transport.Reset();
                    _logger.LogHttpTransportReset("HTTP_403");
                    _logger.LogNetworkRecoveryAttempt(attempt);

                    var usage = await _fetchUsage().ConfigureAwait(false);

                    _logger.LogNetworkRecoverySuccess();
                    _logger.LogNetworkRecoveryExit();
                    await NotifySuccessAsync(usage).ConfigureAwait(false);
                    CompleteRecovery();
                    return;
                }
                catch (Exception ex) when (CursorApiPathFailure.IsPathFailure(ex))
                {
                    _logger.LogNetworkRecoveryFailed("HTTP_403");
                }
                catch (Exception ex)
                {
                    _logger.LogNetworkRecoveryFailed(ex.GetType().Name);
                    _logger.LogNetworkRecoveryExit();
                    CompleteRecovery();
                    return;
                }
                finally
                {
                    _refreshLock.Release();
                }

                lock (_stateSync)
                {
                    _wakeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    private async Task WaitForNextAttemptAsync(CancellationToken cancellationToken)
    {
        var elapsed = DateTimeOffset.UtcNow - _enteredAt;
        var interval = elapsed < _fastPhaseDuration ? _fastInterval : _slowInterval;

        if (!_backoffLogged && elapsed >= _fastPhaseDuration)
        {
            _backoffLogged = true;
            _logger.LogNetworkRecoveryBackoff(_slowInterval);
        }

        TaskCompletionSource wake;
        lock (_stateSync)
        {
            wake = _wakeSignal;
        }

        var delayTask = Task.Delay(interval, cancellationToken);
        await Task.WhenAny(delayTask, wake.Task).ConfigureAwait(false);
    }

    private void CompleteRecovery()
    {
        var shouldNotify = false;

        lock (_stateSync)
        {
            shouldNotify = IsActive;
            IsActive = false;
            _loopCts?.Cancel();
            _loopCts = null;
            _loopTask = null;
        }

        if (shouldNotify)
            RecoveryEnded?.Invoke();
    }

    private async Task NotifySuccessAsync(QuotaUsage usage)
    {
        var handler = RecoverySucceeded;
        if (handler is not null)
            await handler(usage).ConfigureAwait(false);
    }
}
