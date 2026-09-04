using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.ProjectElements;

public partial class ProjectDefinition : INotifyPropertyChanged
{
    private FormDefinition? startupForm;
    private bool startsAtSubMain;
    private string? absolutePath;
    private string name;
    private string description = "";
    private VBProjectType projectType;

    private List<FormDefinition> forms = new();
    private List<ModuleDefinition> modules = new();
    private List<RelatedDocumentDefinition> relatedDocuments = new();
    private List<VbReference> references = new();

    public ProjectDefinition(VBProjectType projectType, string name)
    {
        this.name = name;
        this.projectType = projectType;
    }

    public FormDefinition? StartupForm
    {
        get => startupForm;
        set
        {
            // The two are mutually exclusive: a project starts at a form OR at Sub Main, never both.
            if (value is not null) startsAtSubMain = false;
            SetField(ref startupForm, value);
        }
    }

    /// <summary>
    /// True when the project's startup object is <c>Sub Main</c> rather than a form —
    /// <c>Startup="Sub Main"</c> in the .vbp, and the ordinary shape of a code-only Standard EXE.
    /// </summary>
    ///
    /// <remarks>
    /// The startup object is a user choice, not a property of the project type: VB6's Project Properties
    /// dialog lists <c>Sub Main</c> alongside every form. Modelling it as a nullable
    /// <see cref="StartupForm"/> alone left <c>Sub Main</c> with nowhere to live, so a project with no
    /// forms could not run at all (#210).
    ///
    /// <para>
    /// The third state VB6 allows — <c>Startup="(None)"</c> — needs no flag of its own: it is simply
    /// neither, which is what a project with no matching form and this false already means.
    /// </para>
    /// </remarks>
    public bool StartsAtSubMain
    {
        get => startsAtSubMain;
        set
        {
            if (value) startupForm = null;
            SetField(ref startsAtSubMain, value);
        }
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string Description
    {
        get => description;
        set => SetField(ref description, value);
    }

    public VBProjectType ProjectType
    {
        get => projectType;
        set => SetField(ref projectType, value);
    }

    public IReadOnlyList<FormDefinition> Forms => forms;
    public IReadOnlyList<ModuleDefinition> Modules => modules;

    /// <summary>
    /// Files the project carries but does not compile. Separate from <see cref="Modules"/> on purpose —
    /// see <see cref="RelatedDocumentDefinition"/>: everything that iterates Modules would otherwise need a
    /// guard, and a missed guard damages a file the developer never edited.
    /// </summary>
    public IReadOnlyList<RelatedDocumentDefinition> RelatedDocuments => relatedDocuments;
    public IReadOnlyList<VbReference> References => references;

    // Unknown key=value lines from the .vbp file (before any [Section] header), preserved verbatim.
    // PositionHint = count of recognised keys seen before each unknown line.
    public List<(int PositionHint, string RawLine)> UnknownPreSectionLines { get; } = new();

    // Verbatim .vbp item lines (Form=/UserDocument=/...) for project items that were parsed but not
    // loaded into the live model — e.g. a UserDocument (.dob, unsupported) or a Form whose .frm file is
    // missing on disk. Carried so an open->save round-trip never drops the node, even on a host that
    // lacks the file or a non-Windows machine. (A future spec may surface these in the project explorer
    // with a "missing"/"unsupported" glyph; for now they are preserved opaquely.)
    public List<string> PreservedItemLines { get; } = new();

    // The startup form's NAME as read from the .vbp. Retained even when the form itself couldn't be
    // loaded (missing .frm), so the Startup= line survives round-trip. StartupForm.Name takes precedence.
    public string? StartupFormName { get; set; }

    // Everything from the first [SectionName] line to EOF in the .vbp, preserved verbatim.
    public string? ExtensionTail { get; set; }

    public void AddReference(VbReference r)
    {
        if (!references.Contains(r))
            references.Add(r);
    }

    public void RemoveReference(VbReference r) => references.Remove(r);

    public void SetReferences(IEnumerable<VbReference> newReferences)
    {
        references.Clear();
        references.AddRange(newReferences);
    }

    public event Action<ProjectDefinition, FormDefinition>? FormAdded;
    public event Action<ProjectDefinition, FormDefinition>? FormDeleted;
    public event Action<ProjectDefinition, ModuleDefinition>? ModuleAdded;
    public event Action<ProjectDefinition, ModuleDefinition>? ModuleDeleted;
    public event Action<ProjectDefinition, RelatedDocumentDefinition>? RelatedDocumentAdded;
    public event Action<ProjectDefinition, RelatedDocumentDefinition>? RelatedDocumentDeleted;

    public void AddForm(FormDefinition form)
    {
        forms.Add(form);
        FormAdded?.Invoke(this, form);
        // The first form added becomes the startup — but only if the project does not already start
        // somewhere. Without the second condition, adding a form to a code-only project would silently
        // move its startup object off Sub Main, which is a change the user did not ask for and would only
        // discover on the next F5.
        if (startupForm is null && !startsAtSubMain)
            StartupForm = form;
    }

    public void DeleteForm(FormDefinition form)
    {
        forms.Remove(form);
        FormDeleted?.Invoke(this, form);
        if (startupForm == form)
        {
            startupForm = forms.FirstOrDefault();
        }
    }

    public void AddModule(ModuleDefinition module)
    {
        modules.Add(module);
        ModuleAdded?.Invoke(this, module);
    }

    public void DeleteModule(ModuleDefinition module)
    {
        modules.Remove(module);
        ModuleDeleted?.Invoke(this, module);
    }

    public void AddRelatedDocument(RelatedDocumentDefinition document)
    {
        // Note what is deliberately absent, compared with AddForm: no startup-object side effect. A related
        // document is never a startup object, so adding one must not touch how the project runs.
        relatedDocuments.Add(document);
        RelatedDocumentAdded?.Invoke(this, document);
    }

    public void DeleteRelatedDocument(RelatedDocumentDefinition document)
    {
        relatedDocuments.Remove(document);
        RelatedDocumentDeleted?.Invoke(this, document);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
