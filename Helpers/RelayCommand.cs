using System.Windows.Input;

namespace Quota.Helpers;

public class RelayCommand : ICommand
{
    private readonly Func<Task>? _asyncExecute;
    private readonly Action? _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public RelayCommand(Func<Task> asyncExecute, Func<bool>? canExecute = null)
    {
        _asyncExecute = asyncExecute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;

    public void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
            return;

        if (_asyncExecute is not null)
        {
            _ = ExecuteAsync();
            return;
        }

        _execute?.Invoke();
    }

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();

    private async Task ExecuteAsync()
    {
        try
        {
            await _asyncExecute!();
        }
        catch
        {
            // Errors are handled inside the command target.
        }
    }
}
