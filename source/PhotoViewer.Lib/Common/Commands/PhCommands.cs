using System;
using System.Threading.Tasks;

namespace PhotoViewer.Common.Commands;

public static class PhCommands
{
    public static IPhCommand Create(Action? execute)
    {
        return new SyncCommand(WrapAction(execute), CanExecuteTrue);
    }

    public static IPhCommand Create(Action<object?>? execute)
    {
        return new SyncCommand(execute ?? DefaultExecute, CanExecuteTrue);
    }

    public static IPhCommand Create(Action<string?>? execute)
    {
        return new SyncCommand(execute ?? DefaultExecute, CanExecuteTrue);
    }

    public static IPhCommand Create(Action? execute, Func<bool>? canExecute)
    {
        return new SyncCommand(WrapAction(execute), WrapAction(canExecute));
    }

    public static IPhCommand Create(Action<object?>? execute, Func<object?, bool>? canExecute)
    {
        return new SyncCommand(execute ?? DefaultExecute, canExecute ?? CanExecuteTrue);
    }

    public static IPhCommand Create(Func<Task>? execute)
    {
        return new AsyncCommand(WrapAction(execute), CanExecuteTrue);
    }

    public static IPhCommand Create(Func<object?, Task>? execute)
    {
        return new AsyncCommand(execute ?? DefaultExecuteAsync, CanExecuteTrue);
    }

    public static IPhCommand Create(Func<string?, Task>? execute)
    {
        return new AsyncCommand(execute ?? DefaultExecuteAsync, CanExecuteTrue);
    }

    public static IPhCommand Create(Func<Task>? execute, Func<bool>? canExecute)
    {
        return new AsyncCommand(WrapAction(execute), WrapAction(canExecute));
    }

    public static IPhCommand Create(Func<object?, Task>? execute, Func<object?, bool>? canExecute)
    {
        return new AsyncCommand(execute ?? DefaultExecuteAsync, canExecute ?? CanExecuteTrue);
    }

    private static void DefaultExecute(object? _) { }

    private static Task DefaultExecuteAsync(object? _) => Task.CompletedTask;

    private static bool CanExecuteTrue(object? _) => true;

    private static Func<object?, Task> WrapAction(Func<Task>? action)
    {
        if (action is null)
            return DefaultExecuteAsync;

        return _ => action();
    }

    private static Action<object?> WrapAction(Action? action)
    {
        if (action is null)
            return DefaultExecute;

        return _ => action();
    }

    private static Func<object?, bool> WrapAction(Func<bool>? action)
    {
        if (action is null)
            return CanExecuteTrue;

        return _ => action();
    }
}
