using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime;

/// <summary>
/// A window a form runs in, as the program drives it (#822): its handle on the program, its content, and showing, hiding
/// and closing it. The host makes one for each form that gets an instance.
/// </summary>
public interface IFormView
{
    /// <summary>The form's handle on the program: its instance, its events and its unload.</summary>
    VBWindowContext Context { get; }

    /// <summary>Put the form's controls in the window.</summary>
    void SetContent(Avalonia.Controls.Control content);

    /// <summary>Show the window. Whether it is modal is the program's business, not the window's.</summary>
    void ShowForm();

    /// <summary>Whether the window is showing: false once it has been hidden, made invisible or closed.</summary>
    bool IsShown { get; }

    /// <summary>
    /// The window's client size in whole device pixels, and its state, whose change Resize reports (#994). Pixels, as VB6's
    /// sizes are: a form 4000 twips wide is 266.67 units, which the window lays out again at 267. A minimized window has no
    /// size: VB6 raises Resize once as it minimizes, whatever the backend does to the window then.
    /// </summary>
    (Avalonia.Size Size, Avalonia.Controls.WindowState State) Extent { get; }

    /// <summary>
    /// The modal form holding this one, or null (#822). A held form is drawn as it was and takes no input: no pointer, no
    /// key, and its close box is refused, as VB6's disabled window ignores a click on it. Its <c>Enabled</c> reads False
    /// (measured). It is not disabled, which would grey every control VB6 leaves as drawn.
    /// </summary>
    IFormView? HeldBy { get; set; }

    /// <summary>Bring the window to the front with the focus, as activating a form beneath a modal one brings the modal form.</summary>
    void ActivateForm();

    /// <summary>Hide the window. The form stays loaded.</summary>
    void HideForm();

    /// <summary>Close the window, as the program does: the form isn't asked.</summary>
    void CloseForm();
}

/// <summary>
/// A running program (#822): one interpreter built from every module, every form a template of it, one event pump that
/// every form's events go through, and the forms that have instances. It ends when no form is loaded, shown or hidden
/// (measured), and remaining instances terminate then, in project form order, newest first within each form (#2497).
/// </summary>
public sealed class VBProgram : IFormProgram
{
    private readonly BasicInterpreter interpreter;
    internal IBasicStandardLibrary StandardLibrary { get; }
    private readonly Func<FormDefinition, IFormView>? openForm;
    private readonly Dictionary<string, FormDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VbObject, VBWindowContext> contexts = new(ReferenceEqualityComparer.Instance);
    private readonly List<VbObject> created = [];
    private readonly Dictionary<string, int> formOrder = new(StringComparer.OrdinalIgnoreCase);
    // The loaded forms, in the order they loaded, as VB6's Forms holds them (measured, #2496).
    private readonly List<VbObject> loaded = [];
    // The modal forms displayed, innermost last.
    private readonly List<VBWindowContext> modals = [];
    // The form Activate was last raised for, until another is activated or it unloads.
    private VBWindowContext? active;
    private readonly TaskCompletionSource ended = new();
    private bool finishing;

    /// <summary>The program's state, every module's and form's slots in one.</summary>
    public ModuleExecutionContext ExecutionContext { get; }

    /// <summary>Runs every form's handlers one at a time, as VB6's single thread does (#766).</summary>
    public EventPump Events { get; } = new();

    /// <summary>The program's interpreter.</summary>
    public BasicInterpreter Interpreter => interpreter;

    /// <summary>Completes when the program has ended: its last form unloaded, and the Terminates run.</summary>
    public Task Ended => ended.Task;

    /// <summary>
    /// Build the program a run of <paramref name="startup"/> runs: parsed whole, so a syntax error in any module throws
    /// here, before anything is shown.
    /// </summary>
    /// <param name="startupContext">The startup form's handle, whose window was made before the program.</param>
    /// <param name="openForm">
    /// How the host makes a window for another form. Null in a host that runs one form only: another form's use is then
    /// refused, naming #822.
    /// </param>
    public VBProgram(FormDefinition startup, VBWindowContext startupContext, Debugging.IDebugController? debugController,
        Func<FormDefinition, IFormView>? openForm)
        : this(startup.Owner, startup, startupContext, debugController, openForm)
    {
    }

