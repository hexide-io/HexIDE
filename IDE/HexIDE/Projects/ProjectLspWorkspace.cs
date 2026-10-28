using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.ComponentModel;
using Avalonia.Threading;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.IDE;
using HexIDE.Lsp;

namespace HexIDE.Projects;

/// <summary>
/// Answers "where is the workspace" for the language layer, from whichever project is currently the
/// startup one.
///
/// <para>
/// Deliberately thin, and deliberately here rather than in the language layer. Reusing
/// <see cref="ProjectService.ProjectFilesDirectory"/> is the whole point: that is already the rule
/// deciding where a project's files live, including the fallback for a project that has never been saved,
/// and a second answer to the same question would drift from it.
/// </para>
///
/// <para>
/// <b>An unsaved project's directory is a private temp path that may never exist.</b> It used to be
/// shared — keyed on the project's NAME, so every new "Project1" resolved to the same place, and a server
/// could be rooted among another project's leavings. hexide-io/HexIDE#260 fixed that by appending a GUID,
/// which means the path is now unique per project and, in exchange, <b>does not exist until something is
/// written into it</b>: <c>ProjectFilesDirectory</c> mints and memoises the string and creates nothing. Since
/// hexide-io/HexIDE#500 the Add commands write nothing, as VB6's do, so for a project the developer saves
/// themselves nothing ever does: its first save puts every file where the save's pickers say, and the root
/// moves there. Only <c>--newproject</c> writes into the folder, saving its project file there.
/// </para>
///
/// <para>
/// Answering with it anyway is what this type does, and it is a choice between two costs rather than the
/// right answer. A server configured with a <c>{workspaceUri}</c> or <c>{workspaceDir}</c> placeholder is
/// refused a launch when there is no workspace, and the registry holds that refusal as <c>Failed</c> for the
/// rest of the session, so with no root it would not start even once the first save gave it one. Told a root
/// that does not exist yet, it starts, and the first save restarts it at the real one. What a server does
/// with a root naming nothing has not been measured. Making the refusal wait for a workspace instead, so an
/// unsaved project can have no root, is hexide-io/HexIDE#1382. What must NOT be done with the path is
/// launch a process there — that throws, and cost the whole connection until hexide-io/HexIDE#278; see
/// <c>LspLaunchDirectory</c>, which separates the two questions.
/// </para>
///
/// <para>
/// <b>The project manager arrives as a factory, and it has to.</b> This type closes a dependency cycle —
/// <c>ProjectManager</c> needs an editor service, which builds code editors, which need the language
/// client, which needs this. Pure.DI cannot order a cycle, and it does not refuse one either: it emits the
/// singleton field <em>unguarded</em>, so whichever participant is constructed first receives
/// <see langword="null"/> for the back edge. Taking it as a factory defers the resolution past
/// construction, which is the same way <c>EditorService</c> holds its view-model factories.
/// </para>
///
/// <para>
/// This was not a hypothetical. The direct dependency compiled, passed every test, and made
/// <c>Directory</c> throw a <see cref="NullReferenceException"/> on the first document opened — which
/// <c>CodeEditorViewModel.Initialize</c> logged and swallowed, so no language server ever started and the
/// IDE simply had no language features. Only running it found that.
/// </para>
///
/// <para>
/// The manager is matched rather than dereferenced for the same reason. The factory defers the back edge
/// past construction, but the generated code still reads an unguarded field in some paths, so a null is
/// reachable in principle. This type already has a defined answer for "there is no workspace yet" —
/// <see langword="null"/> — and giving that answer costs a lazily started server nothing, where throwing
/// costs every language feature in the IDE.
/// </para>
/// </summary>
public sealed class ProjectLspWorkspace(Func<IProjectManager> projectManager) : IWorkspaceArtifactWorkspace
{

    private readonly List<Action> _unsubscribe = [];
    private IProjectManager? _observed;
    private IReadOnlyList<WorkspaceArtifactContext>? _projects;
    public event Action? ProjectChanged;

    public IReadOnlyList<WorkspaceArtifactContext> Projects
    {
        get
        {
            if (_observed is null && projectManager() is { } manager)
            {
                _observed = manager;
                manager.ProjectLoaded += MembershipChanged;
                manager.ProjectUnloaded += MembershipChanged;
                Refresh(notify: false);
            }
            return _projects ?? [];
        }
    }

    private void MembershipChanged(ProjectDefinition _) => Refresh();
    private void MaterialChanged(object? sender, PropertyChangedEventArgs e)
    {
        if ((sender is ProjectDefinition && e.PropertyName != nameof(ProjectDefinition.WorkingDirectory))
            || e.PropertyName is "Name" or "AbsolutePath") Refresh();
    }

