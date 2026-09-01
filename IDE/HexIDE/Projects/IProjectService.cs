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
    Task<FormDefinition> AddNewForm(ProjectDefinition project, string name);
    Task<ModuleDefinition> AddNewModule(ProjectDefinition project, string name, ModuleKind kind);
    Task<ModuleDefinition> AddNewUserControl(ProjectDefinition project, string name);
    Task<ModuleDefinition> AddNewPropertyPage(ProjectDefinition project, string name);
    Task<bool> SaveForm(FormDefinition form, bool saveAs);
    Task<bool> SaveModule(ModuleDefinition module, bool saveAs);

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

    Task MakeProject();
    Task MakeProject(ProjectDefinition project);
    Task MakeProjectGroup();
    Task EditProjectProperties(ProjectDefinition project);
    Task EditProjectReferences(ProjectDefinition project);
    Task EditProjectComponents(ProjectDefinition project);
}