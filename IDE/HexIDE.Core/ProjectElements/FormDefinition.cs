using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.ProjectElements;

public partial class FormDefinition : INotifyPropertyChanged
{
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
    public FormDefinition(ProjectDefinition owner, IComponentClass formComponentClass, string name)
    {
        Owner = owner;
        Code = "Private Sub Form_Load()\n\nEnd Sub";
        components = new List<ComponentInstance>
        {
            new ComponentInstance(formComponentClass, name)
                .SetProperty(VBProperties.WidthProperty, 400d)
                .SetProperty(VBProperties.HeightProperty, 300d)
                .SetProperty(VBProperties.CaptionProperty, name)
        };
    }

    private string? absolutePath;
    private List<ComponentInstance> components;

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
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
    }

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

        // The OCX declarations between VERSION and the root Begin. Not fidelity as such, but read from the
        // same file and just as stale: keeping the old ones would write another file's Object= lines back.
        HeaderLines.Clear();
        HeaderLines.AddRange(fresh.HeaderLines);
    }

    /// <summary>
    /// True when loading this form encountered a property backed by the companion binary (.frx/.ctx/.pgx)
    /// that HexIDE does not model — a <c>DragIcon</c>, a <c>CommandButton.Picture</c>, an <c>ItemData</c>.
    /// The property line is dropped, so a save cannot reproduce the blob it pointed at.
    ///
    /// The save path MUST leave the companion file completely alone when this is set: writing a
    /// regenerated companion truncates it (Splash Screen.frx: 790 bytes to 12) and writing none at all is
    /// read as "delete it" (Button ListBox.frx: 2122 bytes destroyed). Neither is recoverable — the images
    /// exist nowhere else.
    /// </summary>
    public bool HasUnmodelledBinaryProperties { get; private set; }

    public void MarkUnmodelledBinaryProperty() => HasUnmodelledBinaryProperties = true;

    /// <summary>
    /// How many blobs the companion binary held when this form was loaded. The save path compares this
    /// against what it is about to write: fewer blobs out than in means something was lost on a path
    /// nobody flagged, which is the general safety net behind
    /// <see cref="HasUnmodelledBinaryProperties"/>. Properties that are named but whose CLR type is
    /// unmapped (ComboBox <c>List</c>, <c>ItemData</c>) are dropped with no diagnostic at all, so an
    /// explicit flag alone cannot be trusted to catch every case.
    /// </summary>
    public int LoadedCompanionBlobCount { get; private set; }

    public void RecordLoadedCompanionBlobCount(int count) => LoadedCompanionBlobCount = count;

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