    /// <summary>
    /// Build the program a run that starts at <c>Sub Main</c> runs (#822): every standard module, the first of them primary,
    /// and every form a template whose default instance is made where its name is first used. Parsed whole, as a form's
    /// program is.
    /// </summary>
    /// <param name="rootContext">The library, state and root environment the program runs in. It is no form's handle.</param>
    /// <param name="openForm">How the host makes a window for a form, or null in a host that shows none.</param>
    public VBProgram(ProjectDefinition project, VBWindowContext rootContext, Debugging.IDebugController? debugController,
        Func<FormDefinition, IFormView>? openForm)
        : this(project, startup: null, rootContext, debugController, openForm)
    {
    }

    private VBProgram(ProjectDefinition? project, FormDefinition? startup, VBWindowContext rootContext,
        Debugging.IDebugController? debugController, Func<FormDefinition, IFormView>? openForm)
    {
        this.openForm = openForm;
        if (project is not null)
            foreach (var form in project.Forms)
                formOrder.TryAdd(form.Name, formOrder.Count);
        StandardLibrary = rootContext.StandardLibrary;
        var (standardModules, classModules) = VBLoader.InterpreterModules(project);
        string primaryName, primaryCode;
        var primaryKind = InterpreterModuleKind.Form;
        IReadOnlyList<(string Name, string Code)> additional = standardModules;
        if (startup is not null)
        {
            primaryName = VBLoader.FormComponentOf(startup).GetPropertyOrDefault(VBProperties.NameProperty)?.ToString()
                          ?? "Module1";
            // Every form's whole file, header included, as the code window and the debugger count its lines (#273).
            primaryCode = FormCodeText.WholeFile(startup);
            definitions[primaryName] = startup;
        }
        else
        {
            // Sub Main may be in any standard module (measured); the first is primary only so the gate has a name for it.
            if (standardModules.Count == 0)
                throw new VBCompileErrorException(BasicInterpreter.NoStartupObject);
            (primaryName, primaryCode) = standardModules[0];
            primaryKind = InterpreterModuleKind.Standard;
            additional = standardModules.Skip(1).ToList();
        }
        var others = new List<(string Name, string Code)>();
        if (project is not null)
            foreach (var form in project.Forms)
                if (!ReferenceEquals(form, startup) && definitions.TryAdd(form.Name, form))
                    others.Add((form.Name, FormCodeText.WholeFile(form)));

        ExecutionContext = rootContext.ExecutionContext;
        // The project's Conditional Compilation Arguments, or vb6.exe's refusal of them before anything runs (#1112).
        var compilation = project is null ? null : CompilationConstants.ForProject(project);
        interpreter = new BasicInterpreter(rootContext.StandardLibrary, ExecutionContext, rootContext.RootEnv,
            primaryCode, primaryName, additional, classModules.Concat(project?.Modules
                .Where(m => m.Kind == ModuleKind.UserControl && m.FormPart is not null)
                .Select(m => (m.Name, FormCodeText.WholeFile(m))) ?? []).ToList(), primaryKind, others, compilation)
        {
            DebugController = debugController,
            Events = Events,
            Forms = this,
        };
        if (project is not null)
        {
            interpreter.SetAppInfo(AppInfo.FromProject(project));
            foreach (var module in project.Modules.Where(m => m.Kind == ModuleKind.UserControl && m.FormPart is not null))
                if (interpreter.Modules.TryGet(module.Name, out var controlTemplate))
                    controlTemplate.HostedControl = true;
        }
        VBLoader.DescribeProject(interpreter, project);
        interpreter.SeedFormNames();
        if (startup is null)
            return;
        rootContext.Attach(this, interpreter.PrimaryModule, primaryCode);
        Register(rootContext);
    }

    // Sub Main is running. The program doesn't end while it is, however many forms have unloaded (measured).
    private bool mainRunning;

