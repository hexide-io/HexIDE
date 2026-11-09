using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Labs.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Controls;
using HexIDE.Events;
using HexIDE.Localization;
using HexIDE.Runtime;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using HexIDE.VisualDesigner;

namespace HexIDE.IDE;

/// <summary>Runs a project with HexIDE's own interpreter and its run-time forms, in the IDE's process.</summary>
public sealed class InterpreterRunEngine : IRunEngine
{
    private readonly IWindowManager windowManager;
    private readonly ILocalizationService localization;
    private readonly IDebugController debugController;

    public InterpreterRunEngine(IWindowManager windowManager, ILocalizationService localization, IDebugController debugController)
    {
        this.windowManager = windowManager;
        this.localization = localization;
        this.debugController = debugController;
        // A running program's CommonDialog shows the IDE's own dialogs (#1084), whichever window the form runs in.
        HostDialogs.File = request => MDIStandaloneStandardLib.ShowFileDialog(windowManager, request);
        HostDialogs.Print = windowManager.ShowPrintDialog;
        HostDialogs.Color = windowManager.ShowColorDialog;
        HostDialogs.Font = initial => MDIStandaloneStandardLib.ShowFontDialog(windowManager, initial);
    }

    /// <inheritdoc />
    /// <remarks>A project an add-in read is run by the program it belongs to, never by this interpreter.</remarks>
    public bool Runs(ProjectDefinition project) => project.ContributedBy is null;

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
    public RunStart Prepare(ProjectDefinition project) =>
        project.StartsAtSubMain ? PrepareSubMain(project) : PrepareForm(project.StartupForm!);

