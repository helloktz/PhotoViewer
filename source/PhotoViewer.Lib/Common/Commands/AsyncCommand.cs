using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Commands;

public sealed partial class AsyncCommand : IPhCommand
{
    private readonly Func<object?, Task> _executeFn;
    private readonly Func<object?, bool> _canExecuteFn;
    private bool _isExecuting;

    public event EventHandler? CanExecuteChanged;
    public bool IsAsync => true;

    public AsyncCommand(Func<object?, Task> execute, Func<object?, bool> canExecute)
    {
        _executeFn = execute;
        _canExecuteFn = canExecute;
    }

    public AsyncCommand(Func<string?, Task> execute, Func<object?, bool> canExecute)
    {
        _executeFn = obj => execute(obj?.ToString());
        _canExecuteFn = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return !_isExecuting && _canExecuteFn.Invoke(parameter);
    }

    public void Execute(object? parameter)
    {
        _ = ExecuteAsync(parameter);
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (_isExecuting) return;

        try
        {
            _isExecuting = true;
            RaiseCanExecuteChanged();
            await _executeFn.Invoke(parameter);
        }
        finally
        {
            _isExecuting = false;
            RaiseCanExecuteChanged();
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (CanExecuteChanged is not null)
        {
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
