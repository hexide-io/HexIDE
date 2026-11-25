using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Dialogs;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Running;

/// <summary>
/// Runs a project with HexIDE's own interpreter and its run-time forms, in the IDE's process. The Basic provider registers it
/// through the host (#5616), with a debugger of its own that is both the IDE's controller and the interpreter's gate.
/// </summary>
public sealed class InterpreterRunEngine : IRunEngine, IDisposable
{
    internal OSPlatform? Host { get; init; }
    private readonly RunEngineServices services;
    private readonly IProgramHost programHost;
    private readonly IDebugGate debugController;
    private Func<RunFailure, Task<bool>>? haltHost;
    // What it installed for the whole runtime, to put back when it's withdrawn (#5616).
    private readonly Func<VbFileDialogRequest, Task<VbFileDialogAnswer?>> fileDialog;
    private readonly Func<Task<bool>> printDialog;
    private readonly Func<int, Task<int?>> colorDialog;
    private readonly Func<VbFontChoice, Task<VbFontChoice?>> fontDialog;
    private readonly Action<VBWindowContext, VBRunTimeException> runTimeError;
    private readonly Action<VBWindowContext, VBCompileErrorException> compileError;

    /// <summary>Makes the engine the provider registers: <paramref name="debugController"/> when a test brings its own.</summary>
    public InterpreterRunEngine(RunEngineServices services, IDebugGate? debugController = null)
    {
        this.services = services;
        programHost = services.Host;
        this.debugController = debugController ?? new DebugController();
        Program = new InterpreterRunningProgram();
        // A running program's CommonDialog shows the IDE's own dialogs (#1084), whichever window the form runs in.
        HostDialogs.File = fileDialog = programHost.FileDialog;
        HostDialogs.Print = printDialog = programHost.PrintDialog;
        HostDialogs.Color = colorDialog = programHost.ColorDialog;
        HostDialogs.Font = fontDialog = programHost.FontDialog;
        // And a form's MsgBox and InputBox are shown in windows the IDE makes, so it knows them as the program's (#5616).
        HostDialogs.BoxWindows = services.Dialogs;

        runTimeError = (_, e) => ErrorMet?.Invoke(new RunFailure(RunErrorText.For(e), ErrorLocation.Of(e)));
        VBWindowContext.RunTimeError += runTimeError;

        // Compile errors met while running halt through the same dialog (#759, #833), with Debug breaking.
        // VB6's Compile On Demand breaks before procedure entry; the walk meets the error at the statement:
        // docs/vb6-fidelity-oracle/a-compile-error-met-while-a-program-runs.md.
        // Without a halt host, or before the run starts, the existing reporting route still applies.
        compileError = (_, e) => ErrorMet?.Invoke(new RunFailure(e.Message, ErrorLocation.Of(e)));
        VBWindowContext.CompileError += compileError;
    }

    /// <summary>
    /// Once it's withdrawn (#5616): the runtime's dialogs and box windows go back to its own, where they're still this
    /// engine's, and it stops hearing every form's errors. Another engine's settings are left alone.
    /// </summary>
    public void Dispose()
    {
        if (ReferenceEquals(HostDialogs.File, fileDialog)) HostDialogs.File = null;
        if (ReferenceEquals(HostDialogs.Print, printDialog)) HostDialogs.Print = null;
        if (ReferenceEquals(HostDialogs.Color, colorDialog)) HostDialogs.Color = null;
        if (ReferenceEquals(HostDialogs.Font, fontDialog)) HostDialogs.Font = null;
        if (ReferenceEquals(HostDialogs.BoxWindows, services.Dialogs)) HostDialogs.BoxWindows = null;
        VBWindowContext.RunTimeError -= runTimeError;
        VBWindowContext.CompileError -= compileError;
    }

    /// <inheritdoc />
    public event Action<RunFailure>? ErrorMet;

    /// <inheritdoc />
    /// <remarks>The debugger halts only while there is someone to ask, so it is left without a host while this has none.</remarks>
    public Func<RunFailure, Task<bool>>? HaltHost
    {
        get => haltHost;
        set
        {
            haltHost = value;
            debugController.HaltHost = value is { } host
                ? error => host(new RunFailure(RunErrorText.For(error), ErrorLocation.Of(error)))
                : null;
            debugController.CompileHaltHost = value is { } compileHost
                ? error => compileHost(new RunFailure(error.Message, ErrorLocation.Of(error)))
                : null;
        }
    }

    /// <inheritdoc />
    /// <remarks>A project whose type doesn't offer this is run by the program it belongs to, never by this interpreter.</remarks>
    public bool Runs(ProjectDefinition project) => project.Offers.Runs;

