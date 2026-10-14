using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

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

    /// <summary>Show the window, modelessly.</summary>
    void ShowForm();

    /// <summary>Hide the window. The form stays loaded.</summary>
    void HideForm();

    /// <summary>Close the window, as the program does: the form isn't asked.</summary>
    void CloseForm();
}

/// <summary>
/// A running program (#822): one interpreter built from every module, every form a template of it, one event pump that
/// every form's events go through, and the forms that have instances. It ends when no form is loaded, shown or hidden
/// (measured), and the default instances' Terminates come then, in the order the forms were made.
/// </summary>
public sealed class VBProgram : IFormProgram
{
    private readonly BasicInterpreter interpreter;
    private readonly Func<FormDefinition, IFormView>? openForm;
    private readonly Dictionary<string, FormDefinition> definitions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<VbObject, VBWindowContext> contexts = new(ReferenceEqualityComparer.Instance);
    private readonly List<VbObject> created = [];
    private readonly HashSet<VbObject> loaded = new(ReferenceEqualityComparer.Instance);
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
    {
        this.openForm = openForm;
        var project = startup.Owner;
        var startupName = VBLoader.FormComponentOf(startup).GetPropertyOrDefault(VBProperties.NameProperty)?.ToString()
                          ?? "Module1";
        definitions[startupName] = startup;
        var others = new List<(string Name, string Code)>();
        if (project is not null)
            foreach (var form in project.Forms)
                if (!ReferenceEquals(form, startup) && definitions.TryAdd(form.Name, form))
                    others.Add((form.Name, form.Code));

        var (standardModules, classModules) = VBLoader.InterpreterModules(project);
        ExecutionContext = startupContext.ExecutionContext;
        interpreter = new BasicInterpreter(startupContext.StandardLibrary, ExecutionContext, startupContext.RootEnv,
            startup.Code, startupName, standardModules, classModules, InterpreterModuleKind.Form, others)
        {
            DebugController = debugController,
            Events = Events,
            Forms = this,
        };
        if (project is not null)
            interpreter.SetAppInfo(AppInfo.FromProject(project));
        VBLoader.DescribeProject(interpreter, project);
        interpreter.SeedFormNames();
        startupContext.Attach(this, interpreter.PrimaryModule);
        Register(startupContext);
    }

    private void Register(VBWindowContext context)
    {
        var instance = context.FormInstance!;
        contexts[instance] = context;
        if (!created.Contains(instance))
            created.Add(instance);
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
    /// controls doesn't trap it (measured, #822), so it is reported as an error in any event is.
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
        context.Attach(this, form, existing);
        var instance = context.FormInstance!;
        view.SetContent(VBLoader.SpawnComponents(definition, ExecutionContext, instance.InstanceEnv));
        context.HoldControlsUntilLoad();
        context.CloseWindow = view.CloseForm;
        Register(context);
        return context;
    }

    /// <summary>A form has loaded: the program holds it until it unloads, as VB6's Forms does.</summary>
    internal void Loaded(VBWindowContext context)
    {
        var instance = context.FormInstance!;
        if (loaded.Add(instance))
            interpreter.AddRef(Vb6Value.NewObject(instance));
    }

    /// <summary>
    /// A form's unload is complete, its handler returned: the program lets go of it, and ends once no form is loaded. A
    /// form that loaded again meanwhile, in a new window, is still the program's.
    /// </summary>
    internal async Task Gone(VBWindowContext context)
    {
        var instance = context.FormInstance!;
        if (!instance.FormLoaded && loaded.Remove(instance))
            await interpreter.ReleaseRef(Vb6Value.NewObject(instance));
        if (loaded.Count == 0)
            await FinishAsync();
    }

    // The program's end: the Terminates of the forms still held, in the order they were made (measured), then every
    // window closed.
    private async Task FinishAsync()
    {
        if (finishing)
            return;
        finishing = true;
        foreach (var instance in created.ToList())
            if (!instance.Terminated && contexts.TryGetValue(instance, out var context))
                await context.TerminateAsync();
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
