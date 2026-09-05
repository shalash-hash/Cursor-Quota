using Quota.Models;

namespace Quota.Services;

public sealed class QuotaRefreshScheduler : IDisposable
{
    private readonly System.Windows.Threading.DispatcherTimer _timer;
    private bool _isDisposed;
    private bool _isPaused;

    public QuotaRefreshScheduler(Func<RefreshSource, Task> refreshAction, TimeSpan interval)
    {
        _timer = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = interval
        };

        _timer.Tick += async (_, _) =>
        {
            if (_isPaused)
                return;

            try
            {
                await refreshAction(RefreshSource.Timer);
            }
            catch
            {
                // Errors are handled inside refresh logic.
            }
        };

        _timer.Start();
    }

    public void Pause()
    {
        _isPaused = true;
    }

    public void Resume()
    {
        _isPaused = false;
    }

    public void Dispose()
    {
        if (_isDisposed)
            return;

        _timer.Stop();
        _isDisposed = true;
    }
}
