using HexIDE.IDE;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.ProjectElements;

public partial class FormDefinition : INotifyPropertyChanged
{
    private Vb6TextEncoding textEncoding = Vb6TextEncoding.Ansi;

    /// <summary>Source encoding retained through saves and path changes.</summary>
    /// <remarks>
    /// A designer half has none of its own. A <c>.ctl</c> or <c>.pag</c> is one file in one encoding, and the
    /// module holds it, as it holds the identity: read or set here, it is the module's, so the designer's Save
    /// Form and the module's own save write the file the same way. Each half used to hold a copy, and a reload
    /// whose new text did not parse as a designer file set only the module's, so the designer's Save Form then
    /// wrote the file in the encoding it had before.
    /// </remarks>
    public Vb6TextEncoding TextEncoding
    {
        get => OwningModule?.TextEncoding ?? textEncoding;
        set
        {
            if (OwningModule is { } module)
                module.TextEncoding = value;
            else
                textEncoding = value;
        }
    }

    public ProjectDefinition Owner { get; }

    /// <summary>
    /// Creates a FormDefinition with a pre-built component list (used by deserializers
    /// and factory methods that know the concrete IComponentClass for "Form").
    /// </summary>
    public FormDefinition(ProjectDefinition owner, IReadOnlyList<ComponentInstance> initialComponents, string code)
    {
        Owner = owner;
        Code = code;
        components = new List<ComponentInstance>(initialComponents);
    }

    /// <summary>
    /// Convenience constructor: creates a default form with initial Form component.
    /// The caller provides the concrete IComponentClass for "Form" (e.g. FormComponentClass.Instance).
    /// </summary>
    /// <remarks>
    /// Not what a plain form is created with: that is <see cref="NewForm"/>. This also builds the designer half of
    /// a new UserControl or PropertyPage, whose root type is set only afterwards, so nothing here may assume a form.
    /// Its seed is CRLF and terminated all the same, as VB6 writes every line of a designer file (#931).
    /// </remarks>
    public FormDefinition(ProjectDefinition owner, IComponentClass formComponentClass, string name)
    {
        Owner = owner;
        Code = "Private Sub Form_Load()\r\n\r\nEnd Sub\r\n";
        components = new List<ComponentInstance>
        {
            new ComponentInstance(formComponentClass, name)
                .SetProperty(VBProperties.WidthProperty, 400d)
                .SetProperty(VBProperties.HeightProperty, 300d)
                .SetProperty(VBProperties.CaptionProperty, name)
        };
    }

    /// <summary>
    /// A plain form as Add Form, the Project Explorer's Add Form and a new Standard EXE create it, carrying what
    /// VB6 writes for one and HexIDE did not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The five <c>Attribute</c> lines</b>, as nineteen of the twenty forms in VB6's Template tree carry them,
    /// its new project's <c>Form1.frm</c> among them (#516). That is what VB6 writes, not what it needs: measured, a
    /// form without them builds and has its default instance alike, and its <c>Begin</c> line names it. A rename keeps
    /// <c>VB_Name</c> in step. In the code, so the code window shows what the file will say, and greys and folds them
    /// as it does a form read from disk.
    /// </para>
    /// <para>
    /// <b><c>StartUpPosition = 3</c>, Windows Default</b>, which VB6 writes for a new form. The property's default
    /// is 0, Manual, because that is what VB6 reads a missing line as (#932, measured), so a form that does not
    /// say 3 would open at the screen's origin, its caption off the top.
    /// </para>
    /// <para>
    /// Not for the designer half of a UserControl or PropertyPage. No VB6-authored one in the corpus carries a
    /// <c>StartUpPosition</c> line, and their <c>Attribute</c> block differs (<c>Gauge.ctl</c> has
    /// <c>VB_PredeclaredId = False</c>), so nothing of a form's is written into one.
    /// </para>
    /// </remarks>
    public static FormDefinition NewForm(ProjectDefinition owner, IComponentClass formComponentClass, string name)
    {
        var form = new FormDefinition(owner, formComponentClass, name);
        form.Code = $"Attribute VB_Name = \"{name}\"\r\n"
                  + "Attribute VB_GlobalNameSpace = False\r\n"
                  + "Attribute VB_Creatable = False\r\n"
                  + "Attribute VB_PredeclaredId = True\r\n"
                  + "Attribute VB_Exposed = False\r\n"
                  + form.Code;
        form.components[0].SetProperty(VBProperties.StartUpPositionProperty, VBStartupPosition.StartUpWindowsDefault);
        return form;
    }