    private RunStart PrepareForm(FormDefinition form)
    {
        var tokenSource = new CancellationTokenSource();
        var watch = new RunWatch(debugController, tokenSource);
        Task task;
        try
        {
            if (Static.SingleView)
            {
                task = RunFormInBrowser(form, tokenSource.Token, out _, debugController);
            }
            else
            {
                // RunForm starts the form and shows it once Load has run (#822).
                task = VBLoader.RunForm(form, tokenSource.Token, out _, debugController);
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
            var context = new VBWindowContext(new MDIStandaloneStandardLib(windowManager));
            program = new VBProgram(project, context, debugController,
                Static.SingleView ? OpenInBrowser : VBLoader.OpenWindow);
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
        watch.Fault is { } fault ? new RunFailure(RunErrorText.ForFault(fault, localization), ErrorLocation.Of(fault)) : null;

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
            : string.Format(localization.GetString("Str.ProjectRunner.StartupFormFailedToLoad"), formName, described);
    }

    private string Describe(Exception e) => e switch
    {
        VBRunTimeException runTime => RunErrorText.For(runTime),
        VBCompileErrorException compile => compile.Message,
        _ => RunErrorText.ForFault(e, localization),
    };

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
        private readonly IDebugController debugController;
        private readonly CancellationTokenSource tokenSource;
        private readonly int session;

        public RunWatch(IDebugController debugController, CancellationTokenSource tokenSource)
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
    private VBMDIFormRuntime InstantiateWindow(ComponentInstance instance, bool menuBar)
    {
        var form = new VBMDIFormRuntime(windowManager)
        {
            Title = instance.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "",
            Width = instance.GetPropertyOrDefault(VBProperties.WidthProperty),
            Height = instance.GetPropertyOrDefault(VBProperties.HeightProperty) + (menuBar ? VBLoader.MenuBarHeight : 0),
            [AttachedProperties.BackColorProperty] = instance.GetPropertyOrDefault(VBProperties.BackColorProperty),
            [MDIHostPanel.WindowLocationProperty] = new Point((int)instance.GetPropertyOrDefault(VBProperties.LeftProperty), (int)instance.GetPropertyOrDefault(VBProperties.TopProperty))
        };
        VBProps.SetName(form, instance.GetPropertyOrDefault(VBProperties.NameProperty));
        return form;
    }
    
    private Task RunFormInBrowser(FormDefinition element, CancellationToken token, out VBMDIFormRuntime window,
        IDebugController? debugController = null)
    {
        // The same build, start and show as VBLoader.RunForm, in an MDI window (#822). Parsed before anything is shown, so
        // a syntax error fails the start without leaving a window behind that nothing would close. The program's other
        // forms get MDI windows of their own.
        var shown = window = InstantiateWindow(VBLoader.FormComponentOf(element), VBLoader.HasMenuBar(element));
        VBLoader.PrepareForm(element, shown.Context, content => shown.Content = content, debugController, OpenInBrowser);
        return VBLoader.StartAndShow(shown.Context, token, close: shown.CloseForm,
            showUntilClosed: () => shown.ShowInHost(blockBehind: true));
    }

    // A window for another of the program's forms, in the single-view host (#822).
    private IFormView OpenInBrowser(FormDefinition definition) =>
        InstantiateWindow(VBLoader.FormComponentOf(definition), VBLoader.HasMenuBar(definition));

    public class VBMDIFormRuntime : MDIWindow, IModuleExecutionRoot, IFormView
    {
        protected override Type StyleKeyOverride => typeof(MDIWindow);

        private VBWindowContext windowContext;
        private readonly IWindowManager windowManager;

        public VBWindowContext Context => windowContext;

        // The window's time in the host, from its first show until it closes.
        private Task? inHost;

        public VBMDIFormRuntime(IWindowManager windowManager)
        {
            this.windowManager = windowManager;
            windowContext = new VBWindowContext(new MDIStandaloneStandardLib(windowManager)) { Window = this };
            // Held by a modal form, the window takes no input of its own (#822), and a press on it brings the modal form
            // forward instead.
            foreach (var input in new RoutedEvent[] { KeyDownEvent, KeyUpEvent, TextInputEvent, PointerReleasedEvent, PointerWheelChangedEvent })
                AddHandler(input, new EventHandler<RoutedEventArgs>((_, e) => e.Handled |= heldBy is not null), RoutingStrategies.Tunnel);
            AddHandler(PointerPressedEvent, (_, e) =>
            {
                if (heldBy is null)
                    return;
                e.Handled = true;
                heldBy.ActivateForm();
            }, RoutingStrategies.Tunnel);
            // Report layout and setters through the same extent comparison (#994, #1570). A size setter reports
            // immediately, even during Load; the later layout finds nothing new.
            this.GetObservable(BoundsProperty).Subscribe(new ActionObserver<Rect>(_ =>
            {
                sizePending = false;
                windowContext.WindowResized();
            }));
            this.GetObservable(WidthProperty).Subscribe(new ActionObserver<double>(_ => SizeSet()));
            this.GetObservable(HeightProperty).Subscribe(new ActionObserver<double>(_ => SizeSet()));
            this.GetObservable(MDIHostPanel.WindowStateProperty).Subscribe(new ActionObserver<WindowState>(_ => windowContext.WindowResized()));
            this.GetObservable(IsVisibleProperty).Subscribe(new ActionObserver<bool>(visible =>
            {
                if (visible)
                    windowContext.Shown();
            }));
            // The host's active window is its topmost, which stays so while the IDE itself is inactive, as a form stays the
            // program's active one while another application is. A held form is never active (#994).
            this.GetObservable(MDIHostPanel.IsActiveProperty).Subscribe(new ActionObserver<bool>(active =>
            {
                if (active && heldBy is null)
                    windowContext.Program?.Activated(windowContext);
            }));
        }

        private bool sizePending;

        private void SizeSet()
        {
            sizePending = true;
            windowContext.WindowResized(sizeSet: true);
        }

        /// <inheritdoc/>
        public (Size Size, WindowState State) Extent
        {
            get
            {
                var state = MDIHostPanel.GetWindowState(this);
                if (state == WindowState.Minimized)
                    return (default, state);
                // Until the host has arranged a size assignment, use the requested size.
                var size = sizePending || Bounds.Size == default ? new Size(Width, Height) : Bounds.Size;
                var scaling = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                return (new Size(Math.Round(size.Width * scaling), Math.Round(size.Height * scaling)), state);
            }
        }

        // Form_Load is not raised here: the program raises Initialize and Load before the window is shown (#822, #994).

        public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null)
        {
            windowContext.ExecuteSub(name, args);
        }

        public bool Handles(string name) => windowContext.Handles(name);

        public void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None, Func<bool>? when = null) =>
            windowContext.ExecuteEvent(name, args, coalescing, when);

        public void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => windowContext.ExecuteTimer(name, args);

        public Task<bool> UnloadAsync(int mode) => windowContext.UnloadAsync(mode);

        /// <summary>
        /// Put the window in the host, or show it again there: the startup form blocking the IDE behind it, another form
        /// blocking nothing, so the program's forms stay usable beside one another (#822). Completes when it closes.
        /// </summary>
        public Task ShowInHost(bool blockBehind)
        {
            IsVisible = true;
            if (inHost is not null)
                return inHost;
            inHost = blockBehind ? windowManager.ShowManagedWindow(this) : windowManager.ShowManagedWindow(this, blockBehind: false);
            // The host's own close takes the window away. The title bar's close asks the program first, as a desktop form's
            // close box does, QueryUnload with UnloadMode 0 and then Unload (#973), and is refused while a modal form holds
            // this one (#822). The window goes once the form has unloaded, through the host's close.
            hostClose = CloseCommand;
            CloseCommand = new DelegateCommand(() =>
            {
                if (!windowContext.UnloadFromCloseBox())
                    hostClose?.Execute(null);
            }, () => true);
            return inHost;
        }

        // The host's close, which takes the window away without asking the program.
        private System.Windows.Input.ICommand? hostClose;

        public void SetContent(Control content) => Content = content;

        public void ShowForm() => ShowInHost(blockBehind: false);

        public void HideForm() => IsVisible = false;

        public void CloseForm() => (hostClose ?? CloseCommand)?.Execute(null);

        public bool IsShown => inHost is { IsCompleted: false } && IsVisible;

        private IFormView? heldBy;

        public IFormView? HeldBy
        {
            get => heldBy;
            set
            {
                heldBy = value;
                // Its controls stop taking hits, which disabling them would do too, greying each one (#822).
                if (Content is Avalonia.Input.InputElement content)
                    content.IsHitTestVisible = value is null;
            }
        }

        public void ActivateForm() => this.ActivateMDIForm();
    }

