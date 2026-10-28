using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Sidecar;
using HexIDE.Lsp;
using Serilog;

namespace HexIDE.Projects;

public class ProjectService : IProjectService
{
    private readonly Func<NewProjectViewModel> newProjectVm;
    private readonly IWindowManager windowManager;
    private readonly IEventBus eventBus;
    private readonly IProjectManager projectManager;
    private readonly IRecentProjectsService recentProjects;
    private readonly IReferenceLibraryService referenceLibraryService;
    private readonly IUserSidecarService sidecar;
    private readonly IFileBaselineStore baselineStore;
    private readonly ILocalizationService localization;
    private readonly IHeaderRefresher headerRefresher;
    private readonly ArchiveProjectService? archives;
    private readonly Func<ILspClient>? lspClient;

    public ProjectService(Func<NewProjectViewModel> newProjectVm,
        IWindowManager windowManager,
        IEventBus eventBus,
        IProjectManager projectManager,
        IRecentProjectsService recentProjects,
        IReferenceLibraryService referenceLibraryService,
        IUserSidecarService sidecar,
        IFileBaselineStore baselineStore,
        ILocalizationService localization,
        IHeaderRefresher headerRefresher,
        ArchiveProjectService? archives = null,
        Func<ILspClient>? lspClient = null)
    {
        this.newProjectVm = newProjectVm;
        this.windowManager = windowManager;
        this.eventBus = eventBus;
        this.projectManager = projectManager;
        this.recentProjects = recentProjects;
        this.referenceLibraryService = referenceLibraryService;
        this.sidecar = sidecar;
        this.baselineStore = baselineStore;
        this.localization = localization;
        this.headerRefresher = headerRefresher;
        this.archives = archives;
        this.lspClient = lspClient;
    }

    /// <summary>
    /// The code page a file without a byte-order mark is read in. The host's, except under test: on a
    /// Western host every byte decodes, so a file the code page cannot read is only reachable by setting it.
    /// </summary>
    internal Vb6TextEncoding HostAnsi { get; set; } = Vb6TextEncoding.Ansi;

    private Task<(string Text, byte[] Bytes, Vb6TextEncoding Encoding, bool ByteForByte)> ReadSourceAsync(
        string path, Vb6TextEncoding? lastReadIn = null) =>
        Vb6TextFile.ReadDocumentAsync(path, lastReadIn, HostAnsi);