    private string? absolutePath;
    private List<ComponentInstance> components;

    /// <summary>
    /// The module this form is the designer half of — a UserControl or PropertyPage — or null for a plain
    /// form.
    /// </summary>
    /// <remarks>
    /// A <c>.ctl</c> or <c>.pag</c> is one file with two halves and one identity, and that identity is the
    /// module's. Recorded here by <see cref="ModuleDefinition.UpdateFormPart"/> rather than found by
    /// searching, so it is already right in the window between the two being joined and the module being
    /// added to the project.
    /// </remarks>
    public ModuleDefinition? OwningModule { get; private set; }

    internal void SetOwningModule(ModuleDefinition? module)
    {
        // A half let go keeps the file's encoding, not whatever it held before it was joined.
        if (module is null && OwningModule is { } previous)
            textEncoding = previous.TextEncoding;
        OwningModule = module;
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    public IReadOnlyList<ComponentInstance> Components => components;

    public string Code { get; private set; }

    private bool lockControls;
    public bool LockControls
    {
        get => lockControls;
        set => SetField(ref lockControls, value);
    }

    public string Name
    {
        get
        {
            foreach (var c in components)
            {
                if (c.BaseClass.VBTypeName == "VB.Form")
                    return c.GetPropertyOrDefault(VBProperties.NameProperty) ?? throw new Exception("Form without a name!");
            }
            throw new Exception("FormDefinition has no form component!");
        }
    }

    public void UpdateCode(string newCode) => Code = newCode;

    public void UpdateComponents(IReadOnlyList<ComponentInstance> components)
    {
        this.components.Clear();
        this.components.AddRange(components);
        NotifyRootPropertiesChanged();
    }

    /// <summary>
    /// Announces that the root component's properties changed in place, so <see cref="Name"/> — which is
    /// derived from them and has no setter of its own — is read again.
    /// </summary>
    /// <remarks>
    /// <see cref="UpdateComponents"/> does this as a side effect of replacing the list, which covers every
    /// path that goes through the designer's flush. A path that mutates a component the list already holds
    /// has nothing to replace and has to say so itself — automation's <c>set_control_property</c> with no
    /// designer open is the one that does. Renaming a form through it is otherwise invisible to the tab
    /// title and to the language layer, which is the half of hexide-io/HexIDE#273 task 2.4a deferred to 3.3.
    /// </remarks>
    public void NotifyRootPropertiesChanged() =>
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));

    public string RootVBTypeName { get; private set; } = "VB.Form";

    /// <summary>
    /// Raw text blocks for child component types that HexIDE does not support — each the full
    /// <c>Begin</c>…<c>End</c> block, preserved verbatim for round-trip fidelity.
    ///
    /// Derived, not stored. The blocks live on the <see cref="ComponentInstance"/> they were read from
    /// (<see cref="ComponentInstance.PreservedChildSubtrees"/>), because a block re-emitted at form level
    /// has been silently re-parented. This is the flat pre-order view of them, which is all any caller
    /// outside the serializer ever wanted.
    /// </summary>
    public IReadOnlyList<string> UnknownChildSubtreeTexts =>
        components.SelectMany(c => c.PreservedChildSubtrees)
                  .OrderBy(s => s.DocumentOrder)
                  .Select(s => s.Text)
                  .ToList();

    /// <summary>
    /// Lines between the VERSION line and the root Begin — in practice the OCX declarations a form
    /// needs (Object = "{GUID}#2.0#0"; "mscomctl.ocx"). Preserved verbatim: HexIDE cannot host an
    /// ActiveX control, but it must not corrupt the declaration of one, exactly as the .vbp side
    /// already preserves its Object= references.
    /// </summary>
    public List<string> HeaderLines { get; } = [];

    /// <summary>
    /// This form's designer half as its file has it: the first line of the file through the root <c>End</c>
    /// inclusive, as read, or as last rendered — by the write that created the file, a committed designer change,
    /// or a save. Null for a form HexIDE created that none of those has rendered yet: one with no file, untouched
    /// in the designer (hexide-io/HexIDE#885).
    /// </summary>
    /// <remarks>
    /// <b>Kept as text as well as parsed into <see cref="Components"/>, because the two answer different
    /// questions.</b> The components are what the designer edits and what a save re-renders. This is what
    /// the file actually said, and phase 3 of hexide-io/HexIDE#273 puts it in front of the developer as the
    /// folded, read-only head of the code window — so that a line number means the same thing to the
    /// editor, a language server, the interpreter and the debugger.
    ///
    /// <para>
    /// It cannot be recovered from the model. The <c>VERSION</c> line is dropped at parse and regenerated
    /// from a literal, and the block's own <c>Begin</c> and <c>End</c> are rebuilt at a computed indent, so
    /// a re-render is a reproduction rather than the text. <see cref="HeaderLines"/> keeps only the run
    /// between them and stays, because the serializer still replays it.
    /// </para>
    ///
    /// <para>
    /// <b>Not named <c>Original</c>-anything on purpose.</b> A save replaces it with the header that save
    /// wrote, because the serializer is not byte-faithful to every file and the buffer has to follow the
    /// file rather than the file it was opened from. It is the current designer text, not the first one.
    /// </para>
    /// </remarks>
    public string? DesignerText { get; private set; }

    /// <summary>
    /// Mirrors <c>ModuleDefinition.RecordOriginalHeader</c>: set by the reader, by a save (the write that creates a
    /// new document's file among them), and by a committed designer change (<c>HeaderRefresher</c>).
    /// </summary>
    public void RecordDesignerText(string? text) => DesignerText = text;

    /// <summary>
    /// Why saving this form would not reproduce it, or null when a save is faithful.
    ///
    /// HexIDE flattens nested <c>Begin</c> blocks — the component list has no parent link — so a menu
    /// hierarchy is destroyed and a container's children are re-parented to the form. VB6 then rejects the
    /// result outright whenever a menu carries a shortcut or a separator, which is nearly every real menu.
    ///
    /// Rather than write that file, HexIDE refuses the save. A refused operation is recoverable; a
    /// silently-mangled project that only fails when the developer goes back to VB6 is not. See
    /// docs/serialization-outcomes.md — this is deliberately moving a defect from outcome 3 to outcome 0.
    /// </summary>
    public string? UnfaithfulSaveReason { get; private set; }

    /// <summary>
    /// Every way in which this form cannot be reproduced, as causes rather than prose. The developer-facing
    /// sentence is built from these in the IDE layer, where a localisation service exists; the runtime
    /// records the cause and nothing more.
    ///
    /// A form can carry more than one — <c>Splash Screen.frm</c> is nested inside containers *and* holds
    /// binary content HexIDE cannot re-emit — so these are OR-ed together and never cleared.
    /// </summary>
    public UnfaithfulSaveCause UnfaithfulSaveCauses { get; private set; } = UnfaithfulSaveCause.None;

    /// <summary>
    /// Records a cause, and a developer-facing English sentence for the log. The sentence is deliberately
    /// not what the user sees: <see cref="UnfaithfulSaveCauses"/> is, via a localisation key.
    /// </summary>
    public void MarkUnfaithfulToSave(UnfaithfulSaveCause cause, string developerReason)
    {
        UnfaithfulSaveCauses |= cause;
        // Keep the first sentence rather than the last, so the log names the cause found earliest in the
        // load rather than whichever check happened to run last.
        UnfaithfulSaveReason ??= developerReason;
    }

    public bool CanSaveFaithfully => UnfaithfulSaveCauses == UnfaithfulSaveCause.None;

    /// <summary>
    /// The designer root's OUTER window rectangle, when the file declared one — held as the offset in
    /// twips from its CLIENT rectangle. Null, which is the usual case, means the file declared only a
    /// client rectangle and a save must not invent an outer one.
    ///
    /// A .frm records the root's geometry as <c>ClientLeft</c>/<c>ClientTop</c>/<c>ClientWidth</c>/
    /// <c>ClientHeight</c>. Nineteen of the twenty-two designer files in VB6's own Template tree stop
    /// there. <c>Forms\Dialog.frm</c> is the one that also writes <c>Left</c>/<c>Top</c>/<c>Width</c>/
    /// <c>Height</c>, and those numbers are not the same numbers:
    ///
    /// <code>
    ///   ClientLeft   2760     Left     2700      (-60)
    ///   ClientTop    3750     Top      3405     (-345)
    ///   ClientWidth  6030     Width    6150     (+120)
    ///   ClientHeight 3195     Height   3600     (+405)
    /// </code>
    ///
    /// That difference is the window frame of its <c>BorderStyle = 3 'Fixed Dialog</c>, and it is
    /// self-consistent — 60 twips of border on each side, 345 of caption-plus-top-border against 60 of
    /// bottom border. So the two rectangles are genuinely different things.
    ///
    /// <see cref="ComponentInstance"/> holds the CLIENT rectangle, because that is the rectangle every
    /// control on the form is positioned inside and the one Avalonia's <c>Window.Width</c>/<c>Height</c>
    /// already mean. Keeping the outer rectangle as an OFFSET rather than as absolute numbers is what
    /// lets a resize in the designer move both together without HexIDE having to compute a frame size —
    /// which it could not do honestly anyway, since the frame belongs to whichever machine opens the
    /// form, not to the machine that saved it.
    /// </summary>
    public RootOuterRect? OuterRect { get; set; }

    /// <summary>
    /// The designer root's coordinate scale, as its file declared it. Null when the file declared none.
    ///
    /// <c>ScaleWidth</c>/<c>ScaleHeight</c> are expressed in <c>ScaleMode</c>'s units, so they are not the
    /// client rectangle unless that mode happens to be twips. The writer used to copy the client width
    /// straight into <c>ScaleWidth</c> and leave <c>ScaleMode</c> alone, which put the declared scale and
    /// its own numbers a factor of fifteen apart on <c>Colorful Control.ctl</c> — <c>ScaleMode = 3 'Pixel</c>
    /// beside a <c>ScaleWidth</c> in twips.
    ///
    /// For every mode but <see cref="VBScaleMode.User"/> the pair is DERIVED from the client rectangle, so
    /// resizing the form keeps it correct. Twenty of the twenty-two designer files in VB6's Template tree
    /// derive exactly. The other two — <c>About Dialog.frm</c> and <c>Log in Dialog.frm</c> — declare
    /// <c>ScaleMode = 0 'User</c>, where the pair is a coordinate system the developer chose
    /// (<c>ScaleHeight = 2453.724</c> against a <c>ClientHeight</c> of 3555) and nothing can derive it.
    /// Those are preserved as read, which is also proof that VB6 does not recompute them on save.
    /// </summary>
    public RootScale? Scale { get; set; }

    /// <summary>
    /// Replaces this form's reproduction verdict, and the state it was derived from, with a freshly-parsed
    /// one. For the reload path, where the file on disk has changed underneath an open form.
    ///
    /// Assignment rather than accumulation, which is the opposite of <see cref="MarkUnfaithfulToSave"/> and
    /// deliberately so: within one load the causes OR together and never clear, but a RELOAD is a new load of
    /// a different file. A form that had its unhostable nesting fixed externally has to stop being read-only,
    /// and a form that gained a blob-backed property has to start.
    ///
    /// Adopting only Code and Components — which is what the reload used to do — leaves the verdict describing
    /// a file that no longer exists: the banner keeps saying a form cannot be saved after the reason has been
    /// removed, or worse, stops saying it after the reason has been introduced.
    /// </summary>
    public void AdoptFidelityState(FormDefinition fresh)
    {
        UnfaithfulSaveCauses = fresh.UnfaithfulSaveCauses;
        UnfaithfulSaveReason = fresh.UnfaithfulSaveReason;
        MaxUnreproducibleNestingDepth = fresh.MaxUnreproducibleNestingDepth;
        HasUnmodelledBinaryProperties = fresh.HasUnmodelledBinaryProperties;
        LoadedCompanionBlobCount = fresh.LoadedCompanionBlobCount;
        OuterRect = fresh.OuterRect;
        Scale = fresh.Scale;

        // The OCX declarations between VERSION and the root Begin. Not fidelity as such, but read from the
        // same file and just as stale: keeping the old ones would write another file's Object= lines back.
        HeaderLines.Clear();
        HeaderLines.AddRange(fresh.HeaderLines);

        // The designer half as the new file says it, for the same reason (#273 task 3.1). Keeping the old
        // text would put the previous file's header in the code window and write it back on the next save.
        DesignerText = fresh.DesignerText;

        // ── Two more that were read from the file and were NOT being adopted ─────────────────────
        //
        // Found while adding the line above, and the first one is a data-loss path rather than a staleness
        // one (hexide-io/HexIDE#506). CitedCompanionBlobCount is the guard on deleting the companion
        // binary: ProjectService leaves the .frx/.ctx/.pgx alone when the form cites nothing, because a
        // companion this form never referenced holds bytes that exist nowhere else. A reload that keeps a
        // stale NON-zero count defeats that guard, so a form whose citations were removed externally and
        // then saved deletes a file HexIDE cannot reproduce.
        //
        // LockControls is the milder sibling: read from the designer root at load, written back on save,
        // and otherwise frozen at whatever the file said when it was first opened.
        CitedCompanionBlobCount = fresh.CitedCompanionBlobCount;
        LockControls = fresh.LockControls;
    }

    /// <summary>
    /// True when loading this form met a property HexIDE does not model, or a line of an unmodelled control, that
    /// cites a record its companion binary (.frx/.ctx/.pgx) does not hold. Such a property is carried with its
    /// record when the companion has it (#60). Without the record, it is dropped, so a save cannot reproduce it.
    ///
    /// The save path MUST leave the companion file completely alone when this is set: writing a
    /// regenerated companion truncates it (Splash Screen.frx: 790 bytes to 12) and writing none at all is
    /// read as "delete it" (Button ListBox.frx: 2122 bytes destroyed). Neither is recoverable — the images
    /// exist nowhere else.
    /// </summary>
    public bool HasUnmodelledBinaryProperties { get; private set; }

    public void MarkUnmodelledBinaryProperty() => HasUnmodelledBinaryProperties = true;

    /// <summary>
    /// How many blobs the companion binary yielded when this form was loaded.
    ///
    /// Informational only. The save path used to compare this against a walk of what it was about to
    /// write, which is what #148 was: two different readers, one of them known to misread <c>List</c> and
    /// <c>ItemData</c>, measuring two different things and disagreeing on files that reproduce perfectly.
    /// Whether a file can be reproduced is decided at load, once, by
    /// <see cref="CanSaveFaithfully"/>.
    /// </summary>
    public int LoadedCompanionBlobCount { get; private set; }

    public void RecordLoadedCompanionBlobCount(int count) => LoadedCompanionBlobCount = count;

    /// <summary>
    /// How many distinct companion offsets this form's designer file CITED when it was loaded.
    ///
    /// Distinct from <see cref="LoadedCompanionBlobCount"/> in the case that matters: a companion nothing
    /// cites is read by falling back to a flat walk, so it yields records while this stays zero. That is
    /// what separates "the developer cleared the last picture" — cited before, cites nothing now, so the
    /// companion is ours to remove — from "a companion this form never referenced", whose bytes we never
    /// modelled and must not delete.
    /// </summary>
    public int CitedCompanionBlobCount { get; private set; }

    public void RecordCitedCompanionBlobCount(int count) => CitedCompanionBlobCount = count;

    /// <summary>
    /// The deepest nesting in this form that the writer cannot reproduce, counting the form itself as 1.
    /// Anything above 2 means a component was nested inside something other than the form and would be
    /// re-parented to the form on save.
    ///
    /// This is deliberately narrower than the raw <c>Begin</c> depth. A menu nested under a menu, or a
    /// menu under the form, is excluded, because the loader records that hierarchy on the parent's
    /// <c>SubItems</c> and the writer walks it back out. Everything else — a control inside a Frame or a
    /// PictureBox — still flattens, so it still counts.
    ///
    /// The distinction is what lets the refusal gate narrow as reproduction improves rather than staying
    /// shut for a defect that has since been fixed.
    /// </summary>
    public int MaxUnreproducibleNestingDepth { get; private set; }

    public void RecordUnreproducibleNestingDepth(int depth) => MaxUnreproducibleNestingDepth = depth;

    public void UpdateRootTypeName(string typeName) => RootVBTypeName = typeName;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Overload accepting PropertyChangedEventArgs (used by UpdateComponents)
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}

