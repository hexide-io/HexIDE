using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// One row of the project document's member list: a form, module, class, user control, property page
/// or carried file, with the localized name of what kind of thing it is.
/// </summary>
/// <param name="Kind">
/// Already localized. The kinds are named by the same <c>Str.AddItem.*</c> keys the Add menu uses, so the
/// document and the menu that creates its contents cannot drift apart in wording.
/// </param>
public sealed record ProjectMemberRow(string Kind, string Name, string? File);

/// <summary>
/// A read-only view of what a project contains.
///
/// <para>
/// <b>It renders the project, not the <c>.vbp</c>.</b> Those are different things and the difference is
/// the whole design. The file is one serialization of the project as it was last saved; the
/// <see cref="ProjectDefinition"/> is the project as it is now, which is what every other surface in the
/// IDE is already showing. Rendering the model means this document agrees with the Project Explorer beside
/// it the moment a module is added, with no file watcher and nothing to reconcile — and it means a project
/// that has never been saved, and therefore has no file at all, still has something to show.
/// </para>
///
/// <para>
/// <b>Read-only, deliberately.</b> Editing would put two authorities over one file and require merging text
/// edits back into a live model — which is the part of this idea that goes wrong in the IDEs that attempt
/// it. There is no merge to get wrong if there is no write path.
/// </para>
///
/// <para>
/// The labels are the model's vocabulary (project type, startup object, members) rather than the file's
/// tokens (<c>Type=</c>, <c>Startup=</c>, <c>Class=</c>). A view captioned with the file's keys would be
/// claiming to show the file, and for an unsaved project those keys describe lines that do not exist.
/// </para>
/// </summary>
public partial class ProjectDocumentViewModel : BaseEditorWindowViewModel
{
    private readonly ILocalizationService localization;
    private ProjectDefinition? project;

    public ProjectDocumentViewModel(ILocalizationService localization) => this.localization = localization;

    public ProjectDefinition? Project => project;

    /// <summary>Where the project lives, or the reason there is nowhere.</summary>
    [Notify] private string location = string.Empty;

    /// <summary>Whether <see cref="Location"/> is a path rather than the never-saved message.</summary>
    [Notify] private bool hasLocation;

    [Notify] private string projectName = string.Empty;
    [Notify] private string projectType = string.Empty;
    [Notify] [AlsoNotify(nameof(HasDescription))] private string description = string.Empty;

    /// <summary>
    /// Whether there is a description to show. A real bool rather than negating the string in the view:
    /// a compiled binding will not coerce a string to a boolean, so the caption showed for every project
    /// without one.
    /// </summary>
    public bool HasDescription => !string.IsNullOrWhiteSpace(Description);
    [Notify] private string startupObject = string.Empty;

    public ObservableCollection<ProjectMemberRow> Members { get; } = [];

    /// <summary>
    /// References as one display line each. A <see cref="VbReference"/> is a GUID, a version, an LCID, a
    /// path and a name, and only the last two mean anything to a reader — but the name is optional in the
    /// file, so the path and finally the GUID stand in for it rather than leaving a blank row.
    /// </summary>
    public ObservableCollection<string> References { get; } = [];

    public override object? Icon => null;

    protected override string ComputeTitle() =>
        project == null ? string.Empty : FileNameOrName(project);

    public ProjectDocumentViewModel Initialize(ProjectDefinition definition)
    {
        project = definition;

        // The model is live, so the document tracks it without a watcher. Every one of these has to come
        // back off in Dispose: a closed tab still holding a handler keeps the whole view model alive for
        // as long as the project is loaded, and nothing in a test or a screenshot would ever show it.
        definition.PropertyChanged += OnProjectChanged;
        definition.FormAdded += OnMembershipChanged;
        definition.FormDeleted += OnMembershipChanged;
        definition.ModuleAdded += OnMembershipChanged;
        definition.ModuleDeleted += OnMembershipChanged;
        definition.RelatedDocumentAdded += OnMembershipChanged;
        definition.RelatedDocumentDeleted += OnMembershipChanged;

        Refresh();
        Title = ComputeTitle();
        return this;
    }

    private void OnProjectChanged(object? sender, PropertyChangedEventArgs e)
    {
        Refresh();
        Title = ComputeTitle();
    }

    private void OnMembershipChanged<T>(ProjectDefinition _, T __) => Refresh();

