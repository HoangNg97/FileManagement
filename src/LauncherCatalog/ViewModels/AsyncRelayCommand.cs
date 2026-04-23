using System.Windows.Input;

namespace LauncherCatalog.ViewModels;

public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private readonly bool _allowConcurrentExecution;
    private bool _isRunning;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, bool allowConcurrentExecution = false)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute(), allowConcurrentExecution)
    {
    }

    public AsyncRelayCommand(
        Func<object?, Task> execute,
        Predicate<object?>? canExecute = null,
        bool allowConcurrentExecution = false)
    {
        _execute = execute;
        _canExecute = canExecute;
        _allowConcurrentExecution = allowConcurrentExecution;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return (_allowConcurrentExecution || !_isRunning) && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        try
        {
            if (!_allowConcurrentExecution)
            {
                _isRunning = true;
                RaiseCanExecuteChanged();
            }

            await _execute(parameter);
        }
        finally
        {
            if (!_allowConcurrentExecution)
            {
                _isRunning = false;
                RaiseCanExecuteChanged();
            }
        }
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }
}