/// <summary>
/// A designer root's outer window rectangle, expressed as the offset in twips from its client
/// rectangle. See <see cref="FormDefinition.OuterRect"/> for why it is an offset and not a rectangle.
///
/// The four axes are recorded and written as a set. VB6 writes them as a set, and a file that declared
/// only some of them is malformed rather than merely unusual — reproducing that partial set exactly
/// would mean four independent nullables threaded through the writer to preserve a shape no VB6 has
/// ever emitted.
/// </summary>
public sealed record RootOuterRect(
    double LeftOffsetTwips,
    double TopOffsetTwips,
    double WidthOffsetTwips,
    double HeightOffsetTwips);

/// <summary>
/// A designer root's declared coordinate scale. See <see cref="FormDefinition.Scale"/>.
///
/// <paramref name="Mode"/> is the raw number from the file rather than a <see cref="VBScaleMode"/>,
/// because ScaleMode is not a modelled property — it survives as a preserved raw line, comment and all,
/// and a value outside the enum is input to be carried rather than input to be rejected.
///
/// <paramref name="Width"/> and <paramref name="Height"/> are null when the file declared no Scale* pair,
/// which is what tells the writer it has nothing to preserve for a user scale.
/// </summary>
public sealed record RootScale(int Mode, double? Width, double? Height);