    /// <inheritdoc />
    public IDebugController Debugger => debugController;

    /// <inheritdoc />
    public IRunningProgram Program { get; }

    /// <inheritdoc />
    public ErrorTrapping ErrorTrapping
    {
        get => debugController.ErrorTrapping;
        set => debugController.ErrorTrapping = value;
    }

    /// <inheritdoc />
    /// <remarks>
    /// A Sub Main project's modules are the ones the form paths see too, so the same project sees the same modules whichever
    /// startup object it has (#220).
    /// </remarks>
    public string? RefusesToStart(ProjectDefinition project)
    {
        var nothingToStart = project.StartsAtSubMain ? VBLoader.InterpreterModules(project).Item1.Count == 0 : project.StartupForm is null;
        return nothingToStart ? new VBCompileErrorException(BasicInterpreter.NoStartupObject).Message : null;
    }

    /// <inheritdoc />
    public RunStart Prepare(ProjectDefinition project)
    {
        // Every distribution gives the user's program the same neutral window icon (#4187).
        VBLoader.DefaultFormIcon = new WindowIcon(AssetLoader.Open(new Uri("avares://HexIDE.Runtime/form.ico")));
        return project.StartsAtSubMain ? PrepareSubMain(project) : PrepareForm(project.StartupForm!);
    }

    private RunStart PrepareForm(FormDefinition form)
    {
        var tokenSource = new CancellationTokenSource();
        var watch = new RunWatch(debugController, tokenSource);
        Task task;
        try
        {
            if (services.SingleView)
            {
                task = RunFormInBrowser(form, tokenSource.Token, out _, debugController);
            }
            else
            {
                // RunForm starts the form and shows it once Load has run (#822).
                task = VBLoader.RunForm(form, tokenSource.Token, out _, debugController, services.RDCoreWin64OnlySetOnWindows, Host);
            }
        }
        catch (Exception e)
        {
            // Building the form parses every module the run loads, so a syntax error in any of them arrives here, naming
            // its module. It was a message box that nothing recorded, so run_project said the program had run and ended
            // (#759).
            watch.Dispose();
            return RunStart.Failed(new RunFailure(DescribeFailedStart(e, form.Name), ErrorLocation.Of(e)));
        }

        return RunStart.Started(new Run(ToEnd, () => tokenSource.Cancel()));

        async Task<RunFailure?> ToEnd()
        {
            try
            {
                await task;
            }
            finally
            {
                watch.Dispose();
            }

            return FaultOf(watch);
        }
    }

    /// <summary>
    /// Build a project whose startup object is <c>Sub Main</c>: a program of every standard module, with the project's forms
    /// as templates that Main can load and show (#822).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every standard module is loaded, because the startup lookup is project-wide: a <c>Sub Main</c> in
    /// any of them is the entry point, including a <c>Private</c> one in a module other than the first
    /// (measured — see <c>corpus/conformance/sub-main-startup.json</c>). The first module is nominated as
    /// primary only so the debug gate has a name to report; which module holds <c>Main</c> is
    /// <see cref="BasicInterpreter.RunStartupSubMain"/>'s business, not this method's.
    /// </para>
    /// <para>
    /// The run lasts until Main has returned and no form is loaded, so a Main that shows a form and returns leaves the
    /// form running (measured). End, a fault in any of its procedures, and the debugger's Stop end it as they end a form's
    /// run, closing every window. `Debug.Print` reaches the Immediate window through the same standard library the MDI
    /// runtime uses.
    /// </para>
    /// </remarks>
    private RunStart PrepareSubMain(ProjectDefinition project)
    {
        var tokenSource = new CancellationTokenSource();
        var watch = new RunWatch(debugController, tokenSource);
        VBProgram program;
        try
        {
            // Building the program parses every module and runs the pre-pass over it, which throws for a syntax error,
            // naming its module, and for a declaration it cannot take. It is a start that never became a run, and is
            // reported the way the form path reports one (#590).
            var context = new VBWindowContext(new MDIStandaloneStandardLib(services));
            program = new VBProgram(project, context, debugController,
                services.SingleView ? OpenInBrowser : VBLoader.OpenWindow, services.RDCoreWin64OnlySetOnWindows, Host);
            // A missing or ambiguous Main is found here, before anything runs, so it too is a start that never became a
            // run. It used to be raised as the run began, and arrived as a run that ended, which run_project answered as
            // a success (#759).
            program.Interpreter.FindStartupSubMain();
        }
        catch (Exception e)
        {
            watch.Dispose();
            return RunStart.Failed(new RunFailure(DescribeFailedStart(e, formName: null), ErrorLocation.Of(e)));
        }

        return RunStart.Started(new Run(ToEnd, () =>
        {
            debugController.Stop();
            tokenSource.Cancel();
        }));

        async Task<RunFailure?> ToEnd()
        {
            RunFailure? failure = null;
            try
            {
                // End in Sub Main unwinds it, and RunStartupSubMain takes that as a clean stop (#775).
                await VBLoader.RunSubMain(program, tokenSource.Token);
            }
            catch (Exception e) when (e is not (OperationCanceledException or ImmediateEvalException))
            {
                // A compile error the run meets as it goes arrives here; a missing or ambiguous Main no longer does,
                // since FindStartupSubMain asks before the run begins. A cancellation is left alone: it is how a
                // statement still waiting on a dialog learns that the run was ended. So is the Immediate window refusing
                // an event procedure while paused, as at the form boundary (VBWindowContext): that is its answer to what
                // was typed, not a fault in the run.
                if (e is not (VBCompileErrorException or VBRunTimeException))
                    Serilog.Log.Error(e, "Sub Main stopped on a fault");
                failure = new RunFailure(Describe(e), ErrorLocation.Of(e));
            }
            finally
            {
                watch.Dispose();
            }

            return FaultOf(watch) ?? failure;
        }
    }