    /// <summary>
    /// Run <c>Sub Main</c>, the program's first walk (#822): events that come meanwhile wait for it, as they wait for a
    /// form's Load. Then the program lasts until no form is loaded, and ends at once if none is (measured). An error in
    /// Main, a missing or ambiguous Main among them, is thrown to the caller.
    /// </summary>
    public async Task RunMainAsync()
    {
        mainRunning = true;
        try
        {
            await Events.Run("Sub Main", async () =>
            {
                await interpreter.RunStartupSubMain();
                return true;
            });
        }
        finally
        {
            mainRunning = false;
        }
        if (loaded.Count == 0)
            await FinishAsync();
    }

    /// <summary>The run was ended from outside the program, by End, Stop or a fault: every window goes, and it is over.</summary>
    public void Abandon()
    {
        // Ended from outside, nothing more of the program runs: no Terminate, as End raises none.
        finishing = true;
        CloseAll();
        Events.Close();
        ended.TrySetResult();
    }

    private void Register(VBWindowContext context)
    {
        var instance = context.FormInstance!;
        contexts[instance] = context;
        if (!created.Contains(instance))
            created.Add(instance);
        if (context.Window is { } window)
            AvaloniaInterop.ScreenPointer.Apply(window, screenPointer);
    }

    /// <inheritdoc/>
    public VbObject? ActiveForm => active?.FormInstance;

    /// <summary>
    /// The control that has the focus in the active form's window: the first of the form's controls at or above the focused
    /// element, so a part of a control, a ComboBox's text box, is the ComboBox. Null when the focus is on none of them, as on a
    /// form with nothing that can take it (measured: Nothing, as the form's own ActiveControl is), and when no form is active.
    /// </summary>
    public Avalonia.Controls.Control? ActiveControl
    {
        get
        {
            if (active?.Window is not { } window
                || Avalonia.Controls.TopLevel.GetTopLevel(window)?.FocusManager?.GetFocusedElement() is not Avalonia.LogicalTree.ILogical focused)
                return null;
            Avalonia.Controls.Control? found = null;
            for (var element = focused; element is not null; element = element.LogicalParent)
            {
                if (ReferenceEquals(element, window))
                    return found;
                if (found is null && element is Avalonia.Controls.Control control && FormControls.TryOf(control, out _))
                    found = control;
            }
            // Not in the active form's window: the focus is elsewhere, as on the IDE's own editor.
            return null;
        }
    }

    // Screen.MousePointer, which every form shows while it is not 0 (#2431).
    private BuiltinTypes.VBCursorType screenPointer;

    /// <inheritdoc/>
    public void ScreenPointerChanged(BuiltinTypes.VBCursorType pointer)
    {
        screenPointer = pointer;
        foreach (var context in contexts.Values)
            if (context.Window is { } window)
                AvaloniaInterop.ScreenPointer.Apply(window, pointer);
    }

    /// <summary>
    /// Make <paramref name="form"/>'s default instance where its name is first read: its declarations and Initialize run
    /// inside the statement that read it, and an error in them is that statement's, for its own <c>On Error</c> to trap
    /// (measured, #822), as an error in a class's Initialize is the <c>New</c>'s.
    /// </summary>
    public async Task MakeDefaultInstance(ModuleInfo form)
    {
        var context = Open(form, existing: null);
        await context.InitializeAsync();
    }

    /// <summary>
    /// Load <paramref name="form"/> if it isn't loaded, inside the statement that asked. Form_Load is an event, and its
    /// error is not the statement's: an <c>On Error</c> around <c>Load Form2</c>, <c>Form2.Show</c> or a use of one of its
    /// controls doesn't trap it (measured, #822), so it is reported as an error in any event is. A Load that unloads the
    /// form is the statement's error, though: 364, "Object was unloaded", whatever the use was (measured).
    /// </summary>
    public async Task Load(VbObject form)
    {
        if (form.FormLoaded)
            return;
        if (!contexts.TryGetValue(form, out var context))
            throw new NotImplementedException($"Load of {form.ClassName} is not implemented here (#822).");
        // An unloaded form's window is gone, as VB6 destroys it: it loads again in a new one, its controls as designed,
        // raising Load and not Initialize (measured).
        if (form.FormUnloaded)
            context = Open(form.ClassDef, existing: form);
        await context.LoadAsync();
        if (form.FormUnloaded)
            throw new VBRunTimeException(VBStandardError.ObjectWasUnloaded);
    }