    public class MDIStandaloneStandardLib : IBasicStandardLibrary
    {
        private readonly IWindowManager windowManager;

        public MDIStandaloneStandardLib(IWindowManager windowManager)
        {
            this.windowManager = windowManager;
        }

        public async Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            return await windowManager.MessageBox(text, caption, buttons, icon, isProgramDialog: true);
        }

        public async Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            return await windowManager.InputBox(prompt, title, defaultText, isProgramDialog: true);
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
        public Task<VbFileDialogAnswer?> ShowFileDialog(VbFileDialogRequest request) => ShowFileDialog(windowManager, request);

        public Task<bool> ShowPrintDialog() => windowManager.ShowPrintDialog();

        public Task<int?> ShowColorDialog(int initial) => windowManager.ShowColorDialog(initial);

        public Task<VbFontChoice?> ShowFontDialog(VbFontChoice initial) => ShowFontDialog(windowManager, initial);

        internal static async Task<VbFileDialogAnswer?> ShowFileDialog(IWindowManager windowManager, VbFileDialogRequest request)
        {
#if DEBUG
            // An armed answer stands for a name typed with the filter left as the program set it.
            int? scriptedIndex = HexIDE.IDE.ScriptedFileDialogs.Pending > 0 && request.FilterIndex >= 1 ? request.FilterIndex : null;
#else
            int? scriptedIndex = null;
#endif
            var start = Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime { MainWindow: { } main }
                        && TopLevel.GetTopLevel(main)?.StorageProvider is { } provider
                ? await VbFileDialogOptions.StartFolder(provider, request)
                : null;
            if (request.Save)
            {
                var path = await windowManager.SaveFilePickerAsync(VbFileDialogOptions.Save(request, start));
                return path is null ? null : new VbFileDialogAnswer(path, scriptedIndex);
            }
            var paths = await windowManager.OpenFilePickerAsync(VbFileDialogOptions.Open(request, start));
            return paths is { Count: > 0 }
                ? new VbFileDialogAnswer(paths[0], scriptedIndex, paths.Skip(1).ToArray())
                : null;
        }

        /// <summary>A CommonDialog's ShowFont (#1084), through the IDE's font dialog.</summary>
        internal static async Task<VbFontChoice?> ShowFontDialog(IWindowManager windowManager, VbFontChoice initial)
        {
            var start = initial.Name.Length == 0
                ? null
                : new FontDialogResult(new Avalonia.Media.FontFamily(initial.Name),
                    initial.Italic ? Avalonia.Media.FontStyle.Italic : Avalonia.Media.FontStyle.Normal,
                    initial.Bold ? Avalonia.Media.FontWeight.Bold : Avalonia.Media.FontWeight.Normal, initial.Size);
            var chosen = await windowManager.ShowFontDialog(start);
            return chosen is null
                ? null
                : new VbFontChoice(chosen.Family.Name, (float)chosen.Size, chosen.Weight >= Avalonia.Media.FontWeight.Bold,
                    chosen.Style != Avalonia.Media.FontStyle.Normal);
        }
    }
}
