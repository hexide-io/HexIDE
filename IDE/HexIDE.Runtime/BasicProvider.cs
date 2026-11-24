using HexIDE.Addins;

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
/// It registers its controls' resources, its language packs, its Form and controls with their toolbox items (#5594), and
/// what draws the designer's controls (#5591). Its control libraries, the run engine and the project type join it as their
/// slices land.
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
        // Last: a refusal here leaves the themes, text and controls already registered.
        registrations.Add(host.RegisterDesignerPictures(surface => new InProcessDesignerPictures(surface)));
    }

    public void Dispose()
    {
        for (var i = registrations.Count - 1; i >= 0; i--)
            registrations[i].Dispose();
        registrations.Clear();
    }
}
