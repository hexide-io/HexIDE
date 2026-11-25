using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;

namespace HexIDE.Runtime;

/// <summary>
/// The Basic provider's way into the IDE: everything it contributes, registered through the host an add-in is given (#3411,
/// #5396).
/// </summary>
/// <remarks>
/// <para>
/// For now HexIDE.Desktop makes this and calls <see cref="Initialize"/> at startup, with an owned host under the provider's
/// own identity, so its registrations meet the same checks as any add-in's. Phase 3 of the move loads it as a bundled
/// add-in instead, and nothing here changes.
/// </para>
/// <para>
/// It registers its controls' resources, its language packs, its Form and controls with their toolbox items (#5594), its
/// control libraries (#5599), what draws the designer's controls (#5591), the VB family's editing helpers (#5403),
/// and the engine that runs projects, with its debugger (#5616). The project type joins it as its slice lands.
/// </para>
/// </remarks>
public sealed class BasicProvider : IAddin
{
    /// <summary>The dictionaries the controls' themes are in, which the designer and a running program both need.</summary>
    internal static readonly IReadOnlyList<Uri> Resources =
    [
        new("avares://HexIDE.Runtime/BuiltinControls/VBTimer.axaml"),
        new("avares://HexIDE.Runtime/BuiltinControls/VBLabel.axaml"),
        new("avares://HexIDE.Runtime/BuiltinControls/Resources.axaml"),
    ];

    /// <summary>
    /// The classes it registers, in the order the designer and the Object Browser have always had them: the Form, then the
    /// toolbox's controls.
    /// </summary>
    internal static readonly IReadOnlyList<IComponentClass> Components =
    [
        FormComponentClass.Instance,
        PictureBoxComponentClass.Instance,
        LabelComponentClass.Instance,
        TextBoxComponentClass.Instance,
        FrameComponentClass.Instance,
        CommandButtonComponentClass.Instance,
        CheckBoxComponentClass.Instance,
        OptionButtonComponentClass.Instance,
        ComboBoxComponentClass.Instance,
        ListBoxComponentClass.Instance,
        HScrollBarComponentClass.Instance,
        VScrollBarComponentClass.Instance,
        TimerComponentClass.Instance,
        ShapeComponentClass.Instance,
        ImageComponentClass.Instance,
    ];

    /// <summary>What it puts in the toolbox, in VB6's order, with each button's icon. Every class but the Form.</summary>
    internal static readonly IReadOnlyList<(IComponentClass Class, string IconKey)> Toolbox =
    [
        (PictureBoxComponentClass.Instance, "Geo.Picture"),
        (LabelComponentClass.Instance, "Geo.Label"),
        (TextBoxComponentClass.Instance, "Geo.Textbox"),
        (FrameComponentClass.Instance, "Geo.Groupbox"),
        (CommandButtonComponentClass.Instance, "Geo.Button"),
        (CheckBoxComponentClass.Instance, "Geo.Checkbox"),
        (OptionButtonComponentClass.Instance, "Geo.Radio"),
        (ComboBoxComponentClass.Instance, "Geo.Combo"),
        (ListBoxComponentClass.Instance, "Geo.Listbox"),
        (HScrollBarComponentClass.Instance, "Geo.Hscroll"),
        (VScrollBarComponentClass.Instance, "Geo.Vscroll"),
        (TimerComponentClass.Instance, "Geo.Timer"),
        (ShapeComponentClass.Instance, "Geo.Shape"),
        (ImageComponentClass.Instance, "Geo.Image"),
    ];

    /// <summary>The control libraries it registers, in the order the Components dialog and the toolbox have always had them.</summary>
    internal static IReadOnlyList<RuntimeLibrary> Libraries => ControlLibraries.All;

    /// <summary>
    /// What <paramref name="library"/> offers: the in-box controls HexIDE implements that it declares, in their order. Asked
    /// only when something first needs to know, so these classes load only then.
    /// </summary>
    internal static IReadOnlyList<IComponentClass> ControlsOf(RuntimeLibrary library)
    {
        var controls = new List<IComponentClass>();
        foreach (var control in InBoxComponentClass.Implemented)
            if (control.Libraries.Contains(library))
                controls.Add(control);
        return controls;
    }

    /// <summary>
    /// The VB family's editing helpers (#5403): its interpreter's <c>#If</c> reader, and its lexer for the parentheses and the
    /// declarations. For <c>vb6</c>, the code window's id for every VB-family project's members, VBA's and RDCore's included,
    /// and for <c>twinbasic</c>, a twinBASIC container's sources in the archive view.
    /// </summary>
    /// <remarks>
    /// <c>twinbasic</c> is here only because these helpers have always greyed a container's <c>#If</c>s. A twinBASIC-owned
    /// registration would be refused while this one serves it, so the change that adds one removes <c>twinbasic</c> from this
    /// list in the same change.
    /// </remarks>
    internal static LanguageEditing LanguageEditing { get; } = new([DocumentLanguage.Vb6, DocumentLanguage.TwinBasic])
    {
        ConditionalCode = new InterpreterConditionalCode(),
        Brackets = new InterpreterBrackets(),
        Declarations = new VbFamilyDeclarationCollector(),
    };

    private readonly List<IDisposable> registrations = [];

    // A real frame: the host names the caller's assembly as the dictionaries' owner, so this can't be inlined away.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    public void Initialize(IHexIdeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        foreach (var dictionary in Resources)
            registrations.Add(host.RegisterResources(dictionary));
        registrations.Add(host.RegisterLanguagePacks(new Uri("avares://HexIDE.Runtime/Localization/Packs/")));
        foreach (var component in Components)
            registrations.Add(host.RegisterComponent(component));
        foreach (var (component, iconKey) in Toolbox)
            registrations.Add(host.RegisterToolboxItem(component, iconKey));
        foreach (var library in Libraries)
            registrations.Add(host.RegisterControlLibrary(library, () => ControlsOf(library)));
        // Internal until the registration is published (#5403): only the host HexIDE composes the provider with can take it.
        if (host is ILanguageEditingRegistration editing)
            registrations.Add(editing.RegisterLanguageEditing(LanguageEditing));
        // VB6's projects, through the call any add-in uses; the grant gives the type VB6's identity (#5625).
        registrations.Add(host.RegisterProjectType(Serialization.Vb6ProjectFileFormat.ProjectType()));
        // Last: a refusal here leaves the themes, text, controls, editing helpers and VB6's projects already
        // registered.
        registrations.Add(host.RegisterDesignerPictures(surface => new InProcessDesignerPictures(surface)));
        // Internal until a second engine registers (#5616): only the host HexIDE composes the provider with can take it.
        if (host is IRunEngineRegistration engines)
            registrations.Add(engines.RegisterRunEngine(services => new Running.InterpreterRunEngine(services)));
    }

    public void Dispose()
    {
        for (var i = registrations.Count - 1; i >= 0; i--)
            registrations[i].Dispose();
        registrations.Clear();
    }
}