    private string ReadByteForByteMessage(string file) =>
        string.Format(localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte"), file, HostAnsi.CodePage);

    /// <summary>
    /// One notice for every member a load read byte for byte, shown on its own rather than among the load's
    /// errors: these members loaded, and listing them under a failure heading hid real errors past its cap.
    /// </summary>
    private Task ReportReadByteForByte(IReadOnlyList<string> files) => files.Count switch
    {
        0 => Task.CompletedTask,
        1 => windowManager.MessageBox(ReadByteForByteMessage(files[0]), icon: MessageBoxIcon.Warning),
        _ => windowManager.MessageBox(
            string.Format(localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte.Many"),
                string.Join("\n", files.Select(f => $"  • {f}")), HostAnsi.CodePage),
            icon: MessageBoxIcon.Warning),
    };

    /// <summary>
    /// A file joining the project that a kept line already names, because it failed to load, takes that
    /// line's place: kept as well, the project file would name it twice.
    /// </summary>
    private static void ForgetKeptLineFor(ProjectDefinition project, string absolutePath)
    {
        for (var i = project.PreservedItemLines.Count - 1; i >= 0; i--)
        {
            if (!KeptLineNames(project, project.PreservedItemLines[i], absolutePath))
                continue;
            project.PreservedItemLines.RemoveAt(i);
            // Slots follow the remaining lines' list indexes, not the removed line's position.
            for (var j = i; j < project.PreservedItemLines.Count; j++)
            {
                if (project.PreservedItemLineSlots.TryGetValue(j + 1, out var slot))
                    project.PreservedItemLineSlots[j] = slot;
                else
                    project.PreservedItemLineSlots.Remove(j);
            }
            project.PreservedItemLineSlots.Remove(project.PreservedItemLines.Count);
        }
    }

    /// <summary>
    /// Whether a line the project file keeps for a member names the file at <paramref name="absolutePath"/>, as the
    /// host's file system compares names. A project with no file of its own has no kept lines to resolve.
    /// </summary>
    private static bool KeptLineNames(ProjectDefinition project, string line, string absolutePath) =>
        // Relative to the folder the line was read in, which a Save As to another folder does not change (#737).
        (project.ItemLinesDirectory ?? (project.AbsolutePath is { } projectPath ? Path.GetDirectoryName(projectPath) : null))
            is { } linesDirectory
        && KeptLinePath(line) is { } relative
        && string.Equals(
            Path.GetFullPath(Path.Join(linesDirectory, ToLocalRelativePath(relative))),
            Path.GetFullPath(absolutePath),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// The file a kept member line names, or null for a line that names no member file. A UserDocument names one, but
    /// is never loaded, so no member joining the project takes its place.
    /// </summary>
    private static string? KeptLinePath(string line) =>
        SerializedProject.ItemLineFile(line) is { } file
        && (file.Key.Equals(SerializedProject.FormKey, StringComparison.OrdinalIgnoreCase)
            || file.Key.Equals(SerializedProject.ModuleKey, StringComparison.OrdinalIgnoreCase)
            || file.Key.Equals(SerializedProject.ClassKey, StringComparison.OrdinalIgnoreCase)
            || file.Key.Equals(SerializedProject.UserControlKey, StringComparison.OrdinalIgnoreCase)
            || file.Key.Equals(SerializedProject.PropertyPageKey, StringComparison.OrdinalIgnoreCase))
            ? line.Substring(file.Start, file.Length)
            : null;

    /// <summary>
    /// The project-file line for a member that did not load, so a save writes it back rather than dropping
    /// it. In the shape VB6 requires for each kind: <c>Name; File</c> for a module or class, the bare path
    /// for the designer kinds (see <see cref="ProjectSerializer"/>).
    /// </summary>
    private static string ItemLineFor(ModuleKind kind, string name, string relativePath) =>
        ProjectLoader.ItemLineFor(kind, name, relativePath);

    private async Task<IAddinProjectTemplate?> ChooseNewProject()
    {
        var vm = newProjectVm();
        if (!await windowManager.ShowDialog(vm))
            return null;

        // Dialog confirmed — check which tab produced the result
        if (vm.ResultFilePath != null)
        {
            // "Existing" or "Recent" tab selected a .vbp file
            await OpenProject(vm.ResultFilePath);
            return null;
        }

        return vm.SelectedTemplate?.Template;
    }

    public async Task CreateNewProject()
    {
        Log.Debug("ProjectService: CreateNewProject — showing dialog");
        var template = await ChooseNewProject();
        if (template == null)
        {
            Log.Debug("ProjectService: CreateNewProject — no template selected (cancelled or file opened)");
            return;
        }

        var name = ProjectNaming.NextFreeProjectName(projectManager.LoadedProjects);
        Log.Debug("ProjectService: Creating project '{ProjectName}' from template {Template}", name, template.Name);
        projectManager.NewProject(template, name);
        EnsureGroupIfMultiple();
    }

    public async Task CreateNewProject(IProjectTemplate template)
    {
        if (template.Supported == false)
        {
            await windowManager.MessageBox("Your HexIDE version doesn't support " + template.Name, "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Information);
            return;
        }

        var name = ProjectNaming.NextFreeProjectName(projectManager.LoadedProjects);

        projectManager.NewProject(template, name);
        EnsureGroupIfMultiple();
    }

    private void EnsureGroupIfMultiple()
    {
        if (projectManager.LoadedProjects.Count > 1 && projectManager.CurrentGroup == null)
            projectManager.CurrentGroup = new ProjectGroupDefinition("Group1");
    }

    public async Task UnloadAllProjects()
    {
        if (projectManager.LoadedProjects.Count == 0)
        {
            if (archives != null) await archives.Close();
            return;
        }

        // Flush open editor buffers into the model before deciding what is dirty.
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        var changedFilesVm = new SaveChangesViewModel();
        foreach (var loadedProject in projectManager.LoadedProjects)
        {
            if (IsDirty(loadedProject))
                changedFilesVm.Add(loadedProject);
            foreach (var form in loadedProject.Forms)
                if (IsDirty(form))
                    changedFilesVm.Add(form);
            // Modules are half a VB6 project — omitting them here discarded .bas/.cls/.ctl edits silently.
            foreach (var module in loadedProject.Modules)
                if (IsDirty(module))
                    changedFilesVm.Add(module);
        }

        // Nothing was edited — opening a project, looking at it and closing must not raise a dialog.
        if (changedFilesVm.ChangedFiles.Count > 0)
        {
            changedFilesVm.SelectedFiles.AddRange(changedFilesVm.ChangedFiles);
            if (!await windowManager.ShowDialog(changedFilesVm))
                throw new OperationCanceledException();

            if (changedFilesVm.SaveChanges)
                await SaveSelected(changedFilesVm);
        }

        projectManager.UnloadAllProjects();
        if (archives != null) await archives.Close();
    }

    public async Task RemoveDocument(DocumentIdentity document)
    {
        if (!DocumentLookup.DocumentsOf(document.Project).Contains(document))
            return;

        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        var changed = new SaveChangesViewModel();
        if (document.Form is { } form && IsDirty(form)) changed.Add(form);
        if (document.Module is { } module && IsDirty(module)) changed.Add(module);
        if (changed.ChangedFiles.Count > 0)
        {
            changed.SelectedFiles.AddRange(changed.ChangedFiles);
            if (!await windowManager.ShowDialog(changed))
                throw new OperationCanceledException();
            if (changed.SaveChanges && changed.SelectedFiles.Count > 0)
            {
                var saved = document.Form is { } toSave
                    ? await SaveForm(toSave, false)
                    : await SaveModule(document.Module!, false);
                if (!saved) return;
            }
        }

        if (document.Form is { } removedForm)
        {
            eventBus.Publish(new FormUnloadedEvent(removedForm));
            document.Project.DeleteForm(removedForm);
        }
        else if (document.Module is { } removedModule)
        {
            eventBus.Publish(new ModuleUnloadedEvent(removedModule));
            document.Project.DeleteModule(removedModule);
        }
    }

    public async Task UnloadProject(ProjectDefinition project)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        var changedFilesVm = new SaveChangesViewModel();
        if (IsDirty(project))
            changedFilesVm.Add(project);
        foreach (var form in project.Forms)
            if (IsDirty(form))
                changedFilesVm.Add(form);
        foreach (var module in project.Modules)
            if (IsDirty(module))
                changedFilesVm.Add(module);

        if (changedFilesVm.ChangedFiles.Count > 0)
        {
            changedFilesVm.SelectedFiles.AddRange(changedFilesVm.ChangedFiles);
            if (!await windowManager.ShowDialog(changedFilesVm))
                throw new OperationCanceledException();

            if (changedFilesVm.SaveChanges)
                await SaveSelected(changedFilesVm);
        }

        projectManager.UnloadProject(project);
    }

    public async Task OpenProject()
    {
        if (OperatingSystem.IsBrowser())
        {
            await windowManager.MessageBox("Opening projects is not supported in browser.", icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        var files = await windowManager.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project",
            FileTypeFilter =
            [
                new("Project Files") { Patterns = ["*.vbp", "*.vbg", "*.twinproj"] },
                new("Project Group") { Patterns = ["*.vbg"] },
                new("Project")       { Patterns = ["*.vbp"] },
                new("All Files")     { Patterns = ["*.*"] }
            ],
            AllowMultiple = false
        });

        if (files == null || files.Count != 1)
            throw new OperationCanceledException();

        // Archive projects coexist with the current workspace, as on the direct/recent open path.
        if (!string.Equals(Path.GetExtension(files[0]), ".twinproj", StringComparison.OrdinalIgnoreCase))
            await UnloadAllProjects();

        await OpenProject(files[0]);
    }

    public async Task OpenProject(string projectPath)
    {
        if (string.Equals(Path.GetExtension(projectPath), ".twinproj", StringComparison.OrdinalIgnoreCase))
        {
            if (archives == null) throw new NotSupportedException("Archive project service is unavailable.");
            try
            {
                await archives.Open(projectPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error(ex, "Error opening archive project {ProjectPath}", projectPath);
                await windowManager.MessageBox(
                    string.Format(localization.GetString("Str.Archive.OpenErrorFormat"), Path.GetFileName(projectPath), ex.Message),
                    icon: MessageBoxIcon.Error);
            }
            return;
        }
        if (string.Equals(Path.GetExtension(projectPath), ".vbg", StringComparison.OrdinalIgnoreCase))
        {
            await OpenGroupFromPath(projectPath);
            return;
        }
        await LoadProjectFromDisk(projectPath);
    }

    private async Task OpenGroupFromPath(string groupPath)
    {
        Log.Information("ProjectService: Opening group {GroupPath}", groupPath);
        var groupDir = Path.GetDirectoryName(groupPath)!;
        var groupName = Path.GetFileNameWithoutExtension(groupPath);

        var (groupText, _, groupEncoding, groupByteForByte) = await ReadSourceAsync(groupPath);
        if (groupByteForByte)
            await windowManager.MessageBox(ReadByteForByteMessage(Path.GetFileName(groupPath)), icon: MessageBoxIcon.Warning);
        var serializedGroup = new GroupDeserializer()
            .Deserialize(groupText);

        foreach (var relPath in serializedGroup.ProjectRelativePaths)
        {
            var absPath = Path.GetFullPath(Path.Combine(groupDir, ToLocalRelativePath(relPath)));
            await LoadProjectFromDisk(absPath);
        }

        if (serializedGroup.StartupProjectRelativePath != null)
        {
            var startupAbs = Path.GetFullPath(
                Path.Combine(groupDir, ToLocalRelativePath(serializedGroup.StartupProjectRelativePath)));
            projectManager.StartupProject = projectManager.LoadedProjects
                .FirstOrDefault(p => string.Equals(
                    p.AbsolutePath, startupAbs, StringComparison.OrdinalIgnoreCase));
        }

        var group = new ProjectGroupDefinition(groupName) { AbsolutePath = groupPath, TextEncoding = groupEncoding };
        group.UnknownLines.AddRange(serializedGroup.UnknownLines);
        projectManager.CurrentGroup = group;
        recentProjects.Add(groupPath);
    }

    // VB6 stores project-relative paths with Windows separators (e.g. Form=Forms\Main.frm). On non-Windows
    // a backslash is a literal filename char, so a multi-folder project would resolve to a bogus path and
    // silently drop the file. Normalize to the platform separator for FILESYSTEM resolution only — the raw
    // value is still preserved verbatim on save (VB6 .vbp fidelity) and in the missing-file line.
    //
    // Delegates rather than repeating the rule. This used to be its own copy, and the serialization layer
    // grew a second, narrower answer to the same question — which is how ProjectDeserializer came to call
    // System.IO.Path.GetFileName on a raw RelatedDoc= value and name a document after its directory on
    // Linux. One rule, one place.
    internal static string ToLocalRelativePath(string relativePath) =>
        SerializedProject.ToHostPath(relativePath);

    private async Task LoadProjectFromDisk(string projectPath)
    {
        Log.Information("ProjectService: Opening project {ProjectPath}", projectPath);
        if (projectManager.LoadedProjects.Any(p => p.AbsolutePath != null && Path.GetFullPath(projectPath) == Path.GetFullPath(p.AbsolutePath)))
        {
            await windowManager.MessageBox($"Project {Path.GetFileName(projectPath)} is already opened.", icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }
        var errorSink = new DeserializeErrorSink(localization);
        // The load itself is shared with the Standalone host a made program runs in (#1062). What the IDE adds is its own
        // record of what it read, which the save-changes prompt and the file watcher compare against.
        var (project, serializedProject, readByteForByteList) = await new ProjectLoader
        {
            HostAnsi = HostAnsi,
            FileRead = baselineStore.Record,
        }.LoadAsync(projectPath, errorSink);
        var readByteForByte = readByteForByteList.ToList();

        projectManager.AddProject(project);
        recentProjects.Add(projectPath);
        await sidecar.LoadAsync(project);

        // Establish the "nothing edited yet" point for the save-changes prompt.
        SnapshotRenderBaselines(project);
        // After the baselines, so that a form whose VB_Name is made to agree comes up edited: its file still disagrees.
        var conflictingNames = ReconcileDesignerNames(project);

        if (serializedProject.SkippedUserDocumentPaths.Count > 0)
        {
            var files = string.Join("\n", serializedProject.SkippedUserDocumentPaths.Select(p => $"  • {p}"));
            await windowManager.MessageBox(
                string.Format(localization.GetString("Str.ProjectLoad.ActiveXDocuments"), files),
                icon: MessageBoxIcon.Warning);
        }

        if (errorSink.Errors.Count > 0)
        {
            const int maxShown = 10;
            var shown = errorSink.Errors.Take(maxShown).ToList();
            var message = localization.GetString("Str.ProjectLoad.Heading") + "\n" + string.Join("\n", shown);
            if (errorSink.Errors.Count > maxShown)
                message += "\n\n" + string.Format(localization.GetString("Str.ProjectLoad.MoreWarnings"), errorSink.Errors.Count - maxShown);
            await windowManager.MessageBox(message, icon: MessageBoxIcon.Warning);
        }

        await ReportReadByteForByte(readByteForByte);
        if (conflictingNames.Count > 0)
            await windowManager.MessageBox(string.Join("\n\n", conflictingNames), icon: MessageBoxIcon.Warning);
    }

    /// <summary>
    /// Makes the <c>Attribute VB_Name</c> of each form, UserControl and PropertyPage agree with its <c>Begin</c> line
    /// where the two differ, even in case alone, and returns a notice for each, in VB6's words (#1407, #1411).
    /// </summary>
    /// <remarks>
    /// VB6 names each of these by its <c>Begin</c> line, as the loader now does too. It says so as the project opens, and
    /// its next save writes <c>VB_Name</c> to match (measured for a form and a UserControl: the oracle's "A form whose two
    /// names disagree"). The rewrite is to the model, which no code window shows yet, so the document comes up edited,
    /// and the next save writes it. A UserControl's or PropertyPage's attribute run is in its module's code, which is
    /// what its save writes.
    /// </remarks>
    private List<string> ReconcileDesignerNames(ProjectDefinition project)
    {
        var notices = new List<string>();
        foreach (var form in project.Forms)
            Reconcile(form.Name, form.Code, form.UpdateCode, form.AbsolutePath);
        foreach (var module in project.Modules)
            if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage && module.FormPart is not null)
                Reconcile(module.Name, module.Code, module.UpdateCode, module.AbsolutePath);
        return notices;

        void Reconcile(string name, string code, Action<string> update, string? path)
        {
            if (name.Length == 0 || FormCodeText.VbNameOf(code) is not { } vbName
                || string.Equals(vbName, name, StringComparison.Ordinal))
                return;
            update(FormCodeText.RetargetVbName(code, name));
            notices.Add(string.Format(localization.GetString("Str.Dialog.ConflictingNames"), path ?? name, name));
        }
    }

    private static IReadOnlyDictionary<string, ComponentBaseClass> BuildUserControlRegistry(
        ProjectDefinition project) => ProjectLoader.BuildUserControlRegistry(project);

    private class DeserializeErrorSink(ILocalizationService localization) : IDeserializeErrorSink
    {
        private List<string> errors = new();

        public void LogError(string error)
        {
            Log.Warning("[Deserialize] {Error}", error);
            errors.Add(error);
        }

        public void LogError(string key, string fallback, params object[] arguments) =>
            LogError(string.Format(localization.GetString(key), arguments));

        public IReadOnlyList<string> Errors => errors;
    }

    /// <summary>
    /// Names refused during the current save, so a batch reports once rather than per file.
    ///
    /// Names rather than forms: a UserControl is refused as a <see cref="ModuleDefinition"/>, which is not a
    /// <see cref="FormDefinition"/>, and both belong in the same report.
    /// </summary>
    private readonly List<string> refusedThisSave = new();

    /// <summary>
    /// Bank a refusal for the current save. Only ever called by an entry point that is followed by a drain
    /// — never by the write helpers themselves, or a path with no drain of its own would leave the entry
    /// sitting there to surface during the next unrelated save, naming a file that was not in that batch.
    /// </summary>
    private void RecordRefusal(string name)
    {
        if (!refusedThisSave.Contains(name))
            refusedThisSave.Add(name);
    }

    /// <summary>
    /// Save one form on its own. A refusal is reported at the moment it happens, where a batch reports
    /// every refusal in one dialog.
    /// </summary>
    /// <param name="interactive">
    /// False for an automation caller: no refusal is shown as a dialog, an unfaithful refusal comes back as
    /// <see langword="false"/>, and an encoding refusal as the <see cref="SourceEncodingRefusal"/>, which
    /// names the character. A dialog would hold the caller's reply until a person dismissed it. The picker
    /// is not suppressed; see <see cref="ScriptedFileDialogs.WouldShowPicker"/>.
    /// </param>
    public async Task<bool> SaveForm(FormDefinition form, bool saveAs, bool interactive = true, SaveRewriteReport? rewrites = null)
    {
        try
        {
            // Returned so a non-interactive caller can tell a refusal from a save. The MCP write tools reported
            // success unconditionally, which told an agent a file had been written when it had not — and an
            // agent, unlike a developer, has no dialog to read and will build on the answer.
            return (await WithSaveRefusals(() => SaveFiles([(form, saveAs)], ownCommand: true, rewrites: rewrites), interactive)).Count > 0;
        }
        finally
        {
            // Without this a lone refusal did not merely go unreported: the entry SURVIVED in refusedThisSave
            // and surfaced during the next unrelated Save Project, naming a file that was not in that batch.
            await ReportRefusedSaves(interactive);
        }
    }

    /// <summary>
    /// Runs a save, and reports a refusal that ends it before anything is written: to a person as a warning, to
    /// an automation caller only by the exception, which is rethrown either way. Two kinds end one: a character
    /// the code page cannot hold (<see cref="SourceEncodingRefusal"/>), and a project file that would leave out a
    /// member with no file (<see cref="UnsavedMemberRefusal"/>).
    /// </summary>
    /// <remarks>
    /// Wrapped around each operation once, at its entry point, rather than around each file it writes. An
    /// operation checks every file before writing any, so one refusal ends it; and by the time the warning
    /// shows, everything the operation moved in the model has been put back.
    /// Rethrown rather than turned into false: a close-time save that carried on would unload the project and
    /// discard the edit the developer had just asked to keep.
    /// </remarks>
    private async Task<T> WithSaveRefusals<T>(Func<Task<T>> operation, bool interactive = true)
    {
        try
        {
            return await operation();
        }
        catch (SourceEncodingRefusal refusal)
        {
            Log.Warning("Cannot save {Document} in code page {CodePage}: it holds {CodePoint}",
                refusal.Document, refusal.CodePage, refusal.CodePoint);
            // Says that nothing was written, rather than that one document was not: the operation checked every
            // file before writing any. The line is quoted because the character may not be in anything the
            // person just edited: a form is written whole, and a project file names its members by file.
            if (interactive)
                await windowManager.MessageBox(
                    string.Format(localization.GetString("Str.Dialog.SourceEncoding.NothingWritten"),
                        refusal.Document, refusal.CodePage, refusal.Character, refusal.CodePoint,
                        refusal.Line.Length > 0 ? refusal.Line : refusal.Character),
                    "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            throw;
        }
        catch (UnsavedMemberRefusal refusal)
        {
            Log.Warning("Not saving {Project}: its project file would leave out {Members}, which have no file",
                refusal.Project, refusal.Members);
            if (interactive)
                await windowManager.MessageBox(
                    string.Format(localization.GetString("Str.Dialog.UnsavedMembers.NothingWritten"),
                        refusal.Project, string.Join("\n", refusal.Members.Select(m => $"  • {m}"))),
                    "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
            throw;
        }
    }

    /// <inheritdoc cref="WithSaveRefusals{T}"/>
    private Task WithSaveRefusals(Func<Task> operation, bool interactive = true) =>
        WithSaveRefusals(async () =>
        {
            await operation();
            return true;
        }, interactive);

    // ── Saving: every file chosen and checked before any is written ─────────────────────────────────

    /// <summary>One file an operation writes, and where.</summary>
    /// <param name="Chosen">The developer picked <paramref name="Path"/> during this operation.</param>
    private readonly record struct Destination(object Document, string Path, bool Chosen = false);

    /// <summary>
    /// Saves forms, modules, projects and the group as one operation, in the order given. Every destination
    /// is chosen, and every file checked against its code page, before the first is written. So a refusal,
    /// or a picker dismissed part-way, leaves every file as it was.
    /// </summary>
    /// <param name="ownCommand">
    /// The one document in <paramref name="files"/> is written by its own Save or Save As command
    /// (<see cref="SaveForm"/>, <see cref="SaveModule"/>), not as a member of a project's or the group's save. Such
    /// a write rewrites the document's attribute qualifiers whether or not it has unsaved changes, as VB6's does;
    /// see <see cref="RewrittenQualifiers"/>.
    /// </param>
    /// <returns>What was written. A document HexIDE cannot reproduce is left out, and banked for the
    /// caller's <see cref="ReportRefusedSaves"/>.</returns>
    /// <remarks>
    /// The check and the write agree for four reasons, and each must keep holding:
    /// <list type="bullet">
    /// <item>both render through the same helper per kind (<see cref="RenderForm"/>, <see cref="RenderModule"/>,
    /// the project and group serializers);</item>
    /// <item>editor buffers are flushed into the model once, here, and not again by a writer;</item>
    /// <item>which documents may be written is decided again after the last picker. A picker is an await, and
    /// the file watcher reloads a document changed on disk while one is open, so a document that passed the
    /// faithful-save gate before a later picker may no longer pass it;</item>
    /// <item>nothing awaits from that decision to the last write, so the model cannot change in between.</item>
    /// </list>
    /// If one stops holding, each writer's own encode still refuses, but files written before it stay written.
    /// </remarks>
    private async Task<IReadOnlyList<Destination>> SaveFiles(
        IReadOnlyList<(object Document, bool SaveAs)> files, bool announceSave = true, bool ownCommand = false, SaveRewriteReport? rewrites = null)
    {
        // A UserControl's or PropertyPage's designer half is one file with its module, and the file the project names is
        // the module's. Saved as a form, a new one has no path (#500) and asked for a .frm, and wrote the edit there
        // beside a .ctl that was never written (#474). So whatever is handed in, its module is saved.
        files = [.. files
            .Select(file => file.Document is FormDefinition { OwningModule: { } module } && DesignerPartOf(module) is not null
                ? (module, file.SaveAs)
                : file)
            .DistinctBy(file => file.Document, ReferenceEqualityComparer.Instance)];

        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        var destinations = new List<Destination>();
        foreach (var (document, saveAs) in files)
            if (await DestinationOf(document, saveAs) is { } destination)
                destinations.Add(destination);

        // The gate in DestinationOf keeps a picker from asking about a file that will not be written; this one
        // keeps a reload during a later picker from slipping a file past it (see the remarks).
        destinations.RemoveAll(destination => RefusesUnfaithful(destination.Document));

        EnsureEveryMemberListed(destinations);
        EnsureEncodable(destinations);
        var written = new List<Destination>();
        foreach (var destination in destinations)
        {
            var existed = File.Exists(destination.Path);
            if (Write(destination, announceSave, ownCommand, rewrites))
            {
                written.Add(destination);
                if (announceSave && !existed && lspClient is not null)
                    await lspClient().DidCreateFilesAsync([new Uri(Path.GetFullPath(destination.Path)).AbsoluteUri]);
            }
        }

        foreach (var destination in written)
        {
            if (destination.Document is ProjectGroupDefinition)
                recentProjects.Add(destination.Path);
            else if (destination.Document is ProjectDefinition project)
            {
                recentProjects.Add(destination.Path);
                if (lspClient is not null)
                    await lspClient().WorkspaceChangedAsync();
                await sidecar.SaveAsync(project);
            }
        }
        return written;
    }

    /// <summary>
    /// Where <paramref name="document"/> will be written, asking the developer when it has nowhere yet or
    /// <paramref name="saveAs"/> is set. Null when HexIDE cannot reproduce it, which is banked as a refusal.
    /// </summary>
    /// <exception cref="OperationCanceledException">A picker was dismissed.</exception>
    private async Task<Destination?> DestinationOf(object document, bool saveAs)
    {
        switch (document)
        {
            case FormDefinition form:
                // Writing this form would not reproduce it. Refusing is the honest move: the original stays intact
                // and the developer finds out now rather than the next time they open the project in VB6.
                //
                // Save As is refused TOO, and refused HERE — above the file picker, so the developer is not asked
                // for a destination that will never be written.
                //
                // It used to be exempt, on the reasoning that "the original file is not at risk". True of the
                // original, and silent about the copy. WriteCompanionBinary can only protect a companion that
                // already EXISTS, so at a new path the blobs are dropped — and the copy then reopens as FAITHFUL,
                // because the citations that flagged it are the very thing that went missing. A refusal we can
                // recover from becomes a file that looks clean and is not, which is the one outcome
                // docs/serialization-outcomes.md says is never acceptable. (#143)
                if (RefusesUnfaithful(form))
                    return null;
                return form.AbsolutePath is { } formPath && !saveAs
                    ? new Destination(form, formPath)
                    : new Destination(form, await ChooseFormPath(form), Chosen: true);

            case ModuleDefinition module:
                // Above the picker, as for a form. This used to sit below it, so Save As on an unfaithful
                // UserControl asked where to put the file and then declined to write it (#147). A brand-new
                // module has no designer half yet, or a fresh one with no unfaithful causes.
                if (RefusesUnfaithful(module))
                    return null;
                return module.AbsolutePath is { } modulePath && !saveAs
                    ? new Destination(module, modulePath)
                    : new Destination(module, await ChooseModulePath(module), Chosen: true);

            case ProjectDefinition project:
                return project.AbsolutePath is { } projectPath && !saveAs
                    ? new Destination(project, projectPath)
                    : new Destination(project, await ChooseProjectPath(project), Chosen: true);

            case ProjectGroupDefinition group:
                return group.AbsolutePath is { } groupPath && !saveAs
                    ? new Destination(group, groupPath)
                    : new Destination(group, await ChooseGroupPath(group), Chosen: true);

            default:
                throw new ArgumentException($"HexIDE does not save a {document?.GetType().Name ?? "null"}.", nameof(document));
        }
    }

    /// <summary>The designer half a UserControl or PropertyPage is written from; null for any other module.</summary>
    private static FormDefinition? DesignerPartOf(ModuleDefinition module) =>
        module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage ? module.FormPart : null;

    /// <summary>Whether a save may write <paramref name="document"/>: the writers' own faithful-save gates.</summary>
    private static bool IsWritable(object document) => document switch
    {
        FormDefinition form => form.CanSaveFaithfully,
        ModuleDefinition module => DesignerPartOf(module) is not { CanSaveFaithfully: false },
        _ => true
    };

    /// <summary>
    /// Whether HexIDE must refuse to write <paramref name="document"/> because it cannot reproduce it. A
    /// refusal is logged and banked for the caller's <see cref="ReportRefusedSaves"/>.
    /// </summary>
    private bool RefusesUnfaithful(object document)
    {
        switch (document)
        {
            case FormDefinition { CanSaveFaithfully: false } form:
                Log.Warning("Refusing to save {Form}: {Reason}", form.Name, form.UnfaithfulSaveReason);
                RecordRefusal(form.Name);
                return true;
            case ModuleDefinition module when DesignerPartOf(module) is { CanSaveFaithfully: false } designerPart:
                Log.Warning("Refusing to write {Module}: {Reason}", module.Name, designerPart.UnfaithfulSaveReason);
                RecordRefusal(module.Name);
                return true;
            default:
                return false;
        }
    }

    private async Task<string> ChooseFormPath(FormDefinition form) =>
        await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = "frm",
            SuggestedFileName = form.Name + ".frm",
            FileTypeChoices = [new("Form Files") { Patterns = ["*.frm"] }, new("All Files") { Patterns = ["*.*"] }]
        }) ?? throw new OperationCanceledException();

    private async Task<string> ChooseModulePath(ModuleDefinition module)
    {
        var ext = ModuleExtension(module.Kind);
        return await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = ext,
            SuggestedFileName = module.Name + "." + ext,
            FileTypeChoices = [new("Module Files") { Patterns = [$"*.{ext}"] }, new("All Files") { Patterns = ["*.*"] }]
        }) ?? throw new OperationCanceledException();
    }

    private async Task<string> ChooseProjectPath(ProjectDefinition project) =>
        await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = "vbp",
            SuggestedFileName = project.Name + ".vbp",
            FileTypeChoices = [new("Project Files") { Patterns = ["*.vbp"] }, new("All Files") { Patterns = ["*.*"] }]
        }) ?? throw new OperationCanceledException();

    private async Task<string> ChooseGroupPath(ProjectGroupDefinition group) =>
        await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Project Group As",
            DefaultExtension = "vbg",
            FileTypeChoices = [new FilePickerFileType("Project Group") { Patterns = ["*.vbg"] }],
            SuggestedFileName = group.Name
        }) ?? throw new OperationCanceledException();

    /// <summary>
    /// Refuses, with an <see cref="UnsavedMemberRefusal"/> for the first project in order, unless every project
    /// file in <paramref name="destinations"/> can list all of its forms and modules: each has a file already, or
    /// is written by this operation. Writes nothing.
    /// </summary>
    /// <remarks>
    /// The save-changes prompt can tick a project without a member that has never been saved, and the project
    /// file then left the member out, as the serializer skips one with no path. Where the member was the startup
    /// form, the file kept its <c>Startup=</c> line, which vb6.exe builds in silence into a program that starts at
    /// <c>Sub Main</c> (hexide-io/HexIDE#497). VB6's own save of a project is all or nothing, and so is this.
    /// </remarks>
    private static void EnsureEveryMemberListed(IReadOnlyList<Destination> destinations)
    {
        var written = destinations.Select(d => d.Document).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var destination in destinations)
            if (destination.Document is ProjectDefinition project
                && UnlistedMembers(project, member => written.Contains(member)) is { Count: > 0 } unlisted)
                throw new UnsavedMemberRefusal(project.Name, unlisted);
    }

    /// <summary>
    /// The forms and modules a project file would leave out if written now, by name: those with no file, unless
    /// <paramref name="writtenWithIt"/> says they get one in the same operation.
    /// </summary>
    private static List<string> UnlistedMembers(ProjectDefinition project, Func<object, bool> writtenWithIt) =>
    [
        .. project.Forms.Where(form => form.AbsolutePath is null && !writtenWithIt(form)).Select(form => form.Name),
        .. project.Modules.Where(module => module.AbsolutePath is null && !writtenWithIt(module))
            .Select(module => module.Name),
    ];

    /// <summary>
    /// Refuses, with a <see cref="SourceEncodingRefusal"/> for the first file in order, unless every file
    /// in <paramref name="destinations"/> can be written in its own code page. Writes nothing and changes
    /// nothing in the model.
    /// </summary>
    /// <remarks>
    /// Each file is rendered as it will be written, at its destination: a form cites its companion by file
    /// name, and a project or group file names its members by where they are going, not where they are now.
    /// Every caller has already refused a document HexIDE cannot reproduce, so none reaches this.
    /// </remarks>
    private void EnsureEncodable(IReadOnlyList<Destination> destinations)
    {
        var going = new Dictionary<object, string>(ReferenceEqualityComparer.Instance);
        foreach (var destination in destinations)
            going[destination.Document] = destination.Path;
        string? PathOf(object member) => going.GetValueOrDefault(member);

        foreach (var destination in destinations)
        {
            var (document, text, encoding) = destination.Document switch
            {
                FormDefinition form => (form.Name, RenderForm(form, destination.Path).Text, form.TextEncoding),
                ModuleDefinition module => (module.Name, RenderModule(module, destination.Path).Text, module.TextEncoding),
                ProjectDefinition project => (project.Name,
                    new ProjectSerializer().Serialize(project, destination.Path, PathOf), project.TextEncoding),
                ProjectGroupDefinition group => (group.Name, RenderGroup(group, destination.Path, PathOf), group.TextEncoding),
                _ => throw new ArgumentException($"HexIDE does not save a {destination.Document.GetType().Name}.")
            };
            Vb6TextFile.EncodeDocument(document, text, encoding);
        }

        // A file given a new name is named in turn by the file that holds it: a form or module by its project
        // file, a project by the group file. When that file is not part of this operation it is rendered with
        // the new name too, so a Save As to a name the holder's code page cannot hold is refused now, in the
        // holder's name, rather than written and then refusing every later save of the holder.
        foreach (var holder in destinations.Where(d => d.Chosen).Select(d => HolderOf(d.Document)).OfType<object>()
                     .Distinct(ReferenceEqualityComparer.Instance))
            if (!going.ContainsKey(holder))
                EnsureHolderEncodable(holder, PathOf);
    }

    /// <summary>The project a form or module is in, or the open group for a project.</summary>
    private object? HolderOf(object document) => document switch
    {
        FormDefinition form => form.Owner,
        ModuleDefinition module => module.Owner,
        ProjectDefinition => projectManager.CurrentGroup,
        _ => null
    };

    /// <summary>
    /// Refuses when <paramref name="holder"/>'s file, rendered with its members where
    /// <paramref name="pathOf"/> puts them, cannot be written in its code page but could be as it stands.
    /// </summary>
    private void EnsureHolderEncodable(object holder, Func<object, string?> pathOf)
    {
        string name;
        Vb6TextEncoding encoding;
        Func<Func<object, string?>?, string> render;
        switch (holder)
        {
            case ProjectDefinition { AbsolutePath: { } projectPath } project:
                (name, encoding) = (project.Name, project.TextEncoding);
                render = of => new ProjectSerializer().Serialize(project, projectPath, of);
                break;
            case ProjectGroupDefinition { AbsolutePath: { } groupPath } group:
                (name, encoding) = (group.Name, group.TextEncoding);
                render = of => RenderGroup(group, groupPath, of);
                break;
            default:
                // No file yet, so nothing names the new place. Its first save checks it whole.
                return;
        }

        try
        {
            Vb6TextFile.EncodeDocument(name, render(pathOf), encoding);
        }
        catch (SourceEncodingRefusal) when (!Encodes(render(null), encoding))
        {
            // Not this operation's doing: the holder cannot be written as it stands either, and its own save
            // refuses that. A Save Form As is not stopped for a project file it does not write.
        }
    }

    private static bool Encodes(string text, Vb6TextEncoding encoding)
    {
        try
        {
            Vb6TextFile.Encode(text, encoding);
            return true;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Writes one file an operation has already checked, and announces a form or module. False when a form's
    /// own gate refused it, which <see cref="SaveFiles"/> rules out by deciding just before.
    /// </summary>
    /// <param name="ownCommand">As <see cref="SaveFiles"/>'s.</param>
    private bool Write(Destination destination, bool announceSave, bool ownCommand = false, SaveRewriteReport? rewrites = null)
    {
        switch (destination.Document)
        {
            case FormDefinition form:
                // On success only. A refusal is the opposite of a save, and telling a server a document was
                // written when it was not would have it re-read a file that still holds the previous content.
                if (!SerializeFormToFile(form, destination.Path, adoptHeader: announceSave, ownCommand, rewrites))
                    return false;
                if (announceSave)
                    eventBus.Publish(new DocumentSavedEvent(form, null));
                return true;
            case ModuleDefinition module:
                WriteModule(module, destination.Path, adoptHeader: announceSave, ownCommand, rewrites);
                if (announceSave)
                    eventBus.Publish(new DocumentSavedEvent(module.FormPart, module));
                return true;
            case ProjectDefinition project:
                SerializeOnlyProjectToFile(project, destination.Path);
                return true;
            case ProjectGroupDefinition group:
                var content = RenderGroup(group, destination.Path, pathOf: null);
                AtomicWriteText(destination.Path, content, Vb6TextFile.EncodeDocument(group.Name, content, group.TextEncoding));
                group.AbsolutePath = destination.Path;
                if (destination.Chosen) group.Name = Path.GetFileNameWithoutExtension(destination.Path);
                Log.Information("ProjectService: Saved group {Path}", group.AbsolutePath);
                return true;
            default:
                throw new ArgumentException($"HexIDE does not save a {destination.Document.GetType().Name}.");
        }
    }

    private static (string Text, byte[]? Companion) RenderForm(FormDefinition form, string path) =>
        new FormSerializer().Serialize(form, Path.GetFileName(path));

    /// <summary>
    /// A module's file as it would be written at <paramref name="path"/>. A UserControl or PropertyPage
    /// is a designer file with a companion; a <c>.bas</c> or <c>.cls</c> gets the canonical VB6 header
    /// (<see cref="ModuleFileFormat"/>) in front of its body, so vb6.exe can load it.
    /// </summary>
    private static (string Text, byte[]? Companion) RenderModule(ModuleDefinition module, string path) =>
        DesignerPartOf(module) is { } designerPart
            ? new FormSerializer().Serialize(designerPart, module.Code, Path.GetFileName(path))
            : (ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader), null);

    private string RenderGroup(ProjectGroupDefinition group, string path, Func<object, string?>? pathOf) =>
        new GroupSerializer().Serialize(path, projectManager.LoadedProjects, projectManager.StartupProject,
            group.UnknownLines, pathOf);

    public async Task<bool> ReloadFormFromDisk(FormDefinition form)
    {
        var path = form.AbsolutePath;
        if (path is null || !File.Exists(path))
            return false;

        var project = form.Owner;
        var formDeserializer = new FormDeserializer();
        var errorSink = new DeserializeErrorSink(localization);

        // The designer file is read FIRST: its cited offsets are what partition the companion, so the
        // companion cannot be parsed without it. See FrxDeserializer.
        var (source, sourceBytes, sourceEncoding, byteForByte) = await ReadSourceAsync(path, form.TextEncoding);
        if (byteForByte)
        {
            Log.Warning("Reload: {Path} could not be read in code page {CodePage}; read byte for byte", path, HostAnsi.CodePage);
            await ReportReadByteForByte([Path.GetFileName(path)]);
        }
        baselineStore.Record(path, sourceBytes);

        IReadOnlyDictionary<int, byte[]>? frxBlobs = null;
        var frxPath = Path.ChangeExtension(path, ".frx");
        if (File.Exists(frxPath))
        {
            var frxBytes = await File.ReadAllBytesAsync(frxPath);
            baselineStore.Record(frxPath, frxBytes);
            frxBlobs = FrxDeserializer.Read(frxBytes, source);
        }

        var fresh = formDeserializer.Deserialize(project, source, errorSink, frxBlobs, BuildUserControlRegistry(project));
        if (fresh is null)
            return false;

        form.TextEncoding = sourceEncoding;
        form.UpdateCode(fresh.Code);
        form.UpdateComponents(fresh.Components);
        // The verdict comes from the file too. Adopting only code and components left the banner describing
        // the version that was on disk when the form was first opened.
        form.AdoptFidelityState(fresh);
        SnapshotRenderBaseline(form);
        return true;
    }

    public async Task<bool> ReloadModuleFromDisk(ModuleDefinition module)
    {
        var path = module.AbsolutePath;
        if (path is null || !File.Exists(path))
            return false;

        var (source, sourceBytes, sourceEncoding, byteForByte) = await ReadSourceAsync(path, module.TextEncoding);
        if (byteForByte)
        {
            Log.Warning("Reload: {Path} could not be read in code page {CodePage}; read byte for byte", path, HostAnsi.CodePage);
            await ReportReadByteForByte([Path.GetFileName(path)]);
        }
        baselineStore.Record(path, sourceBytes);
        var (preservedHeader, reloadedBody) = ModuleFileFormat.SplitHeader(source, module.Kind);

        module.TextEncoding = sourceEncoding;
        module.RecordOriginalHeader(preservedHeader);

        module.UpdateCode(reloadedBody);

        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var formDeserializer = new FormDeserializer();
            var errorSink = new DeserializeErrorSink(localization);
            var blobs = await LoadCompanionBlobs(path, source);
            var fresh = formDeserializer.Deserialize(module.Owner, source, errorSink, blobs);
            if (fresh != null)
            {
                // StripHeader above is a no-op for these kinds, so Code still holds the whole file.
                module.UpdateCode(fresh.Code);
                if (module.FormPart is { } existing)
                {
                    // Update the existing FormPart in place so an open designer's reference stays valid
                    // (the designer's FormEditViewModel.FormDefinition points at this instance).
                    existing.UpdateCode(fresh.Code);
                    existing.UpdateComponents(fresh.Components);
                    existing.AdoptFidelityState(fresh);
                }
                else
                {
                    fresh.AbsolutePath = path;
                    module.UpdateFormPart(fresh);
                }
            }
        }

        SnapshotRenderBaseline(module);
        return true;
    }

    public async Task MakeProject()
    {
        if (archives?.IsActive == true) return;
        if (projectManager.StartupProject == null)
        {
            await windowManager.MessageBox(localization.GetString("Str.MakeProject.NoStartup"), icon: MessageBoxIcon.Information);
            return;
        }

        await MakeProject(projectManager.StartupProject);
    }

    public async Task EditProjectProperties(ProjectDefinition project)
    {
        var vm = new ProjectPropertiesViewModel(project, proposed =>
            !ProjectNaming.IsValidName(proposed)
                ? string.Format(localization.GetString("Str.Naming.Msg.NotAVb6Name"), proposed)
            : ProjectNaming.IsProjectNameTaken(projectManager.LoadedProjects, proposed, project)
                ? string.Format(localization.GetString("Str.Naming.Msg.ProjectNameTaken"), proposed)
            : null);
        if (!await windowManager.ShowDialog(vm))
            return;

        vm.Apply(project);
    }

    public async Task EditProjectReferences(ProjectDefinition project)
    {
        var vm = new ReferencesViewModel(project, referenceLibraryService, windowManager);
        if (!await windowManager.ShowDialog(vm))
            return;
        vm.Apply(project);
    }

    public async Task EditProjectComponents(ProjectDefinition project)
    {
        var vm = new ComponentsViewModel(project, windowManager, localization);
        await windowManager.ShowDialog(vm);
    }

    public async Task MakeProject(ProjectDefinition projectDefinition)
    {
        try
        {
            // The warning every other save shows. The refusal is an OperationCanceledException, so it then leaves
            // through the cancellation arm below. A form or the project file used to reach the fatal-error box
            // instead, with .NET's message in place of the document and the code page.
            await WithSaveRefusals(() => MakeProjectInternal(projectDefinition));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            await windowManager.MessageBox(string.Format(localization.GetString("Str.MakeProject.Failed"), e.Message), icon: MessageBoxIcon.Error);
            throw;
        }
    }

    private async Task MakeProjectInternal(ProjectDefinition projectDefinition)
    {
        if (OperatingSystem.IsBrowser())
        {
            await windowManager.MessageBox(localization.GetString("Str.MakeProject.Browser"), icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // Named now and created only once there is something to put in it.
        var tempPath = Path.Join(Path.GetTempPath(), "hexide-make-" + Guid.NewGuid().ToString("N"));

        // A form or module HexIDE cannot reproduce is refused before the picker too, as Save As refuses it, and
        // every one is named at once. The write loop below still refuses one that becomes unfaithful while the
        // picker is open.
        if (projectDefinition.Forms.Cast<object>().Concat(projectDefinition.Modules).Where(RefusesUnfaithful).ToList()
            is { Count: > 0 })
        {
            await ReportRefusedSaves();
            throw new OperationCanceledException();
        }

        // Every file the package will hold is checked against its code page BEFORE the developer is asked
        // where to put the program, for the reason the faithful-save gate sits above the Save As picker: the
        // question is wasted if the answer will never be used. No source file's content depends on where the
        // program goes. Above the standalone-runtime check too, which also makes this reachable in a test.
        //
        // Each is checked in its OWN encoding, which is what the loop below writes it in. The project file's
        // own name is not in its text, so the name the program will give it is not needed yet.
        EnsureEncodable([
            .. projectDefinition.Forms.Select(form =>
                new Destination(form, Path.Join(tempPath, Path.ChangeExtension(form.Name, "frm")))),
            .. projectDefinition.Modules.Select(module =>
                new Destination(module, Path.Join(tempPath, module.Name + "." + ModuleExtension(module.Kind)))),
            new Destination(projectDefinition, Path.Join(tempPath, projectDefinition.Name + ".vbp"))
        ]);

        var exePath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = OperatingSystem.IsWindows() ? "exe" : "",
            SuggestedFileName = projectDefinition.Name + (OperatingSystem.IsWindows() ? ".exe" : ""),
            FileTypeChoices = OperatingSystem.IsWindows() ? [new("Windows EXE") { Patterns = ["*.exe"] }] : [new("All Files") { Patterns = ["*.*"] }]
        });

        if (exePath == null)
            throw new OperationCanceledException();

        if (StandaloneRunnerName() is not { } runnerName)
        {
            await windowManager.MessageBox(localization.GetString("Str.MakeProject.UnsupportedPlatform"), icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        var standaloneFolder = Path.Join(RuntimeDirectory, "standalone");
        var runner = new FileInfo(Path.Join(standaloneFolder, runnerName));
        if (!runner.Exists)
        {
            await windowManager.MessageBox(string.Format(localization.GetString("Str.MakeProject.RuntimeNotFound"),
                standaloneFolder, runnerName), icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        var exeFileName = Path.GetFileNameWithoutExtension(exePath);

        Directory.CreateDirectory(tempPath);

        var originalAbsolutePath = projectDefinition.AbsolutePath;
        var originalFormPaths = projectDefinition.Forms.Select(x => (x, x.AbsolutePath)).ToList();
        // Modules are repointed and restored exactly as forms are. They used not to be written at ALL, and
        // because the .vbp records every item by AbsolutePath, leaving them on their original paths made
        // ProjectSerializer emit each one relative to the TEMP directory — producing lines like
        // `Module1; ..\..\..\Users\me\Projects\Real\Module1.bas`. So the package both omitted every .bas,
        // .cls, .ctl and .pag and published the layout of the developer's source tree. (#149)
        var originalModulePaths = projectDefinition.Modules.Select(x => (x, x.AbsolutePath)).ToList();

        // try/finally because the restore below is not bookkeeping — it is the only thing putting the model
        // back after every form has been repointed into a temp directory. Any escape between here and there
        // leaves the whole project pointing at %TEMP%, and the next ordinary Ctrl+S writes there. That was
        // survivable while this block could not fail; a refused form makes it an early exit. (#148)
        var refused = new List<string>();
        try
        {
            foreach (var form in projectDefinition.Forms)
                if (!SerializeFormToFile(form, Path.Join(tempPath, Path.ChangeExtension(form.Name, "frm")),
                        adoptHeader: false))
                    refused.Add(form.Name);

            // Modules too. SaveModuleCore rather than SaveModule, because this is a batch: a refusal is
            // banked and reported once below, with the forms', rather than one dialog per module.
            //
            // The path is assigned first so the picker is never reached — a Make must not stop to ask where
            // to put a file, and a module that has never been saved still belongs in the package.
            foreach (var module in projectDefinition.Modules)
            {
                module.AbsolutePath = Path.Join(tempPath, module.Name + "." + ModuleExtension(module.Kind));
                // announceSave: false — this writes a COPY into a temporary directory and restores every
                // AbsolutePath in the finally below. The developer's files are untouched, so announcing a
                // save here would report one that never happened, and point a server at a path that is
                // about to be deleted. (Forms take SerializeFormToFile directly and never reach the save
                // core, so only this half needs saying.)
                if (!await SaveModuleCore(module, saveAs: false, announceSave: false))
                    refused.Add(module.Name);
            }

            // Abort the whole Make rather than package a project whose .vbp names a form that was never
            // written. Omitting the form silently produces an archive that fails on open, at a remove from
            // the cause; refusing costs nothing, because the source project has not been touched.
            if (refused.Count > 0)
            {
                foreach (var name in refused)
                    RecordRefusal(name);
                await ReportRefusedSaves();
                throw new OperationCanceledException();
            }

            SerializeOnlyProjectToFile(projectDefinition, Path.Join(tempPath, Path.ChangeExtension(exeFileName, "vbp")),
                includeRelatedDocuments: false);

            EncodePackageSources(projectDefinition);

            // This is not a .dll file, but it will look better :wink: :wink:
            var archivePath = Path.ChangeExtension(exePath, "dll")!;
            AtomicFileWriter.Write(archivePath, stream => ZipFile.CreateFromDirectory(tempPath, stream));
        }
        finally
        {
            // vvv this is a bad design. TODO a better way to handle paths
            projectDefinition.AbsolutePath = originalAbsolutePath;
            foreach (var (form, original) in originalFormPaths)
                form.AbsolutePath = original;
            foreach (var (module, original) in originalModulePaths)
                module.AbsolutePath = original;

            try { Directory.Delete(tempPath, true); } catch { /* best effort — the temp dir is disposable */ }
        }

        // The program is the runner, renamed, beside its project archive. The runner is one self-contained file holding .NET,
        // its own assemblies and the native libraries, so nothing else goes with it (#1971). It used to be the runner's
        // apphost and three native libraries, which ran only while the runner was compiled AOT; since #30 the apphost has
        // needed HexIDE.Standalone.dll beside it, and no made program started.
        AtomicFileWriter.Copy(runner.FullName, exePath);
    }

    /// <summary>The folder Make finds the standalone runtime in: the IDE's own, whatever the current directory is (#1947).</summary>
    internal string RuntimeDirectory { get; set; } = AppContext.BaseDirectory;

    /// <summary>
    /// The runner a made program is, in <c>standalone/</c> beside the IDE: one self-contained single-file executable per
    /// platform (#1971). Null where none is built.
    /// </summary>
    internal static string? StandaloneRunnerName() =>
        OperatingSystem.IsWindows() ? "HexIDE.Standalone.exe"
        : OperatingSystem.IsMacOS() || OperatingSystem.IsLinux() ? "HexIDE.Standalone"
        : null;

    // Only the disposable copies: a made program must read the same text on any host code page (#731).
    internal static void EncodePackageSources(ProjectDefinition project)
    {
        foreach (var form in project.Forms)
            Convert(form.AbsolutePath!, form.TextEncoding);
        foreach (var module in project.Modules)
            Convert(module.AbsolutePath!, module.TextEncoding);
        Convert(project.AbsolutePath!, project.TextEncoding);

        static void Convert(string path, Vb6TextEncoding encoding)
        {
            var text = Vb6TextFile.Decode(File.ReadAllBytes(path), encoding);
            AtomicFileWriter.WriteAllBytes(path, Vb6TextFile.Encode(text, Vb6TextEncoding.Utf8WithBom));
        }
    }

    public async Task SaveProject(ProjectDefinition project, bool saveAs)
    {
        // try/finally so the drain runs even when the batch is abandoned part-way. A dismissed picker throws
        // OperationCanceledException, which skipped the report and left every refusal banked — to surface
        // during the next unrelated save, naming files that were not in it. That is the leak #145 fixed for
        // the lone path and left open here.
        try
        {
            await WithSaveRefusals(() => SaveFiles([.. FilesOf(project, saveAs)]));
        }
        finally
        {
            await ReportRefusedSaves();
        }
    }

    /// <summary>A project's files in the order Save Project writes them: forms, modules, then the project file.</summary>
    private static IEnumerable<(object Document, bool SaveAs)> FilesOf(ProjectDefinition project, bool saveAs) =>
    [
        .. project.Forms.Select(form => ((object)form, false)),
        .. project.Modules.Select(module => ((object)module, false)),
        (project, saveAs)
    ];

    public Task SaveProjectToDirectory(ProjectDefinition project, string directory) => WithSaveRefusals(async () =>
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // Refused before anything is written, and reported here rather than banked. This method has no drain
        // of its own, so a refusal added to the shared list would sit there and surface during the next
        // unrelated Save Project, naming a file that was not part of it. Throwing keeps the report attached
        // to the operation that caused it, and stops a .vbp being written that names forms which are not
        // beside it. (#148)
        var refused = project.Forms.Where(form => !IsWritable(form)).Select(form => form.Name)
            .Concat(project.Modules.Where(module => !IsWritable(module)).Select(module => module.Name))
            .ToList();
        if (refused.Count > 0)
            throw new InvalidOperationException(
                $"Cannot write this project to {directory}: HexIDE cannot reproduce "
              + $"{string.Join(", ", refused)}, so nothing was written.");

        // Modules as well. Writing the forms and the .vbp but not the modules produced a directory whose
        // project file named .bas, .cls and .ctl files that were not in it — and, because every item is
        // recorded by AbsolutePath, named them by a path back to wherever they had been. (#149)
        //
        // Each file is repointed as it is written, and permanently, unlike Make's: this is "write the project
        // to this directory", so the project genuinely lives there afterwards.
        List<Destination> files =
        [
            .. project.Forms.Select(form => new Destination(form, Path.Join(directory, form.Name + ".frm"))),
            .. project.Modules.Select(module =>
                new Destination(module, Path.Join(directory, module.Name + "." + ModuleExtension(module.Kind)))),
            new Destination(project, Path.Join(directory, project.Name + ".vbp"))
        ];
        EnsureEncodable(files);
        Directory.CreateDirectory(directory);
        // Not each member's own Save: a member written to a file other than its own has its attribute qualifiers
        // rewritten for that reason alone, as VB6's Save As writes them, and one written back to its own file is a
        // project save's member like any other (RewrittenQualifiers, #273 task 4.4).
        foreach (var file in files)
        {
            var existed = File.Exists(file.Path);
            if (Write(file, announceSave: true) && !existed && lspClient is not null)
                await lspClient().DidCreateFilesAsync([new Uri(Path.GetFullPath(file.Path)).AbsoluteUri]);
        }
        if (lspClient is not null)
            await lspClient().WorkspaceChangedAsync();
    });

    public string NextFreeFormName(ProjectDefinition project) =>
        ProjectNaming.NextFreeName(project, "Form", name => IsFileInTheWay(project, name + ".frm"));

    public string NextFreeModuleName(ProjectDefinition project, ModuleKind kind) =>
        ProjectNaming.NextFreeName(project, ModulePrefix(kind),
            name => IsFileInTheWay(project, name + "." + ModuleExtension(kind)));

    /// <summary>
    /// Whether something is already where a new member's file, <paramref name="fileName"/> in the project's folder,
    /// would go: a file, the binary file beside it, or a line the project file keeps for a member that did not load
    /// and that names it. False for a project whose folder is not chosen yet.
    /// </summary>
    /// <remarks>
    /// The name a new member is given is the file name its first save suggests, so a name whose file is taken would
    /// suggest a stranger's file (#738). Only the suggestion is protected here: nothing is written as a member is
    /// added (#500), and the save's own picker is where a developer, or an automation caller's armed answer, decides
    /// where the file goes.
    /// </remarks>
    private static bool IsFileInTheWay(ProjectDefinition project, string fileName)
    {
        // Not ProjectFilesDirectory, which chooses a scratch folder for a project with none: there is nothing in a
        // folder not chosen yet, and asking for a name must not choose one.
        if (ChosenFilesDirectory(project) is not { } dir)
            return false;

        var path = Path.Join(dir, fileName);
        // Only the designer kinds have a binary file. CompanionBinaryExtension answers .frx for a .bas or a .cls too.
        return File.Exists(path)
            || Path.GetExtension(fileName).ToLowerInvariant() is ".frm" or ".ctl" or ".pag"
               && File.Exists(Path.ChangeExtension(path, CompanionBinaryExtension(path)))
            || project.PreservedItemLines.Any(line => KeptLineNames(project, line, path));
    }

    public Task<FormDefinition> AddNewForm(ProjectDefinition project, string name, bool interactive = true) =>
        WithSaveRefusals(() =>
        {
            var form = FormDefinition.NewForm(project, FormComponentClass.Instance, name);
            form.TextEncoding = HostAnsi;
            EnsureNewMemberEncodable(project, form, name + ".frm");
            // No file until the project is saved, as in VB6 (#489, #500). The header its first save will write is
            // rendered now and held, so the code window shows what the file will say, and the next refresh has the
            // header it replaces to compare against (#885).
            form.RecordDesignerText(FormCodeText.DesignerHalfOf(RenderForm(form, name + ".frm").Text, form.Code));
            project.AddForm(form);
            return Task.FromResult(form);
        }, interactive);

    public Task<ModuleDefinition> AddNewModule(ProjectDefinition project, string name, ModuleKind kind,
        bool interactive = true) =>
        WithSaveRefusals(() => Task.FromResult(
            AddNewModuleToProject(project, new ModuleDefinition(project, name, kind) { TextEncoding = HostAnsi })), interactive);

    public Task<ModuleDefinition> AddNewUserControl(ProjectDefinition project, string name, bool interactive = true) =>
        AddNewDesignerModule(project, name, ModuleKind.UserControl, interactive);

    public Task<ModuleDefinition> AddNewPropertyPage(ProjectDefinition project, string name, bool interactive = true) =>
        AddNewDesignerModule(project, name, ModuleKind.PropertyPage, interactive);

    private Task<ModuleDefinition> AddNewDesignerModule(
        ProjectDefinition project, string name, ModuleKind kind, bool interactive) =>
        WithSaveRefusals(() =>
        {
            // As VB6 makes one, designer half and Attribute lines alike (#1538).
            var module = ModuleDefinition.NewDesigner(project, name, kind,
                FormComponentClass.ForRoot(kind == ModuleKind.UserControl ? "VB.UserControl" : "VB.PropertyPage"));
            module.TextEncoding = HostAnsi;
            return Task.FromResult(AddNewModuleToProject(project, module));
        }, interactive);

    /// <summary>
    /// Adds a module just created to the project. Its file is written by the project's next save, as VB6 writes it
    /// (#489, #500): nothing reaches disk here, and no folder is chosen.
    /// </summary>
    private ModuleDefinition AddNewModuleToProject(ProjectDefinition project, ModuleDefinition module)
    {
        var fileName = module.Name + "." + ModuleExtension(module.Kind);
        EnsureNewMemberEncodable(project, module, fileName);
        // The header the code window will show, rendered as the first save will write it and held from now, as a
        // new form's is (#885). Without it a brand-new UserControl or PropertyPage opens with no designer block.
        DesignerPartOf(module)?.RecordDesignerText(
            FormCodeText.DesignerHalfOf(RenderModule(module, fileName).Text, module.Code));
        project.AddModule(module);
        return module;
    }

    /// <summary>
    /// Refuses a new member whose name its code page cannot hold, before a folder is chosen, a directory
    /// created or anything added, so a refused name leaves nothing behind.
    /// </summary>
    /// <param name="fileName">The file the member will be written to. Its own text depends on nothing else
    /// about where that is.</param>
    private void EnsureNewMemberEncodable(ProjectDefinition project, object member, string fileName)
    {
        // Rendered whole, in the encoding it will be written in: the name reaches the Begin line, the
        // Attribute VB_Name line and a new form's Caption.
        EnsureEncodable([new Destination(member, fileName)]);
        // The next Save Project names it in the project file too, as `Module=Name; Name.bas` or
        // `Form=Name.frm`, in the project's code page. That file is the one that cannot hold it then, so the
        // refusal names the project.
        Vb6TextFile.EncodeDocument(project.Name, fileName, project.TextEncoding);
    }

    /// <summary>
    /// Whether a name a file asked for can be used in a project, and why not when it cannot.
    /// </summary>
    /// <remarks>
    /// Checked at adoption rather than left for the build. VB6 refuses a project whose form and module
    /// share a name with <c>Name conflicts with existing module, project, or object library</c> and does
    /// not say which two conflicted; refusing here can, and does.
    /// </remarks>
    private static AdoptionRefusal CheckAdoptedName(ProjectDefinition project, string name) =>
        !ProjectNaming.IsValidName(name) ? AdoptionRefusal.NameNotValid
        : ProjectNaming.IsNameTaken(project, name) ? AdoptionRefusal.NameTaken
        : AdoptionRefusal.None;

    public async Task<Adopted<FormDefinition>> AddExistingForm(ProjectDefinition project, string absolutePath)
    {
        var (source, bytes, sourceEncoding, byteForByte) = await ReadSourceAsync(absolutePath);

        // Form text before companion: the .frm's cited offsets partition the .frx.
        var blobs = await LoadCompanionBlobs(absolutePath, source);
        var form = new FormDeserializer().Deserialize(
            project, source, new DeserializeErrorSink(localization), blobs, BuildUserControlRegistry(project));
        if (form == null)
            return Adopted<FormDefinition>.Unreadable();

        // The form's own name, off its designer line, checked before anything is recorded: a refused form
        // must leave the project and the baseline store exactly as it found them.
        var name = form.Name;
        if (CheckAdoptedName(project, name) is var refusal && refusal != AdoptionRefusal.None)
            return Adopted<FormDefinition>.Refused(refusal, name);

        // Recorded only once the parse succeeded. A baseline for a form that was never added would leave
        // the dirty check answering about a file the project does not carry.
        baselineStore.Record(absolutePath, bytes);
        form.AbsolutePath = absolutePath;
        form.TextEncoding = sourceEncoding;
        project.AddForm(form);
        SnapshotRenderBaseline(form);
        ForgetKeptLineFor(project, absolutePath);
        return Adopted<FormDefinition>.Added(form, name, byteForByte ? HostAnsi.CodePage : null);
    }

    public async Task<Adopted<ModuleDefinition>> AddExistingModule(
        ProjectDefinition project, string absolutePath, ModuleKind kind)
    {
        var (source, bytes, sourceEncoding, byteForByte) = await ReadSourceAsync(absolutePath);

        // .bas/.cls: strip the VB6 header so Code is the body only (a no-op for .ctl/.pag).
        var (preservedHeader, body) = ModuleFileFormat.SplitHeader(source, kind);
        var name = ModuleNameFor(absolutePath, preservedHeader);

        // Before the baseline is recorded, so a refused file leaves no trace: a baseline for a module the
        // project does not carry would have the dirty check answering about a stranger.
        if (CheckAdoptedName(project, name) is var refusal && refusal != AdoptionRefusal.None)
            return Adopted<ModuleDefinition>.Refused(refusal, name);

        baselineStore.Record(absolutePath, bytes);
        var module = new ModuleDefinition(project, name, kind)
        {
            AbsolutePath = absolutePath,
            TextEncoding = sourceEncoding,
        };
        module.RecordOriginalHeader(preservedHeader);
        module.UpdateCode(body);

        // .ctl and .pag carry a designer half, and this is the same call the load path makes — which is
        // what stops an adopted UserControl being text-only while a loaded one opens in the designer.
        if (kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var blobs = await LoadCompanionBlobs(absolutePath, source);
            var formPart = new FormDeserializer().Deserialize(project, source, new DeserializeErrorSink(localization), blobs);
            if (formPart != null)
            {
                formPart.AbsolutePath = absolutePath;
                module.UpdateFormPart(formPart);
                // SplitHeader is a no-op for .ctl/.pag, so Code still holds the whole file — replace it
                // with the body only, or the next save emits the Begin..End header twice.
                module.UpdateCode(formPart.Code);
            }
        }

        project.AddModule(module);
        SnapshotRenderBaseline(module);
        ForgetKeptLineFor(project, absolutePath);
        return Adopted<ModuleDefinition>.Added(module, name, byteForByte ? HostAnsi.CodePage : null);
    }

    public Task<RelatedDocumentDefinition> AddExistingRelatedDocument(
        ProjectDefinition project, string absolutePath)
    {
        // No OriginalItemLine. This document is joining the project now, so there is no line to preserve
        // and RelatedDoc= is exactly what should be written. The preserved-line case belongs to entries
        // RECLASSIFIED on read, where rewriting would edit the .vbp on the strength of an inference.
        var document = new RelatedDocumentDefinition(project, Path.GetFileName(absolutePath), absolutePath);
        project.AddRelatedDocument(document);
        return Task.FromResult(document);
    }

    /// <summary>
    /// The module's own name, read out of its VB6 header, falling back to the filename.
    ///
    /// <para>
    /// A .vbp's <c>Module=Name; path</c> field and the file's <c>Attribute VB_Name</c> can disagree, and
    /// VB_Name is the one that decides the module's identity to code that qualifies a call through it.
    /// Taking the filename would silently rename an adopted module, and the rename would only surface
    /// later, as something failing to resolve.
    /// </para>
    /// </summary>
    private static string ModuleNameFor(string absolutePath, string? header)
    {
        if (header is not null)
        {
            foreach (var line in header.Split('\n'))
            {
                var trimmed = line.TrimStart();
                if (!trimmed.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
                    continue;
                var open = trimmed.IndexOf('"');
                var close = open >= 0 ? trimmed.IndexOf('"', open + 1) : -1;
                if (close > open)
                    return trimmed[(open + 1)..close];
            }
        }

        return Path.GetFileNameWithoutExtension(absolutePath);
    }

    /// <summary>
    /// Where this project's files live right now — its own directory once saved, and a private scratch
    /// directory before that.
    ///
    /// <para>
    /// <b>The scratch directory is unique per project instance, and that is the whole point.</b> It used
    /// to be <c>%TEMP%/hexide_{Name}</c>, and every new Standard EXE is named <c>Project1</c>, so every
    /// first project of every session shared one folder. That was not a theoretical collision: a
    /// development machine held eleven files from four days of unrelated sessions in a single directory.
    /// Adding a <c>Module1</c> where a previous session had left one destroyed it silently; Save As
    /// copied whatever else happened to be lying there; two IDE instances wrote to the same place at
    /// once. None of it announced itself (hexide-io/HexIDE#260).
    /// </para>
    ///
    /// <para>
    /// Assigned once per project and remembered, because callers ask repeatedly and every answer has to
    /// name the same place — a freshly-generated directory per call would scatter one project's files
    /// across many.
    /// </para>
    /// </summary>
    public static string ProjectFilesDirectory(ProjectDefinition project)
    {
        if (ChosenFilesDirectory(project) is { } chosen)
            return chosen;

        // The suffix is what makes it private. The name is kept in front of it so a user who goes looking
        // in TEMP can still tell which directory belongs to what.
        var scratch = Path.Combine(
            Path.GetTempPath(),
            $"hexide_{SanitiseForPath(project.Name)}_{Guid.NewGuid():N}");

        project.WorkingDirectory = scratch;
        return scratch;
    }

    /// <summary>
    /// The folder <see cref="ProjectFilesDirectory"/> answers once one is chosen: the project file's, or the scratch
    /// folder it was given. Null for a project with neither, rather than choosing one.
    /// </summary>
    private static string? ChosenFilesDirectory(ProjectDefinition project) =>
        project.AbsolutePath is { } p ? Path.GetDirectoryName(p)!
        : project.WorkingDirectory is { Length: > 0 } existing ? existing
        : null;

    /// <summary>
    /// Makes a project name safe to embed in a directory name.
    ///
    /// <para>
    /// A project name is user text and reaches here unfiltered; a name containing a separator would
    /// otherwise place the scratch directory somewhere other than TEMP, which is the failure this method
    /// exists to prevent rather than a theoretical one.
    /// </para>
    /// </summary>
    private static string SanitiseForPath(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Select(c => invalid.Contains(c) ? '_' : c)]).Trim();
        if (cleaned.Length == 0) return "project";

        // Truncated because the name is user text and the whole path still has to fit: the GUID that
        // follows it is what guarantees uniqueness, so losing the tail of a long name costs nothing.
        return cleaned.Length <= 48 ? cleaned : cleaned[..48];
    }

    /// <summary>Saves everything the user left ticked in the save-changes prompt.</summary>
    private async Task SaveSelected(SaveChangesViewModel changedFilesVm)
    {
        // try/finally for the same reason as SaveProject: a dismissed picker anywhere in this batch used to
        // skip the drain and leave refusals banked for the next unrelated save.
        try
        {
            await WithSaveRefusals(() => SaveFiles(
            [
                .. changedFilesVm.SelectedFiles.Where(f => f.Form != null).Select(f => ((object)f.Form!, false)),
                .. changedFilesVm.SelectedFiles.Where(f => f.Module != null).Select(f => ((object)f.Module!, false)),
                .. changedFilesVm.SelectedFiles.Where(f => f.Project != null).Select(f => ((object)f.Project!, false))
            ]));
        }
        finally
        {
            await ReportRefusedSaves();
        }
    }

    /// <summary>
    /// Tells the developer which forms were not written, and why, once per save rather than per file.
    /// Silence here would be the worst of both worlds — the file is protected but the user believes their
    /// edit was persisted.
    /// </summary>
    /// <param name="interactive">False for an automation caller, which learns of each refusal from the
    /// save's result instead. The list is still cleared, so the next save does not report it.</param>
    private async Task ReportRefusedSaves(bool interactive = true)
    {
        if (refusedThisSave.Count == 0)
            return;

        var names = string.Join("\n", refusedThisSave.Select(n => $"  • {n}"));
        var singular = refusedThisSave.Count == 1;
        refusedThisSave.Clear();

        // The body is a whole localized sentence rather than an English frame with a reason injected into
        // it. The earlier version read "These forms were not saved, because it contains…" — the reason is
        // phrased for one form and the frame for many — and worse, that reason was a hardcoded English
        // literal from FormDeserializer appearing verbatim in a user-facing dialog in every language.
        // UnfaithfulSaveReason stays as-is for the log, which is developer-facing.
        var body = localization.GetString(singular
            ? "Str.Dialog.UnfaithfulSave.Body.One"
            : "Str.Dialog.UnfaithfulSave.Body.Many");

        if (interactive)
            await windowManager.MessageBox(
                string.Format(body, names),
                "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    public Task SaveAllProjects(bool saveAs)
    {
        var files = projectManager.LoadedProjects.SelectMany(project => FilesOf(project, saveAs)).ToList();
        if (projectManager.CurrentGroup is { } group)
            files.Add((group, saveAs));
        return SaveAll(files);
    }

    /// <summary>
    /// Every project at once, as one operation: one refusal leaves every project and the group as they
    /// were, and the refusals HexIDE cannot reproduce are reported in one dialog rather than one per project.
    /// </summary>
    private async Task SaveAll(IReadOnlyList<(object Document, bool SaveAs)> files)
    {
        try
        {
            await WithSaveRefusals(() => SaveFiles(files));
        }
        finally
        {
            await ReportRefusedSaves();
        }
    }

    /// <summary>
    /// Write a form's two halves — the designer text and its companion binary — or write neither.
    ///
    /// Returns false when the form was refused, in which case nothing was written and nothing on the model
    /// was repointed. Reporting is the caller's: a lone save says so immediately, a batch collects.
    /// </summary>
    /// <param name="adoptHeader">
    /// False for a write that is a COPY rather than the developer's file. Make packages every form into a
    /// temporary directory under a name built from the form's own name, so the header it renders cites a
    /// companion that may not be the one the real file cites -- and adopting it would leave that in the
    /// model and in the open code window after the temp directory has been deleted. The path repointing
    /// beside it is already undone in a finally for the same reason; this is the half that could not be.
    /// </param>
    /// <param name="ownCommand">As <see cref="SaveFiles"/>'s: the form is written by its own Save or Save As.</param>
    // internal rather than private so the adoptHeader contract can be asserted directly; the Make
    // path that depends on it cannot be driven from a test (it needs the published standalone runtime).
    internal bool SerializeFormToFile(FormDefinition form, string formPath, bool adoptHeader = true, bool ownCommand = false, SaveRewriteReport? rewrites = null)
    {
        // ONE ruler, asked ONCE, before either half is written. (#148)
        //
        // The text write used to be unconditional while the companion write carried a refusal of its own,
        // decided on a different measure. When that refusal fired, the .frm went to disk citing freshly
        // renumbered offsets beside a companion that still held the OLD partition — FrxSerializer assigns
        // each record's offset by where it lands, so every citation moves the moment the record set does.
        // The damaged pair then reopened as FAITHFUL, because the citations that would have flagged it are
        // precisely what the renumbering overwrote.
        //
        // The two measures could never agree. This one is answered at LOAD, from the file, by counting what
        // the .frm cites against what reached the model. The other walked the PRODUCED companion as flat
        // length-prefixed records — a reader whose own documentation says it is wrong for List and ItemData,
        // and which called VB6's own ODBC Log In unsavable while it reproduced byte for byte.
        //
        // Whether HexIDE can reproduce a file is a question about the file, so it is answered where the file
        // is read. The save path's only remaining job is to keep the two halves in step.
        if (!form.CanSaveFaithfully)
        {
            Log.Warning("Refusing to write {Form}: {Reason}", form.Name, form.UnfaithfulSaveReason);
            return false;
        }

        // Before the render, because the qualifiers have to reach the file (#273 task 4.4). A copy leaves the
        // model alone, as it leaves the header. Before the repointing below, too, which is what lets the rewrite
        // tell a write to the form's own file from a write somewhere else.
        if (adoptHeader
            && RewrittenQualifiers(form, formPath, ownCommand, FormCodeText.Prefix(form), form.Code, rewrites) is { } requalified)
            form.UpdateCode(requalified);

        var (frmText, frxContent) = RenderForm(form, formPath);

        // Encoded before either half of the pair changes. An operation has checked this already; this is the
        // backstop for a path that did not.
        var encodedText = Vb6TextFile.EncodeDocument(form.Name, frmText, form.TextEncoding);

        // COMPANION FIRST, and the order is load-bearing.
        //
        // These are two File.Move calls; nothing makes the pair atomic, so an interruption between them
        // always leaves one half of the new save on disk. The order decides WHICH half, and the two
        // outcomes are not equally bad.
        //
        //   text first     -> new .frm beside the old companion. Its renumbered citations still land inside
        //                     the larger, stale file, so they all resolve — to the wrong records. The pair
        //                     reopens as faithful. That is outcome 3: silent, and it launders.
        //   companion first-> old .frm beside the new companion. Its citations were written for the old
        //                     partition, so a shortfall leaves an offset past the end and the load gate
        //                     refuses the form. That is outcome 0: loud, and recoverable.
        //
        // Neither is correct, but a crash should fail towards the one the developer is told about.
        WriteCompanionBinary(formPath, frxContent, form);
        AtomicWriteText(formPath, frmText, encodedText);
        form.AbsolutePath = formPath;

        // The second refresh trigger (#273 task 3.3a). The header the buffer shows becomes the header this
        // save wrote, which is NOT always the one it was opened with: the serializer is a reproduction
        // rather than a byte-faithful copy, and a Save As or a first save to a name of the developer's
        // choosing also rewrites every .frx citation in it. Below the write and above the announcement, so
        // a server reading the buffer and a server reading the file agree about what line one is -- the
        // announcement flushes a pending didChange before its didSave, so the order holds on the wire too.
        if (adoptHeader)
            headerRefresher.Saved(DocumentIdentity.For(form), FormCodeText.DesignerHalfOf(frmText, form.Code));
        return true;
    }

    /// <summary>
    /// The code a save writes for <paramref name="document"/>, with every procedure's attribute run, and every
    /// run a rename of a one-name variable line left, naming its member as declared; or null when the save
    /// writes the code as it stands (#273 task 4.4).
    /// </summary>
    /// <param name="document">The form or module being saved, for <see cref="IsDirty(FormDefinition)"/>.</param>
    /// <param name="path">Where the save writes it, compared with where it lives now.</param>
    /// <param name="ownCommand">The document is written by its own Save or Save As (<see cref="SaveFiles"/>).</param>
    /// <param name="prefix">What the code window composes in front of <paramref name="code"/>.</param>
    /// <param name="code">The document's code, which the flush at the start of the save has just written.</param>
    /// <remarks>
    /// <para>
    /// <b>Whenever VB6 would write the module, and only then.</b> VB6 writes a module's runs afresh every time it
    /// writes the module, changed or not, and its Save Project does not write a module it holds as unchanged
    /// (oracle, "Member attributes", the third IDE session's Q1 to Q3). HexIDE's Save Project writes every member,
    /// so the rewrite is gated on the writes VB6 makes: a document with unsaved changes, a document written by its
    /// own Save or Save As whatever its state, and a document written to a file other than its own, which
    /// <c>SaveProjectToDirectory</c> does and VB6 does only by a Save As. A project's or the group's save of an
    /// unchanged member at its own path writes its bytes as they were read, which is all VB6's leaving the file
    /// alone can be here, so a loaded <c>Attribute Gone.VB_Description</c> under <c>Total</c> survives it, as it
    /// does VB6's. The dirty check is asked last, and only when a rewrite is due, because it renders the document.
    /// </para>
    /// <para>
    /// <b>After the code page check and before the write, and that order is safe.</b> <c>EnsureEncodable</c>
    /// rendered the code as it stood; this changes it after. A rewrite only ever writes a name the same file
    /// already declares, so it adds no character the file did not hold, and what encoded before still encodes.
    /// </para>
    /// </remarks>
    private string? RewrittenQualifiers(object document, string path, bool ownCommand, string prefix, string code, SaveRewriteReport? rewrites)
    {
        var rewritten = MemberAttributeQualifiers.Rewrite(prefix, code);
        if (ReferenceEquals(rewritten, code))
            return null;
        var written = document switch
        {
            FormDefinition form => ownCommand || !IsItsOwnFile(form.AbsolutePath, path) || IsDirty(form),
            ModuleDefinition module => ownCommand || !IsItsOwnFile(module.AbsolutePath, path) || IsDirty(module),
            _ => throw new ArgumentException($"HexIDE does not rewrite a {document.GetType().Name}.", nameof(document)),
        };
        if (written)
            rewrites?.Record(prefix, code);
        return written ? rewritten : null;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is the file a document already lives at, <paramref name="own"/>, as the host's
    /// file system compares names: without case on Windows only. Never for a document with no file yet.
    /// </summary>
    private static bool IsItsOwnFile(string? own, string path) =>
        own is not null
        && string.Equals(Path.GetFullPath(own), Path.GetFullPath(path),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>
    /// Write, replace, or remove a companion binary (.frx/.ctx/.pgx) beside <paramref name="sourcePath"/>.
    ///
    /// Only ever called for a form the caller has already decided it can reproduce, so regenerating the
    /// companion is safe by then: the records going out are the records that came in. This used to carry a
    /// SECOND refusal of its own, on a different measure from the caller's, which is what let a .frm be
    /// written beside a companion that was not — see <see cref="SerializeFormToFile"/> (#148).
    ///
    /// It keeps one guard, and only one: a companion this form never cited is not ours to delete.
    /// </summary>
    private void WriteCompanionBinary(string sourcePath, byte[]? content, FormDefinition form)
    {
        var companionPath = Path.ChangeExtension(sourcePath, CompanionBinaryExtension(sourcePath));

        if (content is { Length: > 0 })
        {
            AtomicWriteBytes(companionPath, content);
            return;
        }

        if (!File.Exists(companionPath))
            return;

        // Producing no blobs means "delete it" only if this form had blobs to lose. A companion NOTHING
        // cites is read by falling back to a flat walk, so it yields records the model never captured and
        // never cites back — and a form that cited nothing on the way in produces nothing on the way out,
        // every single save. Treating that as "the developer cleared the last picture" deletes a file whose
        // bytes exist nowhere else, on an ordinary save of a form that never referenced it.
        //
        // The old blob-count refusal happened to block this, for the wrong reason. Removing it made the
        // deletion reachable, so the guard is now stated in terms of what the form actually cited.
        if (form.CitedCompanionBlobCount == 0)
        {
            Log.Warning("Leaving {Companion} untouched — {Source} cites no companion content, so this file "
                      + "is not one this save produced or can account for.",
                        Path.GetFileName(companionPath), Path.GetFileName(sourcePath));
            return;
        }

        File.Delete(companionPath);
        baselineStore.Remove(companionPath);
        renderBaselines.Remove(companionPath);
    }

    private static string CompanionBinaryExtension(string sourceFilePath) =>
        ProjectLoader.CompanionBinaryExtension(sourceFilePath);

    /// <summary>
    /// Read the companion binary resource file (.frx / .ctx / .pgx) beside a .frm / .ctl / .pag, if present.
    /// Always route companion reads through here — hardcoding ".frx" silently drops UserControl and
    /// PropertyPage resources, and the save path then deletes the companion it never read.
    /// </summary>
    /// <param name="citingSource">
    /// The designer file's text. Its cited offsets are what partition the companion into records, so
    /// omitting it falls back to walking the file as length-prefixed blobs — which cannot see a
    /// <c>List</c>/<c>ItemData</c> record at all. Pass it wherever it is to hand.
    /// </param>
    private async Task<IReadOnlyDictionary<int, byte[]>?> LoadCompanionBlobs(
        string sourceFilePath, string? citingSource = null)
    {
        var companionPath = Path.ChangeExtension(sourceFilePath, CompanionBinaryExtension(sourceFilePath));
        if (!File.Exists(companionPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(companionPath);
        baselineStore.Record(companionPath, bytes);
        return citingSource is null
            ? FrxDeserializer.Read(bytes)
            : FrxDeserializer.Read(bytes, citingSource);
    }

    // Instance (not static) so each atomic write records a baseline. Recording the just-written
    // content immediately — well before the resulting FileSystemWatcher event is processed (it is
    // debounced) — is the primary self-write-suppression mechanism for the file watcher: the watcher
    // re-hashes the file, sees it matches the baseline, and ignores the IDE's own save.
    //
    // Takes the encoded bytes rather than encoding them itself, so no write can encode without naming its
    // document (Vb6TextFile.EncodeDocument) and every refusal names it.
    private void AtomicWriteText(string targetPath, string content, byte[] bytes)
    {
        AtomicFileWriter.WriteAllBytes(targetPath, bytes);
        baselineStore.Record(targetPath, bytes);
        renderBaselines[targetPath] = FileHasher.Hash(content);
    }

    private void AtomicWriteBytes(string targetPath, byte[] content)
    {
        AtomicFileWriter.WriteAllBytes(targetPath, content);
        baselineStore.Record(targetPath, content);
        renderBaselines[targetPath] = FileHasher.Hash(content);
    }

    /// <summary>The file extension VB6 gives a module of this kind.</summary>
    private static string ModuleExtension(ModuleKind kind) => kind switch
    {
        ModuleKind.ClassModule  => "cls",
        ModuleKind.UserControl  => "ctl",
        ModuleKind.PropertyPage => "pag",
        _                       => "bas"
    };

    /// <summary>What VB6 numbers a new module of this kind after: <c>Module1</c>, <c>Class1</c> and so on.</summary>
    private static string ModulePrefix(ModuleKind kind) => kind switch
    {
        ModuleKind.ClassModule  => "Class",
        ModuleKind.UserControl  => "UserControl",
        ModuleKind.PropertyPage => "PropertyPage",
        _                       => "Module"
    };

    /// <summary>
    /// Save one module on its own. A refusal is reported at the moment it happens — mirroring
    /// <see cref="SaveForm"/>, and for the same reason: without a drain the entry survives in
    /// <c>refusedThisSave</c> and surfaces during the next unrelated save, naming a file that was not in
    /// that batch. Returns false when the module was refused and nothing was written.
    /// </summary>
    /// <inheritdoc cref="SaveForm" path="/param[@name='interactive']"/>
    public async Task<bool> SaveModule(ModuleDefinition module, bool saveAs, bool interactive = true, SaveRewriteReport? rewrites = null)
    {
        try
        {
            return await WithSaveRefusals(() => SaveModuleCore(module, saveAs, rewrites: rewrites), interactive);
        }
        finally
        {
            await ReportRefusedSaves(interactive);
        }
    }

    /// <summary>
    /// Writes a module and, if it was written, says so, banking a refusal rather than reporting it: Make
    /// reports every refusal in one dialog.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>internal</c> rather than private so a test can reach the <paramref name="announceSave"/> flag.
    /// Its only production caller that passes <see langword="false"/> is Make EXE, which cannot be driven
    /// without a published standalone runtime — so without this the one exclusion in "which writes count
    /// as a save" would have no test at all, which mutation testing confirmed.
    /// </para>
    /// <para>
    /// The module is written on its own, so the write is its own Save or Save As (<see cref="SaveFiles"/>'s
    /// <c>ownCommand</c>). Make's copy, which passes <paramref name="announceSave"/> false, rewrites nothing either
    /// way, because a write that adopts no header rewrites no qualifier.
    /// </para>
    /// </remarks>
    internal async Task<bool> SaveModuleCore(ModuleDefinition module, bool saveAs, bool announceSave = true, SaveRewriteReport? rewrites = null) =>
        (await SaveFiles([(module, saveAs)], announceSave, ownCommand: true, rewrites: rewrites)).Count > 0;

    /// <summary>Writes a module the caller has decided HexIDE can reproduce, and repoints it there.</summary>
    /// <param name="ownCommand">As <see cref="SaveFiles"/>'s: the module is written by its own Save or Save As.</param>
    private void WriteModule(ModuleDefinition module, string modulePath, bool adoptHeader, bool ownCommand, SaveRewriteReport? rewrites = null)
    {
        // As SerializeFormToFile: before the render and the repointing, and not for a copy (#273 task 4.4).
        if (adoptHeader
            && RewrittenQualifiers(module, modulePath, ownCommand, FormCodeText.Prefix(module), module.Code, rewrites) is { } requalified)
            module.UpdateCode(requalified);

        var (text, companion) = RenderModule(module, modulePath);
        // Encoded before anything is written, and the module repointed only once everything is.
        var encodedText = Vb6TextFile.EncodeDocument(module.Name, text, module.TextEncoding);
        // Companion first — see SerializeFormToFile for why the order is the difference between a crash
        // leaving a laundered file and a crash leaving a refused one.
        if (DesignerPartOf(module) is { } designerPart)
            WriteCompanionBinary(modulePath, companion, designerPart);
        AtomicWriteText(modulePath, text, encodedText);
        module.AbsolutePath = modulePath;
        // Both halves are the one file, as every load gives them (#474).
        if (DesignerPartOf(module) is { } written)
            written.AbsolutePath = modulePath;

        // As SerializeFormToFile: the .ctl/.pag half of the same refresh trigger (#273 task 3.3a). The code here is
        // the MODULE's, which is the pairing this path writes and the one the code window composes from. Gated
        // for the same reason announceSave exists -- Make writes a copy into a temporary directory and restores
        // every path afterwards, so neither the save nor the header it rendered there is the developer's. A .bas
        // or .cls has no designer half to adopt, but its open buffer still takes the qualifiers the save rewrote.
        if (adoptHeader)
            headerRefresher.Saved(DocumentIdentity.For(module),
                DesignerPartOf(module) is not null ? FormCodeText.DesignerHalfOf(text, module.Code) : null);
    }

    private void SerializeOnlyProjectToFile(ProjectDefinition definition, string projectPath, bool includeRelatedDocuments = true)
    {
        // The second check, at the write, for a route that reaches it without SaveFiles: that refuses before
        // anything is written (EnsureEveryMemberListed), and by here every member it wrote has its path. Not in
        // the serializer, which also renders for the dirty check, where a project holding a form with no file
        // yet is ordinary.
        if (UnlistedMembers(definition, writtenWithIt: _ => false) is { Count: > 0 } unlisted)
            throw new InvalidOperationException(
                $"{definition.Name}'s project file would leave out {string.Join(", ", unlisted)}, which "
              + "have no file, so it was not written.");
        var serialized = new ProjectSerializer().Serialize(definition, projectPath, includeRelatedDocuments: includeRelatedDocuments);
        AtomicWriteText(projectPath, serialized,
            Vb6TextFile.EncodeDocument(definition.Name, serialized, definition.TextEncoding));
        // A kept line names its file from the folder it was read in. A project the loader did not read has that folder
        // pinned here, before its own path moves, or the next save would read its kept lines from the new one (#737).
        if (definition.AbsolutePath is { } previous)
            definition.ItemLinesDirectory ??= Path.GetDirectoryName(previous);
        definition.AbsolutePath = projectPath;
    }

    // ── Dirty detection ───────────────────────────────────────────────────────────────────────
    // "Dirty" means saving would change what is on disk. Each item is rendered through the very
    // serializer its save path uses, then compared against the baseline recorded at load/save — so the
    // check cannot drift from what a save would actually write. There is deliberately no per-model
    // IsDirty flag: that would need setting at every mutation site and would rot silently.
    //
    // Callers must publish ApplyAllUnsavedChangesEvent first, so open editor buffers are flushed into
    // the model before anything is rendered.

    private bool IsDirty(ProjectDefinition project)
    {
        var path = project.AbsolutePath;
        if (path is null)
            return true; // never saved

        // A member with no file changes the project file for certain, since the next save lists it, and the render
        // below cannot show that: the serializer skips a member with no path. The Project Explorer's Add Form adds
        // one, and a project counted clean then stayed out of the close prompt, so the form it listed was saved
        // and the project file never named it (#1359).
        if (UnlistedMembers(project, writtenWithIt: _ => false).Count > 0)
            return true;

        return !BaselineMatches(path, new ProjectSerializer().Serialize(project, path));
    }

    public bool HasUnsavedChanges(FormDefinition form) => IsDirty(form);
    public bool HasUnsavedChanges(ModuleDefinition module) => IsDirty(module);

    private bool IsDirty(FormDefinition form)
    {
        var path = form.AbsolutePath;
        if (path is null)
            return true;

        var (text, binary) = new FormSerializer().Serialize(form, Path.GetFileName(path));
        return !BaselineMatches(path, text) || CompanionWouldChange(path, binary);
    }

    private bool IsDirty(ModuleDefinition module)
    {
        var path = module.AbsolutePath;
        if (path is null)
            return true;

        if (module.FormPart is { } formPart && module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var (text, binary) = new FormSerializer().Serialize(formPart, module.Code, Path.GetFileName(path));
            return !BaselineMatches(path, text) || CompanionWouldChange(path, binary);
        }

        return !BaselineMatches(path, ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader));
    }

    /// <summary>
    /// Hash of what each file rendered to when it was last loaded or saved. Deliberately NOT the
    /// <see cref="IFileBaselineStore"/>, which holds the bytes actually on disk and answers a different
    /// question ("did the file change underneath us?", for the file watcher).
    ///
    /// Comparing a fresh render against the on-disk bytes would conflate "the user edited something" with
    /// "our serializer does not reproduce this file byte-for-byte" — and any serializer infidelity would
    /// then mark an untouched project dirty and prompt on every single close. Comparing render-to-render
    /// asks only whether the model changed, which is the question the save prompt is actually asking.
    /// </summary>
    private readonly Dictionary<string, string> renderBaselines = new(StringComparer.OrdinalIgnoreCase);

    private bool BaselineMatches(string path, string rendered) =>
        renderBaselines.TryGetValue(path, out var known) && known == FileHasher.Hash(rendered);

    /// <summary>True when a save would write, change, or delete the companion binary (.frx/.ctx/.pgx).</summary>
    private bool CompanionWouldChange(string sourcePath, byte[]? binary)
    {
        var companionPath = Path.ChangeExtension(sourcePath, CompanionBinaryExtension(sourcePath));
        if (binary is { Length: > 0 })
            return !renderBaselines.TryGetValue(companionPath, out var known)
                   || known != FileHasher.Hash(binary);

        return renderBaselines.ContainsKey(companionPath); // had one, and a save would now delete it
    }

    /// <summary>
    /// Records what every file in <paramref name="project"/> renders to right now, establishing the
    /// "unedited" point. Called once after a project finishes loading; saves keep themselves current via
    /// <see cref="AtomicWriteText"/> / <see cref="AtomicWriteBytes"/>, which write the rendered content.
    /// </summary>
    private void SnapshotRenderBaselines(ProjectDefinition project)
    {
        if (project.AbsolutePath is { } projectPath)
            renderBaselines[projectPath] = FileHasher.Hash(new ProjectSerializer().Serialize(project, projectPath));

        foreach (var form in project.Forms)
            SnapshotRenderBaseline(form);

        foreach (var module in project.Modules)
            SnapshotRenderBaseline(module);
    }

    /// <summary>
    /// Re-establishes the "unedited" point for a single form. Called after a load and after
    /// <see cref="ReloadFormFromDisk"/> adopts external content — without it the render baseline would
    /// still describe the pre-reload model, so an untouched form would report itself as edited.
    /// </summary>
    private void SnapshotRenderBaseline(FormDefinition form)
    {
        if (form.AbsolutePath is not { } path)
            return;
        var (text, binary) = new FormSerializer().Serialize(form, Path.GetFileName(path));
        RecordRender(path, text, binary);
    }

    /// <inheritdoc cref="SnapshotRenderBaseline(FormDefinition)"/>
    private void SnapshotRenderBaseline(ModuleDefinition module)
    {
        if (module.AbsolutePath is not { } path)
            return;

        if (module.FormPart is { } formPart && module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var (text, binary) = new FormSerializer().Serialize(formPart, module.Code, Path.GetFileName(path));
            RecordRender(path, text, binary);
        }
        else
        {
            renderBaselines[path] = FileHasher.Hash(
                ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader));
        }
    }

    private void RecordRender(string path, string text, byte[]? binary)
    {
        renderBaselines[path] = FileHasher.Hash(text);
        var companionPath = Path.ChangeExtension(path, CompanionBinaryExtension(path));
        if (binary is { Length: > 0 })
            renderBaselines[companionPath] = FileHasher.Hash(binary);
        else
            renderBaselines.Remove(companionPath);
    }
}