    private RunFailure? FaultOf(RunWatch watch) =>
        watch.Fault is { } fault ? new RunFailure(DescribeFault(fault), ErrorLocation.Of(fault)) : null;

    /// <summary>
    /// What a start that never became a run says, the same way on both paths (#759). A compile error is the program's
    /// and is given as it is. Anything else is described as a fault in a run would be, with the startup form named
    /// where there is one, since the person did not ask for that form and may not know it was being loaded.
    /// </summary>
    private string DescribeFailedStart(Exception e, string? formName)
    {
        if (e is VBCompileErrorException)
            return e.Message;
        Serilog.Log.Error(e, "The project failed to load, at {Form}", formName ?? "Sub Main");
        var described = Describe(e);
        return formName is null
            ? described
            : programHost.StartupFormFailedToLoad(formName, described);
    }

    private string Describe(Exception e) => e switch
    {
        VBRunTimeException runTime => RunErrorText.For(runTime),
        VBCompileErrorException compile => compile.Message,
        _ => DescribeFault(e),
    };

    private string DescribeFault(Exception e) =>
        RunErrorText.ForFault(e is MDIFormRunRefusal
            ? new NotImplementedException(programHost.MDIFormNotImplemented, e)
            : e, programHost.NotImplementedYet);

    private sealed class Run(Func<Task<RunFailure?>> toEnd, Action end) : IEngineRun
    {
        public Task<RunFailure?> RunToEndAsync() => toEnd();

        public void End() => end();
    }

    /// <summary>
    /// What ends one run from inside it, listened for only while that run is live, so a fault from a run that has already
    /// ended is logged and nothing else.
    /// </summary>
    private sealed class RunWatch : IDisposable
    {
        private readonly IDebugGate debugController;
        private readonly CancellationTokenSource tokenSource;
        private readonly int session;

        public RunWatch(IDebugGate debugController, CancellationTokenSource tokenSource)
        {
            this.debugController = debugController;
            this.tokenSource = tokenSource;
            session = debugController.Session;
            VBWindowContext.Faulted += OnFaulted;
            BasicInterpreter.EndReached += OnEndReached;
        }

        /// <summary>
        /// The run's first fault, from an event procedure in any of its forms. A fault cannot be trapped, so the procedure
        /// it hit stopped at that statement, and carrying on would run the rest of the program as though the statement had
        /// worked. The run ends instead, and says why (#754).
        /// </summary>
        public Exception? Fault { get; private set; }

        private void OnFaulted(VBWindowContext sender, Exception e)
        {
            if (!sender.IsInDebugSession(debugController, session) || Fault is not null)
                return;   // the first fault ends the run; any after it come from the same collapse
            Fault = e;
            // Abort first, as EndProject does. A fault is caught where its own event procedure was dispatched, so a
            // procedure that raised that event (a Click that set Text1.Text, firing Text1_Change) would otherwise carry on
            // past the statement, and so would any other walk still running. Stopping the controller ends each of them at
            // its next statement. The raiser is still waiting on the event when it faults, however the event yielded on the
            // way, since it waits for what it raised before its next statement (#766).
            debugController.Stop();
            // Then close the form. Posted, because a fault can arrive while RunForm is still showing the form, and a window
            // closed before the run-task watches for it closing would leave the run waiting forever. A second cancel, after
            // End has already cancelled, does nothing.
            Dispatcher.UIThread.Post(() => tokenSource.Cancel());
        }

