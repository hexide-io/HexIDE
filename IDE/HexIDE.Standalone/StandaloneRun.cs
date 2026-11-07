using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Standalone;

/// <summary>The made program's fatal-error boundary, shared by startup and subsequent events (#757).</summary>
public sealed class StandaloneRun : IDisposable
{
    private readonly DebugController controller = new() { CanBreak = false };
    private readonly ProjectDefinition project;
    private readonly Action exit;
    private readonly Func<Window, Task> show;
    private readonly Func<string, string> text;

    public bool IsReporting { get; private set; }

    public StandaloneRun(ProjectDefinition project, Func<Window, Task> show, Action exit, Func<string, string> text)
    {
        this.project = project;
        this.show = show;
        this.exit = exit;
        this.text = text;
        controller.HaltHost = async error =>
        {
            await Report(error);
            return false;
        };
        VBWindowContext.RunTimeError += OnRuntimeError;
        VBWindowContext.CompileError += OnCompileError;
        VBWindowContext.Faulted += OnFault;
    }

    public async Task Run(CancellationToken token)
    {
        try
        {
            await VBLoader.RunMadeProgram(project, token, out _, controller);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            await Report(e);
        }
    }

    private void OnRuntimeError(VBWindowContext context, VBRunTimeException e) => OnError(context, e);
    private void OnCompileError(VBWindowContext context, VBCompileErrorException e) => OnError(context, e);
    private void OnFault(VBWindowContext context, Exception e) => OnError(context, e);

    private void OnError(VBWindowContext context, Exception e)
    {
        if (ReferenceEquals(context.Program?.Interpreter.DebugController, controller))
            _ = Report(e);
    }

    private async Task Report(Exception error)
    {
        if (IsReporting)
            return;
        IsReporting = true;
        // Set the abort gate before showing the box: newcomers cannot run, and unwinding fires no Terminate.
        controller.Stop();
        var message = error switch
        {
            VBRunTimeException or VBCompileErrorException => error.Message,
            NotImplementedException => string.Format(text("Str.ProjectRunner.NotImplementedYet"), error.Message),
            _ => new VBRunTimeException(VBStandardError.InternalError).Message
                 + $"\n\n{error.GetType().FullName}: {error.Message}"
        };
        var box = new MessageBox { Text = message, Buttons = MessageBoxButtons.Ok, Icon = MessageBoxIcon.Warning };
        var window = new Window
        {
            Title = project.Name,
            Content = box,
            SizeToContent = SizeToContent.WidthAndHeight,
            CanResize = false
        };
        box.AcceptRequest += _ => window.Close();
        await show(window);
        // The process exit deliberately bypasses Avalonia's window closing and the program's unload lifecycle.
        exit();
    }

    public void Dispose()
    {
        VBWindowContext.RunTimeError -= OnRuntimeError;
        VBWindowContext.CompileError -= OnCompileError;
        VBWindowContext.Faulted -= OnFault;
        controller.Stop();
    }
}