    // A window for `form`, from the host, and its controls put into the instance: a new instance, or an unloaded one.
    private VBWindowContext Open(ModuleInfo form, VbObject? existing)
    {
        if (!definitions.TryGetValue(form.Name, out var definition))
            throw new NotImplementedException($"{form.Name} has no form definition to run (#822).");
        if (openForm is null)
            throw new NotImplementedException(existing is null
                ? $"{form.Name} is another form, and this host runs only the startup form so far (#822)."
                : $"{form.Name} is used after it has unloaded, and this host can't load it again yet (#822).");
        var view = openForm(definition);
        var context = view.Context;
        context.Attach(this, form, FormCodeText.WholeFile(definition), existing);
        var instance = context.FormInstance!;
        view.SetContent(VBLoader.SpawnComponents(definition, ExecutionContext, instance.InstanceEnv, context.StandardLibrary, instance, this));
        context.HoldControlsUntilLoad();
        context.CloseWindow = view.CloseForm;
        Register(context);
        return context;
    }

    /// <summary>
    /// Show a form modelessly. While a modal form is displayed that is 401, "Can't show non-modal form when modal form is
    /// displayed", for any form, one already shown and the modal form itself included (measured, #822).
    /// </summary>
    internal void Show(IFormView view)
    {
        if (modals.Count > 0)
            throw new VBRunTimeException(VBStandardError.CantShowNonModalFormWhenModalFormIsDisplayed);
        view.ShowForm();
    }

    /// <summary>
    /// Hide a form. While a modal form is displayed only the innermost one can be hidden: any other is 402, "Must close or
    /// hide topmost modal form first", a modal form beneath it included (measured, #822).
    /// </summary>
    internal void Hide(IFormView view)
    {
        if (modals.Count > 0 && !ReferenceEquals(modals[^1], view.Context))
            throw new VBRunTimeException(VBStandardError.CodeMustCloseTopmostModalFormFirst);
        view.HideForm();
    }

    /// <summary>
    /// Show a form modally, as <c>Show vbModal</c> does (#822): every other form is held, taking no input, and the statement
    /// waits, running the program's events as they come, until the form is hidden, made invisible or unloaded. Then each
    /// form is held as it was before. All measured.
    /// </summary>
    /// <remarks>
    /// A form already shown modelessly turns modal, with no error: VB6's documented 400 never came (measured). So does a
    /// form beneath the modal one, over it. Only the modal forms themselves are refused, with 405, "Unable to show modal
    /// form within this context". Other forms' events go on: Form1's Timer ticks under Form2's modal form, in compiled code
    /// and in the IDE alike (measured).
    /// </remarks>
    internal async Task ShowModal(IFormView view)
    {
        var context = view.Context;
        if (modals.Contains(context))
            throw new VBRunTimeException(VBStandardError.UnableToShowModalFormWithinThisContext);
        // Form1.Enabled reads False while Form2 is modal, and True again once it has gone (measured on the console). The
        // hold is apart from the form's own Enabled, so a form the program disabled stays so. A form an outer modal form
        // holds is freed while it is modal over it, and held again after. Modal forms nest, so putting back what each
        // form was held by undoes this one exactly.
        var held = new List<(IFormView View, IFormView? Before)>();
        foreach (var other in contexts.Values)
            if (other.Window is IFormView otherView)
            {
                held.Add((otherView, otherView.HeldBy));
                otherView.HeldBy = ReferenceEquals(other, context) ? null : view;
            }
        modals.Add(context);
        var over = interpreter.BuiltIns.WatchRunEnd?.Invoke();
        try
        {
            view.ShowForm();
            var form = context.FormInstance!;
            await Events.WhileModal(() => form.FormLoaded && view.IsShown && over?.Invoke() != true);
        }
        finally
        {
            modals.Remove(context);
            foreach (var (otherView, before) in held)
                otherView.HeldBy = before;
        }
        // The run ended under the modal form, by End, Stop or a fault: the statement after the Show never runs (measured
        // for End).
        if (Events.IsClosed || over?.Invoke() == true)
            throw new Debugging.StopExecutionSignal();
    }

