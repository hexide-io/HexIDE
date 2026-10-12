using System.Threading.Tasks;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

public interface IProjectService
{
    Task CreateNewProject();
    Task CreateNewProject(IProjectTemplate template);
    Task UnloadAllProjects();
    Task UnloadProject(ProjectDefinition project);
    Task OpenProject();
    Task OpenProject(string path);
    Task SaveProject(ProjectDefinition project, bool saveAs);
    Task SaveProjectToDirectory(ProjectDefinition project, string directory);
    Task SaveAllProjects(bool saveAs);
    /// <summary>Writes a new form's file and adds the form to <paramref name="project"/>.</summary>
    /// <param name="interactive">As for <see cref="SaveForm"/>.</param>
    /// <exception cref="HexIDE.IDE.SourceEncodingRefusal">The new file's code page, or the project file's, cannot
    /// hold <paramref name="name"/>. Nothing was created and nothing was added.</exception>
    Task<FormDefinition> AddNewForm(ProjectDefinition project, string name, bool interactive = true);
    Task<ModuleDefinition> AddNewModule(ProjectDefinition project, string name, ModuleKind kind, bool interactive = true);
    Task<ModuleDefinition> AddNewUserControl(ProjectDefinition project, string name, bool interactive = true);
    Task<ModuleDefinition> AddNewPropertyPage(ProjectDefinition project, string name, bool interactive = true);

    /// <summary>
    /// Adds a <c>.frm</c> that already exists on disk.
    /// </summary>
    /// <remarks>
    /// Nothing is added when the file cannot be parsed — a form HexIDE cannot read is not one it should
    /// pretend to carry — and nothing is added when the name the file asks for is one this project already
    /// uses, or is not a VB6 name at all. The result says which, because Add File takes several files at
    /// once and "one of them was refused" is not an answer anybody can act on.
    /// </remarks>
    Task<Adopted<FormDefinition>> AddExistingForm(ProjectDefinition project, string absolutePath);

    /// <summary>
    /// Adds a module file that already exists on disk, as <paramref name="kind"/>.
    ///
    /// <para>
    /// The counterpart to <see cref="AddNewModule"/>: that one authors a file and then adds it, this one
    /// adopts a file the developer already has. It reads through the very same path project load uses, so
    /// an adopted module is indistinguishable from one that arrived in the <c>.vbp</c> — same preserved
    /// header, same on-disk baseline, same companion blob handling.
    /// </para>
    /// </summary>
    Task<Adopted<ModuleDefinition>> AddExistingModule(
        ProjectDefinition project, string absolutePath, ModuleKind kind);

    /// <summary>
    /// Adds a file the project will carry but never compile. Nothing is read or written — a related
    /// document is a path and a name, and the editor reads it on demand.
    /// </summary>
    Task<RelatedDocumentDefinition> AddExistingRelatedDocument(ProjectDefinition project, string absolutePath);
    /// <summary>
    /// Saves one form, asking where when it has no file yet or <paramref name="saveAs"/> is set. Returns false
    /// when HexIDE cannot reproduce it, and nothing was written.
    /// </summary>
    /// <param name="interactive">
    /// False for an automation caller. No refusal is shown as a dialog: it comes back as false, or as the
    /// <see cref="HexIDE.IDE.SourceEncodingRefusal"/> when the code page cannot hold the text, which names the
    /// character. A dialog would hold the caller's reply until a person dismissed it. The picker is NOT
    /// suppressed, so a caller must not ask for one nobody will answer: check
    /// <see cref="HexIDE.IDE.ScriptedFileDialogs.WouldShowPicker"/> first.
    /// </param>
    /// <exception cref="HexIDE.IDE.SourceEncodingRefusal">The form's code page cannot hold its text. Nothing
    /// was written; a person has been shown the warning unless <paramref name="interactive"/> is false.</exception>
    Task<bool> SaveForm(FormDefinition form, bool saveAs, bool interactive = true);

    /// <summary>Saves one module, as <see cref="SaveForm"/> saves a form.</summary>
    /// <param name="interactive">As for <see cref="SaveForm"/>.</param>
    Task<bool> SaveModule(ModuleDefinition module, bool saveAs, bool interactive = true);

    /// <summary>
    /// Re-reads <paramref name="form"/>'s <c>.frm</c> (and companion <c>.frx</c>) from disk and updates the
    /// existing in-memory model in place (code + components), refreshing the on-disk baseline. Returns
    /// false if the file is missing or fails to parse. Used by the file watcher to adopt external changes.
    /// </summary>
    Task<bool> ReloadFormFromDisk(FormDefinition form);

    /// <summary>
    /// Re-reads <paramref name="module"/>'s source from disk and updates the existing in-memory model in
    /// place (code, plus the FormPart for UserControl/PropertyPage), refreshing the on-disk baseline.
    /// Returns false if the file is missing. Used by the file watcher to adopt external changes.
    /// </summary>
    Task<bool> ReloadModuleFromDisk(ModuleDefinition module);

    /// <summary>
    /// True when saving <paramref name="form"/> would change what is on disk — i.e. the in-memory model
    /// holds edits that have not been written. The form is rendered through the very serializer its save
    /// path uses and compared against the baseline recorded at load/save/reload, so the answer cannot
    /// drift from what a save would actually write.
    ///
    /// Used by the file watcher to classify a <b>not-open</b> form: a <c>.frm</c> is layout + code, so its
    /// disk bytes cannot be hashed against an editor buffer the way a <c>.bas</c> can.
    /// </summary>
    bool HasUnsavedChanges(FormDefinition form);

    /// <summary>The module counterpart: whether saving would change what was last loaded or saved.</summary>
    bool HasUnsavedChanges(ModuleDefinition module);

    Task MakeProject();
    Task MakeProject(ProjectDefinition project);
    Task MakeProjectGroup();
    Task EditProjectProperties(ProjectDefinition project);
    Task EditProjectReferences(ProjectDefinition project);
    Task EditProjectComponents(ProjectDefinition project);
}