        // End in any of the run's procedures ends the run, closing the form as the End command does, and reports nothing:
        // ending is not an error (#775). The close is posted for the same reason as the fault's. Unlike a fault, End needs
        // no Stop() here: the interpreter's own Ending flag already stops every walk of it, ahead of the debugger's gate, and
        // a run has one interpreter today. The runner stops the controller once the form has closed.
        private void OnEndReached(BasicInterpreter sender)
        {
            if (sender.IsInDebugSession(debugController, session))
                Dispatcher.UIThread.Post(() => tokenSource.Cancel());
        }

        public void Dispose()
        {
            VBWindowContext.Faulted -= OnFaulted;
            BasicInterpreter.EndReached -= OnEndReached;
        }
    }

    // As FormComponentClass.InstantiateWindow builds a desktop one, the menu bar included (#1127).
    private HostedFormRoot InstantiateWindow(ComponentInstance instance, bool menuBar)
    {
        var hosted = programHost.CreateForm(new HostedFormRequest(
            instance.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "",
            instance.GetPropertyOrDefault(VBProperties.WidthProperty),
            instance.GetPropertyOrDefault(VBProperties.HeightProperty) + (menuBar ? VBLoader.MenuBarHeight : 0),
            (int)instance.GetPropertyOrDefault(VBProperties.LeftProperty),
            (int)instance.GetPropertyOrDefault(VBProperties.TopProperty)));
        var window = (Control)hosted.Window;
        window[AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(VBProperties.BackColorProperty);
        VBProps.SetName(window, instance.GetPropertyOrDefault(VBProperties.NameProperty));
        return new HostedFormRoot(hosted, new MDIStandaloneStandardLib(services));
    }

    private Task RunFormInBrowser(FormDefinition element, CancellationToken token, out HostedFormRoot window,
        IDebugGate? debugController = null)
    {
        // The same build, start and show as VBLoader.RunForm, in an MDI window (#822). Parsed before anything is shown, so
        // a syntax error fails the start without leaving a window behind that nothing would close. The program's other
        // forms get MDI windows of their own.
        var shown = window = InstantiateWindow(VBLoader.FormComponentOf(element), VBLoader.HasMenuBar(element));
        VBLoader.PrepareForm(element, shown.Context, shown.SetContent, debugController, OpenInBrowser, services.RDCoreWin64OnlySetOnWindows, Host);
        return VBLoader.StartAndShow(shown.Context, token, close: shown.CloseForm,
            showUntilClosed: () => shown.ShowInHost(blockBehind: true));
    }

    // A window for another of the program's forms, in the single-view host (#822).
    private IFormView OpenInBrowser(FormDefinition definition) =>
        InstantiateWindow(VBLoader.FormComponentOf(definition), VBLoader.HasMenuBar(definition));

    public class MDIStandaloneStandardLib : IBasicStandardLibrary
    {
        private readonly RunEngineServices services;
        private readonly IProgramHost programHost;

        public MDIStandaloneStandardLib(RunEngineServices services)
        {
            this.services = services;
            programHost = services.Host;
        }

        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
            => MsgBox(text, caption, buttons, icon, MessageBoxLayout.None);

        public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon, MessageBoxLayout layout)
        {
            return await ProgramDialogs.ShowMessageBox(services.Dialogs, services.SingleView, text, caption, buttons, icon, layout);
        }

        public async Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            return await ProgramDialogs.ShowInputBox(services.Dialogs, services.SingleView, prompt, title, defaultText);
        }

        // The Immediate window shows the text, not the typed values (#1380).
        public void DebugPrint(Vb6Value value) { }

        public void DebugWrite(string text) => VBDebugConsole.Write(text);

        public int DebugColumn => VBDebugConsole.Column;

        public ITextClipboard Clipboard => new AvaloniaTextClipboard(() =>
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow?.Clipboard);

        public IVbSettingsStore Settings => VbSettingsStores.ForThisUser();

        private HexIDE.Runtime.AvaloniaInterop.AvaloniaDisplay? display;
        public IVbDisplay Display => display ??= new HexIDE.Runtime.AvaloniaInterop.AvaloniaDisplay(() =>
            (Application.Current?.ApplicationLifetime as Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
                ?.MainWindow);

        /// <summary>
        /// A CommonDialog's Open or Save As (#1084), through the IDE's own pickers, so an automation client answers one
        /// with answer_next_file_dialog as it answers the IDE's.
        /// </summary>
        public Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request) => programHost.FileDialog(request);

        public Task<bool> ShowPrintDialog() => programHost.PrintDialog();

        public Task<int?> ShowColorDialog(int initial) => programHost.ColorDialog(initial);

        public Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) => programHost.FontDialog(initial);

    }
}