    /// <summary>
    /// One of the program's windows has been activated (#994): Deactivate for the form Activate was last raised for, if it
    /// is still loaded, then Activate for this one. Measured on vb6.exe in the console, native and p-code:
    /// <list type="bullet">
    /// <item>both come after the statement that activated the form, a Show or a SetFocus, has returned, as messages Windows
    /// posts. So a Show raises its Resize first, inside the statement;</item>
    /// <item>a form hidden while it was active is given Deactivate, and one unloaded is not;</item>
    /// <item>leaving for another application raises nothing, and coming back raises nothing unless it is to another of the
    /// program's forms. So a window's deactivation is never reported, only which form is activated.</item>
    /// </list>
    /// A window a modal form holds is never activated, as Windows doesn't activate a disabled one, so its host doesn't
    /// report it.
    /// </summary>
    public void Activated(VBWindowContext context)
    {
        if (ReferenceEquals(active, context) || context.FormInstance is not { FormLoaded: true })
            return;
        var previous = active;
        active = context;
        // Posted: they wait for the handler that is running. Win32 activates the window inside Show, and without this the
        // pair ran there, before the statement after the Show; headless, the activation comes after the handler anyway. A
        // window is shown before it is activated, so a Show's Resize has run by then.
        using var posting = Events.Posting();
        previous?.ExecuteSub("Form_Deactivate");
        context.ShowControls();
        context.ExecuteSub("Form_Activate");
    }

    /// <summary>A form has loaded: the program holds it until it unloads, as VB6's Forms does.</summary>
    internal void Loaded(VBWindowContext context)
    {
        var instance = context.FormInstance!;
        if (loaded.Exists(form => ReferenceEquals(form, instance)))
            return;
        loaded.Add(instance);
        interpreter.AddRef(Vb6Value.NewObject(instance));
    }

    /// <inheritdoc/>
    public IReadOnlyList<VbObject> LoadedForms => loaded;

    /// <summary>
    /// A form has unloaded: the program lets go of it there, inside the statement that unloaded it, so a
    /// <c>Set Form2 = Nothing</c> after it raises Terminate before the next statement (measured, #822).
    /// </summary>
    internal async Task Unloaded(VBWindowContext context)
    {
        // The active form unloaded is given no Deactivate, and the form Windows activates next its Activate (measured, #994).
        if (ReferenceEquals(active, context))
            active = null;
        var instance = context.FormInstance!;
        var at = loaded.FindIndex(form => ReferenceEquals(form, instance));
        if (at < 0)
            return;
        loaded.RemoveAt(at);
        await interpreter.ReleaseRef(Vb6Value.NewObject(instance));
    }

    /// <summary>
    /// An unload is complete, the handler that unloaded the form having returned: the program ends there if no form is
    /// loaded (measured, #973). A form that loaded again meanwhile, in a new window, keeps it going.
    /// </summary>
    internal async Task Gone()
    {
        if (loaded.Count == 0 && !mainRunning)
            await FinishAsync();
    }

    // The program's end: the Terminates of the forms still held, in the order they were made (measured), then every
    // window closed.
    private async Task FinishAsync()
    {
        if (finishing)
            return;
        finishing = true;
        // #2496 measured project order between forms, reverse creation order within each form.
        var instances = created.ToList();
        if (formOrder.Count > 0)
            instances = instances.AsEnumerable().Reverse()
                .OrderBy(instance => formOrder.GetValueOrDefault(instance.ClassName, int.MaxValue)).ToList();
        foreach (var instance in instances)
            if (!instance.Terminated && contexts.TryGetValue(instance, out var context))
                await context.TerminateAsync();
        await interpreter.ReleasePredeclaredInstances();
        CloseAll();
        ended.TrySetResult();
    }

    /// <summary>Close every form's window, as the program does when the run is ended from outside it.</summary>
    public void CloseAll()
    {
        foreach (var context in contexts.Values.ToList())
            context.CloseOnce();
    }
}
