using System;
using System.Windows.Input;
using System.Threading.Tasks;

namespace HexIDE.Utils;

public class DelegateCommand<T> : ICommand
{
    private readonly Action<T> command;
    private readonly Func<T, bool>? canExecute;

    public DelegateCommand(Action<T> command, Func<T, bool>? canExecute)
    {
        this.command = command;
        this.canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (canExecute == null)
            return true;
        if (parameter is T t)
            return canExecute(t);
        return false;
    }

    public void Execute(object? parameter)
    {
        if (parameter is T t)
            command(t);
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecutedChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}

internal interface IRunningCommand
{
    Task? ExecutionTask { get; }
}

public class DelegateCommand : ICommand, IRunningCommand
{
    private readonly Action? command;
    private readonly Func<Task>? asyncCommand;
    private readonly Func<bool>? canExecute;

    internal Task? ExecutionTask { get; private set; }
    Task? IRunningCommand.ExecutionTask => ExecutionTask;

    public DelegateCommand(Action command, Func<bool>? canExecute = null)
    {
        this.command = command;
        this.canExecute = canExecute;
    }

    internal DelegateCommand(Func<Task> command, Func<bool>? canExecute = null)
    {
        asyncCommand = command;
        this.canExecute = canExecute;
    }

    public bool CanExecute(object? parameter)
    {
        if (canExecute == null)
            return true;
        return canExecute();
    }

    public void Execute(object? parameter)
    {
        if (asyncCommand is not null)
        {
            ExecutionTask = asyncCommand();
            ExecutionTask.ListenErrors();
        }
        else
            command!();
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecutedChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}