    private void Refresh(bool notify = true)
    {
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        _unsubscribe.Clear();
        var contexts = new List<WorkspaceArtifactContext>();
        foreach (var project in _observed!.LoadedProjects)
        {
            Observe(project);
            project.FormAdded += FormChanged;
            project.FormDeleted += FormChanged;
            project.ModuleAdded += ModuleChanged;
            project.ModuleDeleted += ModuleChanged;
            project.RelatedDocumentAdded += CarriedChanged;
            project.RelatedDocumentDeleted += CarriedChanged;
            _unsubscribe.Add(() =>
            {
                project.FormAdded -= FormChanged;
                project.FormDeleted -= FormChanged;
                project.ModuleAdded -= ModuleChanged;
                project.ModuleDeleted -= ModuleChanged;
                project.RelatedDocumentAdded -= CarriedChanged;
                project.RelatedDocumentDeleted -= CarriedChanged;
            });
            var root = ProjectService.ProjectFilesDirectory(project);
            var files = new List<WorkspaceProjectFile>();
            void Add(INotifyPropertyChanged item, string? path, WorkspaceProjectFileKind kind)
            {
                Observe(item);
                if (path is not null)
                    files.Add(new WorkspaceProjectFile(
                        Path.GetRelativePath(root, path).Replace('/', '\\'), path, kind));
            }
            foreach (var f in project.Forms) Add(f, f.AbsolutePath, WorkspaceProjectFileKind.Form);
            foreach (var m in project.Modules) Add(m, m.AbsolutePath,
                m.Kind switch
                {
                    ModuleKind.ClassModule => WorkspaceProjectFileKind.Class,
                    ModuleKind.UserControl => WorkspaceProjectFileKind.UserControl,
                    ModuleKind.StandardModule => WorkspaceProjectFileKind.Module,
                    _ => WorkspaceProjectFileKind.Unknown
                });
            foreach (var f in project.RelatedDocuments) Add(f, f.AbsolutePath, WorkspaceProjectFileKind.Carried);
            var references = project.References.Select(r => new WorkspaceProjectReference(r.Name ?? "",
                Guid.TryParse(r.Guid, out var guid) ? guid : null,
                Version.TryParse(r.Version, out var version) ? version.Major : null,
                Version.TryParse(r.Version, out version) ? version.Minor : null,
                r.Lcid, r.LibPath is null ? null : SerializedProject.ToHostPath(r.LibPath))).ToArray();
            var arguments = CompilationArguments.TryForProject(project, out var parsed) ? parsed : CompilationArguments.None;
            contexts.Add(new WorkspaceArtifactContext(root,
                new WorkspaceProjectSnapshot(project.Name, project.AbsolutePath, files, references, arguments.Values),
                ProjectText: project.AbsolutePath is { } path
                    ? async token => await Dispatcher.UIThread.InvokeAsync<string>(
                        () => new ProjectSerializer().Serialize(project, path), DispatcherPriority.Normal, token)
                    : null));
        }
        _projects = contexts;
        if (notify) ProjectChanged?.Invoke();
    }

    private void Observe(INotifyPropertyChanged item)
    {
        item.PropertyChanged += MaterialChanged;
        _unsubscribe.Add(() => item.PropertyChanged -= MaterialChanged);
    }
    private void FormChanged(ProjectDefinition _, FormDefinition __) => Refresh();
    private void ModuleChanged(ProjectDefinition _, ModuleDefinition __) => Refresh();
    private void CarriedChanged(ProjectDefinition _, RelatedDocumentDefinition __) => Refresh();

    public void Dispose()
    {
        foreach (var unsubscribe in _unsubscribe) unsubscribe();
        if (_observed is not null)
        {
            _observed.ProjectLoaded -= MembershipChanged;
            _observed.ProjectUnloaded -= MembershipChanged;
        }
    }

    public string? Directory =>
        projectManager() is { StartupProject: { } project }
            ? ProjectService.ProjectFilesDirectory(project)
            : null;

    /// <summary>Project folders followed by existing outside source folders.</summary>
    /// <remarks>
    /// <para>
    /// Deduplicated by full path, and <b>ordinally</b>. Two projects genuinely sharing a directory produce
    /// the same string and collapse to one entry, which is what a server wants — it should not index the
    /// same tree twice. Two differently-cased spellings of one Windows directory do not collapse, and that
    /// is the deliberate direction to err: a duplicate folder is harmless, whereas merging two folders that
    /// only look alike on a case-insensitive filesystem would drop one project's root on Linux, where the
    /// paths are genuinely distinct.
    /// </para>
    ///
    /// <para>
    /// Empty rather than null when nothing is loaded — "no folders" is a state the protocol can express,
    /// and the caller turns it into the null the wire wants.
    /// </para>
    /// </remarks>
    public IReadOnlyList<LspWorkspaceFolder> Folders
    {
        get
        {
            if (projectManager() is not { LoadedProjects: { } projects }) return [];

            var seen = new HashSet<string>(StringComparer.Ordinal);
            var folders = new List<LspWorkspaceFolder>();
            foreach (var project in projects)
            {
                var directory = ProjectService.ProjectFilesDirectory(project);
                if (string.IsNullOrWhiteSpace(directory)) continue;

                var full = TryFullPath(directory);
                if (full is null) continue;

                if (seen.Add(full)) folders.Add(new LspWorkspaceFolder(project.Name, full));
                var sources = project.Forms.Select(f => f.AbsolutePath)
                    .Concat(project.Modules.Select(m => m.AbsolutePath));
                foreach (var source in sources)
                {
                    if (source is null) continue;
                    var hostPath = Path.GetFullPath(SerializedProject.ToHostPath(source), full);
                    if (!File.Exists(hostPath)) continue;
                    var relative = Path.GetRelativePath(full, hostPath);
                    if (relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar,
                            StringComparison.Ordinal) && !Path.IsPathRooted(relative)) continue;
                    var outside = Path.GetDirectoryName(hostPath);
                    if (outside is not null && seen.Add(outside))
                        folders.Add(new LspWorkspaceFolder(Path.GetFileName(outside), outside));
                }
            }
            return folders;
        }
    }

    // A path that cannot be resolved costs that folder, not the workspace: the others are still worth
    // sending, and the one that failed was never going to root a server usefully.
    private static string? TryFullPath(string directory)
    {
        try { return Path.GetFullPath(directory); }
        catch (Exception) { return null; }
    }
}
