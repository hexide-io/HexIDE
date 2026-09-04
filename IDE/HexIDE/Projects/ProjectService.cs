using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

    public ProjectService(Func<NewProjectViewModel> newProjectVm,
        IWindowManager windowManager,
        IEventBus eventBus,
        IProjectManager projectManager,
        IRecentProjectsService recentProjects,
        IReferenceLibraryService referenceLibraryService,
        IUserSidecarService sidecar,
        IFileBaselineStore baselineStore,
        ILocalizationService localization)
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
    }

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

        var name = $"Project{projectManager.LoadedProjects.Count + 1}";
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

        var name = $"Project{projectManager.LoadedProjects.Count + 1}";

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
            return;

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

        await UnloadAllProjects();

        var files = await windowManager.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Project",
            FileTypeFilter =
            [
                new("Project Files") { Patterns = ["*.vbp", "*.vbg"] },
                new("Project Group") { Patterns = ["*.vbg"] },
                new("Project")       { Patterns = ["*.vbp"] },
                new("All Files")     { Patterns = ["*.*"] }
            ],
            AllowMultiple = false
        });

        if (files == null || files.Count != 1)
            throw new OperationCanceledException();

        await OpenProject(files[0]);
    }

    public async Task OpenProject(string projectPath)
    {
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

        var serializedGroup = new GroupDeserializer()
            .Deserialize(await Vb6TextFile.ReadAllTextAsync(groupPath));

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

        var group = new ProjectGroupDefinition(groupName) { AbsolutePath = groupPath };
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
        var projectDeserializer = new ProjectDeserializer();
        var formDeserializer = new FormDeserializer();
        var errorSink = new DeserializeErrorSink();

        var serializedProject = projectDeserializer.Deserialize(await Vb6TextFile.ReadAllTextAsync(projectPath), errorSink);

        var project = new ProjectDefinition(serializedProject.ProjectType, serializedProject.Name ?? "Project1");
        project.AbsolutePath = projectPath;

        // Pass 1: .ctl files — load UserControls first so FormParts are available when building the registry
        foreach (var (moduleName, modulePath, moduleKind) in serializedProject.RelativeModulePaths)
        {
            if (moduleKind != ModuleKind.UserControl)
                continue;

            try
            {
                var moduleAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, ToLocalRelativePath(modulePath));
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                if (File.Exists(moduleAbsolutePath))
                {
                    var (moduleSource, moduleBytes) = await Vb6TextFile.ReadWithBytesAsync(moduleAbsolutePath);
                    baselineStore.Record(moduleAbsolutePath, moduleBytes);
                    var ctxBlobs = await LoadCompanionBlobs(moduleAbsolutePath, moduleSource);
                    var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink, ctxBlobs);
                    if (formPart != null)
                    {
                        formPart.AbsolutePath = moduleAbsolutePath;
                        module.UpdateFormPart(formPart);
                        // Body only. FormSerializer regenerates the Begin..End header and then appends Code
                        // verbatim, so storing the whole file here emits the header twice on every save.
                        module.UpdateCode(formPart.Code);
                    }
                    else
                    {
                        // Unparseable .ctl: keep it verbatim so a save round-trips it unchanged (SaveModule
                        // falls through to the header-less branch when FormPart is null).
                        module.UpdateCode(moduleSource);
                    }
                }
                project.AddModule(module);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module {ModulePath}", modulePath);
                errorSink.LogError($"Failed to load module {modulePath}: {ex.Message}");
            }
        }

        var userControlRegistry = BuildUserControlRegistry(project);

        // Pass 2: .frm files — load forms using the UC registry
        foreach (var formPath in serializedProject.RelativeFormPaths)
        {
            var formAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, ToLocalRelativePath(formPath));

            try
            {
                if (!File.Exists(formAbsolutePath))
                {
                    errorSink.LogError($"Form file not found: {formPath}");
                    // Preserve the Form= line so the missing node survives an open->save round-trip
                    // (mirrors modules, which are kept even when their file is absent). Not loaded into
                    // the model — a future project-explorer spec may surface it with a "missing" glyph.
                    project.PreservedItemLines.Add($"{SerializedProject.FormKey}={formPath}");
                    continue;
                }

                // Form text before companion: the .frm's cited offsets partition the .frx.
                var (formSource, formBytes) = await Vb6TextFile.ReadWithBytesAsync(formAbsolutePath);
                baselineStore.Record(formAbsolutePath, formBytes);

                var frxBlobs = await LoadCompanionBlobs(formAbsolutePath, formSource);
                var form = formDeserializer.Deserialize(project, formSource, errorSink, frxBlobs, userControlRegistry);
                if (form != null)
                {
                    form.AbsolutePath = formAbsolutePath;
                    project.AddForm(form);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load form {FormPath}", formPath);
                errorSink.LogError($"Failed to load form {formPath}: {ex.Message}");
            }
        }

        // Pass 4: related documents — files the project carries but does not compile.
        //
        // Note what this pass does NOT do: read the file. A related document's content belongs to the
        // editor that opens it, not to project load. Reading here would mean loading every README and
        // spec in a project on open, and — worse — would hand a text buffer to machinery built for VB6
        // source, which is precisely how a non-code file ends up with an Attribute header written into it.
        foreach (var (docName, docPath, originalItemLine) in serializedProject.RelativeRelatedDocPaths)
        {
            try
            {
                var absolute = Path.Join(Path.GetDirectoryName(projectPath)!, ToLocalRelativePath(docPath));
                project.AddRelatedDocument(
                    new RelatedDocumentDefinition(project, docName, absolute, originalItemLine));
            }
            catch (Exception ex)
            {
                // A malformed path costs one entry, never the project load.
                Log.Error(ex, "Failed to load related document {DocPath}", docPath);
            }
        }

        // Pass 3: remaining modules (.bas, .cls, .pag) — UserControls already loaded in pass 1
        foreach (var (moduleName, modulePath, moduleKind) in serializedProject.RelativeModulePaths)
        {
            if (moduleKind == ModuleKind.UserControl)
                continue;

            try
            {
                var moduleAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, ToLocalRelativePath(modulePath));
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                if (File.Exists(moduleAbsolutePath))
                {
                    var (moduleSource, moduleBytes) = await Vb6TextFile.ReadWithBytesAsync(moduleAbsolutePath);
                    baselineStore.Record(moduleAbsolutePath, moduleBytes);
                    // .bas/.cls: strip the VB6 header so Code is the body only (no-op for .pag/.ctl).
                    var (preservedHeader, moduleBody) = ModuleFileFormat.SplitHeader(moduleSource, moduleKind);

                    module.RecordOriginalHeader(preservedHeader);

                    module.UpdateCode(moduleBody);
                    if (moduleKind == ModuleKind.PropertyPage)
                    {
                        var pgxBlobs = await LoadCompanionBlobs(moduleAbsolutePath, moduleSource);
                        var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink, pgxBlobs);
                        if (formPart != null)
                        {
                            formPart.AbsolutePath = moduleAbsolutePath;
                            module.UpdateFormPart(formPart);
                            // StripHeader above is a no-op for .pag, so Code still holds the whole file —
                            // replace it with the body only or the next save writes the header twice.
                            module.UpdateCode(formPart.Code);
                        }
                    }
                }
                project.AddModule(module);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load module {ModulePath}", modulePath);
                errorSink.LogError($"Failed to load module {modulePath}: {ex.Message}");
            }
        }

        // Always retain the startup form NAME so Startup= survives round-trip even when its .frm was
        // missing and no FormDefinition was created (the live StartupForm, if found, takes precedence).
        project.StartupFormName = serializedProject.StartupFormName;
        // `Startup="Sub Main"` names a procedure, not a form — the ordinary shape of a code-only Standard
        // EXE. Read before the form lookup, which would otherwise find nothing and leave the project with
        // no startup at all: the .vbp round-tripped, and F5 refused.
        if (string.Equals(serializedProject.StartupFormName, SerializedProject.SubMainStartup,
                StringComparison.OrdinalIgnoreCase))
            project.StartsAtSubMain = true;
        else if (serializedProject.StartupFormName != null)
            project.StartupForm = project.Forms.FirstOrDefault(x => x.Name == serializedProject.StartupFormName);

        foreach (var reference in serializedProject.References)
            project.AddReference(reference);

        project.UnknownPreSectionLines.AddRange(serializedProject.UnknownPreSectionLines);
        project.PreservedItemLines.AddRange(serializedProject.PreservedItemLines);
        project.ExtensionTail = serializedProject.ExtensionTail;

        projectManager.AddProject(project);
        recentProjects.Add(projectPath);
        await sidecar.LoadAsync(project);

        // Establish the "nothing edited yet" point for the save-changes prompt.
        SnapshotRenderBaselines(project);

        if (serializedProject.SkippedUserDocumentPaths.Count > 0)
        {
            var files = string.Join("\n", serializedProject.SkippedUserDocumentPaths.Select(p => $"  • {p}"));
            await windowManager.MessageBox(
                $"This project contains ActiveX Document files (.dob) which are not supported in HexIDE " +
                $"and have been skipped:\n\n{files}\n\n" +
                $"ActiveX Documents required Internet Explorer or Office binders as a host, " +
                $"both of which are effectively defunct. This project type is out of scope for HexIDE.",
                icon: MessageBoxIcon.Warning);
        }

        if (errorSink.Errors.Count > 0)
        {
            const int maxShown = 10;
            var shown = errorSink.Errors.Take(maxShown).ToList();
            var message = "Couldn't properly deserialize the project:\n" + string.Join("\n", shown);
            if (errorSink.Errors.Count > maxShown)
                message += $"\n\n...and {errorSink.Errors.Count - maxShown} more warnings (see log for full list).";
            await windowManager.MessageBox(message, icon: MessageBoxIcon.Warning);
        }
    }

    private static IReadOnlyDictionary<string, ComponentBaseClass> BuildUserControlRegistry(
        ProjectDefinition project)
    {
        var projectName = project.Name;
        var dict = new Dictionary<string, ComponentBaseClass>(StringComparer.OrdinalIgnoreCase);
        foreach (var module in project.Modules)
        {
            if (module.Kind != ModuleKind.UserControl)
                continue;
            var typeName = $"{projectName}.{module.Name}";
            if (module.FormPart != null)
                dict[typeName] = UserControlComponentClass.ForModule(typeName, module.FormPart, dict);
            else
                dict[typeName] = PlaceholderComponentClass.ForType(typeName);
        }
        return dict;
    }

    private class DeserializeErrorSink : IDeserializeErrorSink
    {
        private List<string> errors = new();

        public void LogError(string error)
        {
            Log.Warning("[Deserialize] {Error}", error);
            errors.Add(error);
        }

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
    /// Save one form on its own. A refusal is reported at the moment it happens — unlike
    /// <see cref="SaveFormCore"/>, which the project-wide batch uses so that N refusals produce one dialog
    /// rather than N.
    /// </summary>
    public async Task<bool> SaveForm(FormDefinition form, bool saveAs)
    {
        var written = await SaveFormCore(form, saveAs);
        // Without this a lone refusal did not merely go unreported: the entry SURVIVED in refusedThisSave
        // and surfaced during the next unrelated Save Project, naming a file that was not in that batch.
        await ReportRefusedSaves();
        // Returned so a non-interactive caller can tell a refusal from a save. The MCP write tools reported
        // success unconditionally, which told an agent a file had been written when it had not — and an
        // agent, unlike a developer, has no dialog to read and will build on the answer.
        return written;
    }

    /// <summary>Returns false when the form was refused and nothing was written.</summary>
    private async Task<bool> SaveFormCore(FormDefinition form, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

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
        if (!form.CanSaveFaithfully)
        {
            Log.Warning("Refusing to save {Form}: {Reason}", form.Name, form.UnfaithfulSaveReason);
            RecordRefusal(form.Name);
            return false;
        }

        var formPath = form.AbsolutePath;
        if (formPath == null || saveAs)
        {
            formPath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                DefaultExtension = "frm",
                SuggestedFileName = form.Name + ".frm",
                FileTypeChoices = [new("Form Files") { Patterns = ["*.frm"] }, new("All Files") { Patterns = ["*.*"] }]
            });
            if (formPath == null)
                throw new OperationCanceledException();
        }
        // The return is deliberately not banked here. The refusal above already covers this path — and it
        // sits above the picker on purpose, so the developer is never asked for a destination that will not
        // be written. The check inside SerializeFormToFile is the write-site backstop for the paths that do
        // NOT come through here (Save Project's batch, Make EXE, Save-to-directory). Two checks, two jobs;
        // collapsing them into one would either reinstate the picker prompt or leave those paths ungated.
        return SerializeFormToFile(form, formPath);
    }

    public async Task<bool> ReloadFormFromDisk(FormDefinition form)
    {
        var path = form.AbsolutePath;
        if (path is null || !File.Exists(path))
            return false;

        var project = form.Owner;
        var formDeserializer = new FormDeserializer();
        var errorSink = new DeserializeErrorSink();

        // The designer file is read FIRST: its cited offsets are what partition the companion, so the
        // companion cannot be parsed without it. See FrxDeserializer.
        var (source, sourceBytes) = await Vb6TextFile.ReadWithBytesAsync(path);
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

        var (source, sourceBytes) = await Vb6TextFile.ReadWithBytesAsync(path);
        baselineStore.Record(path, sourceBytes);
        var (preservedHeader, reloadedBody) = ModuleFileFormat.SplitHeader(source, module.Kind);

        module.RecordOriginalHeader(preservedHeader);

        module.UpdateCode(reloadedBody);

        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var formDeserializer = new FormDeserializer();
            var errorSink = new DeserializeErrorSink();
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
        if (projectManager.StartupProject == null)
        {
            await windowManager.MessageBox("No startup project found.", icon: MessageBoxIcon.Information);
            return;
        }

        await MakeProject(projectManager.StartupProject);
    }

    public async Task EditProjectProperties(ProjectDefinition project)
    {
        var vm = new ProjectPropertiesViewModel(project);
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
        var vm = new ComponentsViewModel(project);
        await windowManager.ShowDialog(vm);
    }

    public async Task MakeProject(ProjectDefinition projectDefinition)
    {
        try
        {
            await MakeProjectInternal(projectDefinition);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            await windowManager.MessageBox("Fatal error while making the project:\n" + e.Message, icon: MessageBoxIcon.Information);
            throw;
        }
    }

    private async Task MakeProjectInternal(ProjectDefinition projectDefinition)
    {
        if (OperatingSystem.IsBrowser())
        {
            await windowManager.MessageBox("Can't make project in a browser!", icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        var exePath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
        {
            DefaultExtension = OperatingSystem.IsWindows() ? "exe" : "",
            SuggestedFileName = projectDefinition.Name + (OperatingSystem.IsWindows() ? ".exe" : ""),
            FileTypeChoices = OperatingSystem.IsWindows() ? [new("Windows EXE") { Patterns = ["*.exe"] }] : [new("All Files") { Patterns = ["*.*"] }]
        });

        if (exePath == null)
            throw new OperationCanceledException();

        List<FileInfo[]> requiredNativeFiles;

        if (OperatingSystem.IsWindows())
        {
            requiredNativeFiles =
            [
                [new FileInfo("standalone/av_libglesv2.dll"), new FileInfo("av_libglesv2.dll")],
                [new FileInfo("standalone/HexIDE.Standalone.exe")],
                [new FileInfo("standalone/libHarfBuzzSharp.dll"), new FileInfo("libHarfBuzzSharp.dll")],
                [new FileInfo("standalone/libSkiaSharp.dll"), new FileInfo("libSkiaSharp.dll")]
            ];
        }
        else if (OperatingSystem.IsMacOS())
        {
            requiredNativeFiles =
            [
                [new FileInfo("standalone/libAvaloniaNative.dylib"), new FileInfo("libAvaloniaNative.dylib")],
                [new FileInfo("standalone/HexIDE.Standalone")],
                [new FileInfo("standalone/libHarfBuzzSharp.dylib"), new FileInfo("libHarfBuzzSharp.dylib")],
                [new FileInfo("standalone/libSkiaSharp.dylib"), new FileInfo("libSkiaSharp.dylib")]
            ];
        }
        else if (OperatingSystem.IsLinux())
        {
            requiredNativeFiles =
            [
                [new FileInfo("standalone/HexIDE.Standalone")],
                [new FileInfo("standalone/libHarfBuzzSharp.so"), new FileInfo("libHarfBuzzSharp.so")],
                [new FileInfo("standalone/libSkiaSharp.so"), new FileInfo("libSkiaSharp.so")]
            ];
        }
        else
        {
            await windowManager.MessageBox("Your OS is not supported yet, but it can be, search in the code for this message to find how to add support for this platform!", icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        if (requiredNativeFiles.Any(files => files.All(f => !f.Exists)))
        {
            await windowManager.MessageBox("To Make Project, you need to build standalone runtime first. See the readme for help.", icon: MessageBoxIcon.Information);
            throw new OperationCanceledException();
        }

        var exeDir = Path.GetDirectoryName(exePath);
        var exeFileName = Path.GetFileNameWithoutExtension(exePath);

        var tempPath = Path.GetTempFileName();
        File.Delete(tempPath);
        Directory.CreateDirectory(tempPath);
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

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
                if (!SerializeFormToFile(form, Path.Join(tempPath, Path.ChangeExtension(form.Name, "frm"))))
                    refused.Add(form.Name);

            // Modules too. SaveModuleCore rather than SaveModule, because this is a batch: a refusal is
            // banked and reported once below, with the forms', rather than one dialog per module.
            //
            // The path is assigned first so the picker is never reached — a Make must not stop to ask where
            // to put a file, and a module that has never been saved still belongs in the package.
            foreach (var module in projectDefinition.Modules)
            {
                module.AbsolutePath = Path.Join(tempPath, module.Name + "." + ModuleExtension(module.Kind));
                if (!await SaveModuleCore(module, saveAs: false))
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

            SerializeOnlyProjectToFile(projectDefinition, Path.Join(tempPath, Path.ChangeExtension(exeFileName, "vbp")));

            // This is not a .dll file, but it will look better :wink: :wink:
            ZipFile.CreateFromDirectory(tempPath, Path.ChangeExtension(exePath, "dll")!);
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

        foreach (var standaloneFile in requiredNativeFiles.Select(f => f.FirstOrDefault(x => x.Exists)))
        {
            if (standaloneFile == null)
                throw new Exception($"Required files doesn't exist, even tho it existed few lines above");
            var fileName = standaloneFile.Name;
            if (fileName.StartsWith("HexIDE.Standalone"))
                fileName = Path.GetFileName(exePath);
            standaloneFile.CopyTo(Path.Join(exeDir, fileName), true);
        }
    }

    public async Task SaveProject(ProjectDefinition project, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        // try/finally so the drain runs even when the batch is abandoned part-way. SaveOnlyProject and
        // SaveModule both throw OperationCanceledException when their picker is dismissed, which skipped the
        // report and left every refusal banked — to surface during the next unrelated save, naming files
        // that were not in it. That is the leak #145 fixed for the lone path and left open here.
        try
        {
            foreach (var form in project.Forms)
            {
                await SaveFormCore(form, false);
            }
            foreach (var module in project.Modules)
            {
                await SaveModuleCore(module, false);
            }

            await SaveOnlyProject(project, saveAs);
        }
        finally
        {
            await ReportRefusedSaves();
        }
    }

    public async Task SaveProjectToDirectory(ProjectDefinition project, string directory)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        Directory.CreateDirectory(directory);

        // Reported here rather than banked. This method has no drain of its own, so a refusal added to the
        // shared list would sit there and surface during the next unrelated Save Project, naming a file
        // that was not part of it. Throwing keeps the report attached to the operation that caused it, and
        // stops a .vbp being written that names forms which are not beside it. (#148)
        var refused = new List<string>();
        foreach (var form in project.Forms)
            if (!SerializeFormToFile(form, Path.Join(directory, form.Name + ".frm")))
                refused.Add(form.Name);

        // Modules as well. Writing the forms and the .vbp but not the modules produced a directory whose
        // project file named .bas, .cls and .ctl files that were not in it — and, because every item is
        // recorded by AbsolutePath, named them by a path back to wherever they had been. (#149)
        //
        // The repointing here is permanent, unlike Make's: this is "write the project to this directory",
        // so the project genuinely lives there afterwards, which is already how the forms above behave.
        foreach (var module in project.Modules)
        {
            module.AbsolutePath = Path.Join(directory, module.Name + "." + ModuleExtension(module.Kind));
            if (!await SaveModuleCore(module, saveAs: false))
                refused.Add(module.Name);
        }

        if (refused.Count > 0)
            throw new InvalidOperationException(
                $"Cannot write this project to {directory}: HexIDE cannot reproduce "
              + $"{string.Join(", ", refused)}, so no project file was written.");

        SerializeOnlyProjectToFile(project, Path.Join(directory, project.Name + ".vbp"));
    }

    public Task<FormDefinition> AddNewForm(ProjectDefinition project, string name)
    {
        var form = new FormDefinition(project, FormComponentClass.Instance, name);
        var dir = ProjectFilesDirectory(project);
        Directory.CreateDirectory(dir);
        // A brand new form has no unfaithful causes and cites no companion, so this cannot refuse. Asserted
        // rather than ignored: if it ever did, the form would be added to the project with no file behind
        // it, and the .vbp would name a path that does not exist.
        if (!SerializeFormToFile(form, Path.Join(dir, name + ".frm")))
            throw new InvalidOperationException($"A new form ({name}) could not be written.");
        project.AddForm(form);
        return Task.FromResult(form);
    }

    public Task<ModuleDefinition> AddNewModule(ProjectDefinition project, string name, ModuleKind kind)
    {
        var module = new ModuleDefinition(project, name, kind);
        var ext = kind == ModuleKind.ClassModule ? "cls" : "bas";
        var dir = ProjectFilesDirectory(project);
        Directory.CreateDirectory(dir);
        module.AbsolutePath = Path.Join(dir, name + "." + ext);
        // Write the VB6 file header + (empty) body; the editor still shows only the body.
        var diskContent = ModuleFileFormat.ToFileContent(module.Code, name, kind, module.OriginalHeader);
        Vb6TextFile.WriteAllText(module.AbsolutePath, diskContent);
        baselineStore.Record(module.AbsolutePath, Vb6TextFile.Encode(diskContent));
        project.AddModule(module);
        return Task.FromResult(module);
    }

    public Task<ModuleDefinition> AddNewUserControl(ProjectDefinition project, string name)
    {
        var module = new ModuleDefinition(project, name, ModuleKind.UserControl);
        var formPart = new FormDefinition(project, FormComponentClass.Instance, name);
        formPart.UpdateRootTypeName("VB.UserControl");
        module.UpdateFormPart(formPart);
        var dir = ProjectFilesDirectory(project);
        Directory.CreateDirectory(dir);
        module.AbsolutePath = Path.Join(dir, name + ".ctl");
        var serializer = new FormSerializer();
        var (ctl, _) = serializer.Serialize(formPart, module.Code, name + ".ctl");
        Vb6TextFile.WriteAllText(module.AbsolutePath, ctl);
        baselineStore.Record(module.AbsolutePath, Vb6TextFile.Encode(ctl));
        project.AddModule(module);
        return Task.FromResult(module);
    }

    public Task<ModuleDefinition> AddNewPropertyPage(ProjectDefinition project, string name)
    {
        var module = new ModuleDefinition(project, name, ModuleKind.PropertyPage);
        var formPart = new FormDefinition(project, FormComponentClass.Instance, name);
        formPart.UpdateRootTypeName("VB.PropertyPage");
        module.UpdateFormPart(formPart);
        var dir = ProjectFilesDirectory(project);
        Directory.CreateDirectory(dir);
        module.AbsolutePath = Path.Join(dir, name + ".pag");
        var serializer = new FormSerializer();
        var (pag, _) = serializer.Serialize(formPart, module.Code, name + ".pag");
        Vb6TextFile.WriteAllText(module.AbsolutePath, pag);
        baselineStore.Record(module.AbsolutePath, Vb6TextFile.Encode(pag));
        project.AddModule(module);
        return Task.FromResult(module);
    }

    private static string ProjectFilesDirectory(ProjectDefinition project) =>
        project.AbsolutePath is { } p
            ? Path.GetDirectoryName(p)!
            : Path.Combine(Path.GetTempPath(), "hexide_" + project.Name);

    /// <summary>Saves everything the user left ticked in the save-changes prompt.</summary>
    private async Task SaveSelected(SaveChangesViewModel changedFilesVm)
    {
        // try/finally for the same reason as SaveProject: a dismissed picker anywhere in this batch used to
        // skip the drain and leave refusals banked for the next unrelated save.
        try
        {
            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Form != null))
                await SaveFormCore(selected.Form!, false);

            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Module != null))
                await SaveModuleCore(selected.Module!, false);

            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Project != null))
                await SaveOnlyProject(selected.Project!, false);
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
    private async Task ReportRefusedSaves()
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

        await windowManager.MessageBox(
            string.Format(body, names),
            "HexIDE", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    private async Task SaveOnlyProject(ProjectDefinition project, bool saveAs)
    {
        var projectPath = project.AbsolutePath;
        if (projectPath == null || saveAs)
        {
            projectPath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                DefaultExtension = "vbp",
                SuggestedFileName = project.Name + ".vbp",
                FileTypeChoices = [new("Project Files") { Patterns = ["*.vbp"] }, new("All Files") { Patterns = ["*.*"] }]
            });
            if (projectPath == null)
                throw new OperationCanceledException();
        }
        SerializeOnlyProjectToFile(project, projectPath);
        recentProjects.Add(projectPath);
        await sidecar.SaveAsync(project);
    }

    public async Task MakeProjectGroup()
    {
        if (projectManager.LoadedProjects.Count == 0)
            return;
        EnsureGroupIfMultiple();
        foreach (var project in projectManager.LoadedProjects)
            await SaveProject(project, false);
        await SaveGroupFile(true);
    }

    public async Task SaveAllProjects(bool saveAs)
    {
        foreach (var project in projectManager.LoadedProjects)
        {
            await SaveProject(project, saveAs);
        }

        if (projectManager.CurrentGroup != null)
            await SaveGroupFile(saveAs);
    }

    private async Task SaveGroupFile(bool saveAs)
    {
        var group = projectManager.CurrentGroup!;
        if (saveAs || group.AbsolutePath == null)
        {
            var path = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save Project Group As",
                DefaultExtension = "vbg",
                FileTypeChoices = [new FilePickerFileType("Project Group") { Patterns = ["*.vbg"] }],
                SuggestedFileName = group.Name
            });
            if (path == null) throw new OperationCanceledException();
            group.AbsolutePath = path;
            group.Name = Path.GetFileNameWithoutExtension(path);
            recentProjects.Add(path);
        }
        var content = new GroupSerializer().Serialize(
            group.AbsolutePath!, projectManager.LoadedProjects, projectManager.StartupProject,
            group.UnknownLines);
        AtomicWriteText(group.AbsolutePath!, content);
        Log.Information("ProjectService: Saved group {Path}", group.AbsolutePath);
    }

    /// <summary>
    /// Write a form's two halves — the designer text and its companion binary — or write neither.
    ///
    /// Returns false when the form was refused, in which case nothing was written and nothing on the model
    /// was repointed. Reporting is the caller's: a lone save says so immediately, a batch collects.
    /// </summary>
    private bool SerializeFormToFile(FormDefinition form, string formPath)
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

        var serializer = new FormSerializer();
        var (frmText, frxContent) = serializer.Serialize(form, Path.GetFileName(formPath));

        // Below the decision. A refused Save As must not leave the model pointing at a path that was never
        // written — the .vbp records forms by AbsolutePath, so it would name a file that does not exist.
        form.AbsolutePath = formPath;

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
        AtomicWriteText(formPath, frmText);
        return true;
    }

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
        Path.GetExtension(sourceFilePath).ToLowerInvariant() switch
        {
            ".ctl" => ".ctx",
            ".pag" => ".pgx",
            _ => ".frx"
        };

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
    private void AtomicWriteText(string targetPath, string content)
    {
        var tmp = targetPath + ".tmp";
        try
        {
            // Encode once and record the SAME bytes. FileHasher.Hash(string) hashes the UTF-8 encoding,
            // which stopped matching the file the moment VB6 source began being written as ANSI — the
            // watcher would re-hash from disk, see a different hash, and report every save as an external
            // change. Recording the actual bytes keeps self-write suppression correct.
            var bytes = Vb6TextFile.Encode(content);
            File.WriteAllBytes(tmp, bytes);
            File.Move(tmp, targetPath, overwrite: true);
            baselineStore.Record(targetPath, bytes);
            // Render baselines are only ever compared against other renders, so a string hash is fine.
            renderBaselines[targetPath] = FileHasher.Hash(content);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    private void AtomicWriteBytes(string targetPath, byte[] content)
    {
        var tmp = targetPath + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, content);
            File.Move(tmp, targetPath, overwrite: true);
            baselineStore.Record(targetPath, content);
            renderBaselines[targetPath] = FileHasher.Hash(content);
        }
        catch
        {
            if (File.Exists(tmp)) File.Delete(tmp);
            throw;
        }
    }

    /// <summary>The file extension VB6 gives a module of this kind.</summary>
    private static string ModuleExtension(ModuleKind kind) => kind switch
    {
        ModuleKind.ClassModule  => "cls",
        ModuleKind.UserControl  => "ctl",
        ModuleKind.PropertyPage => "pag",
        _                       => "bas"
    };

    /// <summary>
    /// Save one module on its own. A refusal is reported at the moment it happens — mirroring
    /// <see cref="SaveForm"/>, and for the same reason: without a drain the entry survives in
    /// <c>refusedThisSave</c> and surfaces during the next unrelated save, naming a file that was not in
    /// that batch. Returns false when the module was refused and nothing was written.
    /// </summary>
    public async Task<bool> SaveModule(ModuleDefinition module, bool saveAs)
    {
        var written = await SaveModuleCore(module, saveAs);
        await ReportRefusedSaves();
        return written;
    }

    /// <summary>
    /// The batch-safe half: refusals are banked, not reported, so N of them produce one dialog rather
    /// than N. Callers that are not part of a batch use <see cref="SaveModule"/>.
    /// </summary>
    private async Task<bool> SaveModuleCore(ModuleDefinition module, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // ABOVE the picker, for the reason SaveFormCore gives: the developer must not be asked for a
        // destination that will never be written. This used to sit below it, so Save As on an unfaithful
        // UserControl asked where to put the file and then declined to write it — which is the behaviour
        // #145 removed for forms and left in place here, and which the merged requirement rules out in
        // terms ("no destination is asked for"). Nothing in the predicate depends on the picker's result,
        // so hoisting it is a pure statement move. (#147)
        //
        // A brand-new module is unaffected: it has no designer half yet, or a fresh one with no unfaithful
        // causes, so the gate does not fire before its first save.
        var designerPart = module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage
            ? module.FormPart
            : null;

        if (designerPart is not null && !designerPart.CanSaveFaithfully)
        {
            Log.Warning("Refusing to write {Module}: {Reason}", module.Name, designerPart.UnfaithfulSaveReason);
            RecordRefusal(module.Name);
            return false;
        }

        var modulePath = module.AbsolutePath;
        if (modulePath == null || saveAs)
        {
            var ext = ModuleExtension(module.Kind);
            modulePath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                DefaultExtension = ext,
                SuggestedFileName = module.Name + "." + ext,
                FileTypeChoices = [new("Module Files") { Patterns = [$"*.{ext}"] }, new("All Files") { Patterns = ["*.*"] }]
            });
            if (modulePath == null)
                throw new OperationCanceledException();
        }
        // Repointed only now the destination is known and the write is going to happen. (#148)
        module.AbsolutePath = modulePath;

        if (designerPart is not null)
        {
            var serializer = new FormSerializer();
            var fileName = Path.GetFileName(modulePath);
            var (text, binary) = serializer.Serialize(designerPart, module.Code, fileName);
            // Companion first — see SerializeFormToFile for why the order is the difference between a
            // crash leaving a laundered file and a crash leaving a refused one.
            WriteCompanionBinary(modulePath, binary, designerPart);
            AtomicWriteText(modulePath, text);
            return true;
        }

        // .bas/.cls: prepend the canonical VB6 header (ModuleFileFormat) so vb6.exe can load it; Code itself
        // is body-only. (Unmanaged kinds return Code unchanged.)
        AtomicWriteText(modulePath, ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader));
        return true;
    }

    private void SerializeOnlyProjectToFile(ProjectDefinition definition, string projectPath)
    {
        definition.AbsolutePath = projectPath;

        var serializer = new ProjectSerializer();
        var serialized = serializer.Serialize(definition, projectPath);

        AtomicWriteText(projectPath, serialized);
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

        return !BaselineMatches(path, new ProjectSerializer().Serialize(project, path));
    }

    public bool HasUnsavedChanges(FormDefinition form) => IsDirty(form);

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
