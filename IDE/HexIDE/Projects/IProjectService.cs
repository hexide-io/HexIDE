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
    Task SaveForm(FormDefinition form, bool saveAs);
    Task SaveModule(ModuleDefinition module, bool saveAs);

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
    Task MakeProject();
    Task MakeProject(ProjectDefinition project);
    Task MakeProjectGroup();
    Task EditProjectProperties(ProjectDefinition project);
    Task EditProjectReferences(ProjectDefinition project);
    Task EditProjectComponents(ProjectDefinition project);
}