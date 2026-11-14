using Avalonia.Threading;
using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Commands;

public sealed partial class SyncCommand : IPhCommand
{
    private readonly Action<object?> _executeFn;
    private readonly Func<object?, bool> _canExecuteFn;

    public event EventHandler? CanExecuteChanged;
    public bool IsAsync => false;

    public SyncCommand(Action<object?> execute, Func<object?, bool> canExecute)
    {
        _executeFn = execute;
        _canExecuteFn = canExecute;
    }

    public SyncCommand(Action<string?> execute, Func<object?, bool> canExecute)
    {
        _executeFn = obj => execute(obj?.ToString());
        _canExecuteFn = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecuteFn.Invoke(parameter);
    }

    public void Execute(object? parameter)
    {
        _executeFn.Invoke(parameter);
    }

    public async Task ExecuteAsync(object? parameter)
    {
        await Task.Delay(0);
        Execute(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        if (CanExecuteChanged is not null)
        {
            Dispatcher.UIThread.Post(() => CanExecuteChanged?.Invoke(this, EventArgs.Empty));
        }
    }
}