    private void Refresh()
    {
        if (project == null) return;

        ProjectName = project.Name;
        ProjectType = ProjectTypeName(project.ProjectType);
        Description = project.Description;
        StartupObject = StartupName(project);

        HasLocation = !string.IsNullOrEmpty(project.AbsolutePath);
        Location = HasLocation
            ? project.AbsolutePath!
            : localization.GetString("Str.ProjectDocument.NotSaved");

        Members.Clear();
        foreach (var row in BuildMembers(project))
            Members.Add(row);

        References.Clear();
        foreach (var reference in project.References)
            References.Add(ReferenceDisplay(reference));
    }

    private static string ReferenceDisplay(VbReference reference)
    {
        var label = FirstNonEmpty(reference.Name, FileNameOf(reference.LibPath), reference.Guid);
        return string.IsNullOrEmpty(reference.Version) ? label : $"{label}  ({reference.Version})";
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate))
                return candidate;
        return string.Empty;
    }

    private IEnumerable<ProjectMemberRow> BuildMembers(ProjectDefinition definition)
    {
        // Forms first, then modules by kind, then carried files — the order the Add menu offers them,
        // rather than the order the .vbp happens to list them in.
        foreach (var form in definition.Forms)
            yield return new(localization.GetString("Str.AddItem.Form"), form.Name, FileNameOf(form.AbsolutePath));

        foreach (var module in definition.Modules.OrderBy(m => m.Kind))
            yield return new(ModuleKindName(module.Kind), module.Name, FileNameOf(module.AbsolutePath));

        foreach (var carried in definition.RelatedDocuments)
            yield return new(
                localization.GetString("Str.ProjectDocument.Kind.RelatedDocument"),
                carried.Name,
                FileNameOf(carried.AbsolutePath));
    }

    private string ModuleKindName(ModuleKind kind) => localization.GetString(kind switch
    {
        ModuleKind.ClassModule => "Str.AddItem.ClassModule",
        ModuleKind.UserControl => "Str.AddItem.UserControl",
        ModuleKind.PropertyPage => "Str.AddItem.PropertyPage",
        _ => "Str.AddItem.Module",
    });

    /// <summary>
    /// The enum spells these the way the <c>.vbp</c>'s <c>Type=</c> line does; the IDE has always shown
    /// users the VB6 dialog wording, and this uses the same four keys the New Project dialog does.
    /// </summary>
    private string ProjectTypeName(VBProjectType type) => type switch
    {
        VBProjectType.EXE => localization.GetString("Str.ProjectType.StandardEXE"),
        VBProjectType.OleExe => localization.GetString("Str.ProjectType.ActiveXEXE"),
        VBProjectType.OleDll => localization.GetString("Str.ProjectType.ActiveXDLL"),
        VBProjectType.Control => localization.GetString("Str.ProjectType.ActiveXControl"),
        _ => type.ToString(),
    };

    /// <summary>
    /// What runs first. <c>Sub Main</c> is a literal of the language rather than prose, so it is not a
    /// localization key — the same reason a keyword is not translated anywhere else in the IDE.
    /// </summary>
    private static string StartupName(ProjectDefinition definition) =>
        definition.StartsAtSubMain ? "Sub Main"
        : definition.StartupForm?.Name
          ?? definition.StartupFormName
          ?? string.Empty;

    /// <summary>
    /// The file's own name, taken with the VB6-native helper rather than <see cref="System.IO.Path"/>:
    /// these paths came out of a <c>.vbp</c> and are backslash-separated on every host, which
    /// <c>Path.GetFileName</c> answers wrongly on Linux.
    /// </summary>
    private static string? FileNameOf(string? absolutePath) =>
        string.IsNullOrEmpty(absolutePath) ? null : SerializedProject.FileNameOf(absolutePath);

    private static string FileNameOrName(ProjectDefinition definition) =>
        FileNameOf(definition.AbsolutePath) ?? definition.Name;

    public override void Dispose()
    {
        if (project != null)
        {
            project.PropertyChanged -= OnProjectChanged;
            project.FormAdded -= OnMembershipChanged;
            project.FormDeleted -= OnMembershipChanged;
            project.ModuleAdded -= OnMembershipChanged;
            project.ModuleDeleted -= OnMembershipChanged;
            project.RelatedDocumentAdded -= OnMembershipChanged;
            project.RelatedDocumentDeleted -= OnMembershipChanged;
            project = null;
        }

        base.Dispose();
    }
}
