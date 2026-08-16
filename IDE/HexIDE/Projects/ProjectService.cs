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
            var absPath = Path.GetFullPath(Path.Combine(groupDir, relPath));
            await LoadProjectFromDisk(absPath);
        }

        if (serializedGroup.StartupProjectRelativePath != null)
        {
            var startupAbs = Path.GetFullPath(
                Path.Combine(groupDir, serializedGroup.StartupProjectRelativePath));
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
    internal static string ToLocalRelativePath(string relativePath) =>
        relativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

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
                    var ctxBlobs = await LoadCompanionBlobs(moduleAbsolutePath);
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

                var frxBlobs = await LoadCompanionBlobs(formAbsolutePath);

                var (formSource, formBytes) = await Vb6TextFile.ReadWithBytesAsync(formAbsolutePath);
                baselineStore.Record(formAbsolutePath, formBytes);
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
                        var pgxBlobs = await LoadCompanionBlobs(moduleAbsolutePath);
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
        if (serializedProject.StartupFormName != null)
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
    /// Forms refused during the current save, so a batch reports once rather than per file.
    /// </summary>
    private readonly List<FormDefinition> refusedThisSave = new();

    public async Task SaveForm(FormDefinition form, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());

        // Writing this form would not reproduce it, and VB6 rejects the result outright for any menu
        // carrying a shortcut or a separator. Refusing is the honest move: the original stays intact and
        // the developer finds out now rather than the next time they open the project in VB6.
        if (!form.CanSaveFaithfully && !saveAs)
        {
            Log.Warning("Refusing to save {Form}: {Reason}", form.Name, form.UnfaithfulSaveReason);
            if (!refusedThisSave.Contains(form))
                refusedThisSave.Add(form);
            return;
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
        SerializeFormToFile(form, formPath);
    }

    public async Task<bool> ReloadFormFromDisk(FormDefinition form)
    {
        var path = form.AbsolutePath;
        if (path is null || !File.Exists(path))
            return false;

        var project = form.Owner;
        var formDeserializer = new FormDeserializer();
        var errorSink = new DeserializeErrorSink();

        IReadOnlyDictionary<int, byte[]>? frxBlobs = null;
        var frxPath = Path.ChangeExtension(path, ".frx");
        if (File.Exists(frxPath))
        {
            var frxBytes = await File.ReadAllBytesAsync(frxPath);
            baselineStore.Record(frxPath, frxBytes);
            frxBlobs = FrxDeserializer.Read(frxBytes);
        }

        var (source, sourceBytes) = await Vb6TextFile.ReadWithBytesAsync(path);
        baselineStore.Record(path, sourceBytes);

        var fresh = formDeserializer.Deserialize(project, source, errorSink, frxBlobs, BuildUserControlRegistry(project));
        if (fresh is null)
            return false;

        form.UpdateCode(fresh.Code);
        form.UpdateComponents(fresh.Components);
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
            var blobs = await LoadCompanionBlobs(path);
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

        foreach (var form in projectDefinition.Forms)
            SerializeFormToFile(form, Path.Join(tempPath, Path.ChangeExtension(form.Name, "frm")));
        SerializeOnlyProjectToFile(projectDefinition, Path.Join(tempPath, Path.ChangeExtension(exeFileName, "vbp")));

        // vvv this is a bad design. TODO a better way to handle paths
        projectDefinition.AbsolutePath = originalAbsolutePath;
        foreach (var (form, original) in originalFormPaths)
            form.AbsolutePath = original;

        // This is not a .dll file, but it will look better :wink: :wink:
        ZipFile.CreateFromDirectory(tempPath, Path.ChangeExtension(exePath, "dll")!);
        Directory.Delete(tempPath, true);

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
        foreach (var form in project.Forms)
        {
            await SaveForm(form, false);
        }
        foreach (var module in project.Modules)
        {
            await SaveModule(module, false);
        }

        await SaveOnlyProject(project, saveAs);
        await ReportRefusedSaves();
    }

    public Task SaveProjectToDirectory(ProjectDefinition project, string directory)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        Directory.CreateDirectory(directory);
        foreach (var form in project.Forms)
            SerializeFormToFile(form, Path.Join(directory, form.Name + ".frm"));
        SerializeOnlyProjectToFile(project, Path.Join(directory, project.Name + ".vbp"));
        return Task.CompletedTask;
    }

    public Task<FormDefinition> AddNewForm(ProjectDefinition project, string name)
    {
        var form = new FormDefinition(project, FormComponentClass.Instance, name);
        var dir = ProjectFilesDirectory(project);
        Directory.CreateDirectory(dir);
        SerializeFormToFile(form, Path.Join(dir, name + ".frm"));
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
        foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Form != null))
            await SaveForm(selected.Form!, false);

        foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Module != null))
            await SaveModule(selected.Module!, false);

        foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Project != null))
            await SaveOnlyProject(selected.Project!, false);

        await ReportRefusedSaves();
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

        var names = string.Join("\n", refusedThisSave.Select(f => $"  • {f.Name}"));
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

    private void SerializeFormToFile(FormDefinition form, string formPath)
    {
        form.AbsolutePath = formPath;
        var serializer = new FormSerializer();
        var (frmText, frxContent) = serializer.Serialize(form, Path.GetFileName(formPath));

        AtomicWriteText(formPath, frmText);
        WriteCompanionBinary(formPath, frxContent, WouldLoseBlobs(form, frxContent));
    }

    /// <summary>
    /// True when saving would write back fewer blobs than the load read, on any path — flagged or not.
    /// The explicit flag catches the two known drop sites; this catches the rest, including properties
    /// that are named but whose CLR type is unmapped and which vanish with no diagnostic at all.
    /// </summary>
    private static bool WouldLoseBlobs(FormDefinition form, byte[]? produced)
    {
        if (form.HasUnmodelledBinaryProperties)
            return true;
        if (form.LoadedCompanionBlobCount == 0)
            return false;

        var producedCount = 0;
        if (produced is { Length: > 0 })
        {
            try { producedCount = FrxDeserializer.Read(produced).Count; }
            catch { return true; } // cannot even read back what we just wrote — do not risk the original
        }
        return producedCount < form.LoadedCompanionBlobCount;
    }

    /// <summary>
    /// Write, replace, or remove a companion binary (.frx/.ctx/.pgx) beside <paramref name="sourcePath"/> —
    /// unless loading dropped a blob-backed property we cannot reproduce, in which case the file on disk
    /// holds the only copy of those bytes and is left exactly as it is.
    ///
    /// Both alternatives destroy user data: regenerating truncates (Splash Screen.frx, 790 bytes to 12) and
    /// producing nothing is read as "delete it" (Button ListBox.frx, 2122 bytes gone). The images exist
    /// nowhere else, so refusing to touch the file is the only safe option until unmodelled blobs are
    /// passed through. Verified against VB6's own shipped forms — see SerializationCorpusTests.
    /// </summary>
    private void WriteCompanionBinary(string sourcePath, byte[]? content, bool binaryFidelityLost)
    {
        var companionPath = Path.ChangeExtension(sourcePath, CompanionBinaryExtension(sourcePath));

        if (binaryFidelityLost && File.Exists(companionPath))
        {
            Log.Warning("Leaving {Companion} untouched — {Source} uses binary properties HexIDE does not "
                      + "model, so writing a regenerated companion would lose data.",
                        Path.GetFileName(companionPath), Path.GetFileName(sourcePath));
            return;
        }

        if (content is { Length: > 0 })
        {
            AtomicWriteBytes(companionPath, content);
        }
        else if (File.Exists(companionPath))
        {
            File.Delete(companionPath);
            baselineStore.Remove(companionPath);
            renderBaselines.Remove(companionPath);
        }
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
    private async Task<IReadOnlyDictionary<int, byte[]>?> LoadCompanionBlobs(string sourceFilePath)
    {
        var companionPath = Path.ChangeExtension(sourceFilePath, CompanionBinaryExtension(sourceFilePath));
        if (!File.Exists(companionPath))
            return null;

        var bytes = await File.ReadAllBytesAsync(companionPath);
        baselineStore.Record(companionPath, bytes);
        return FrxDeserializer.Read(bytes);
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

    public async Task SaveModule(ModuleDefinition module, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
        var modulePath = module.AbsolutePath;
        if (modulePath == null || saveAs)
        {
            var ext = module.Kind switch
            {
                ModuleKind.ClassModule  => "cls",
                ModuleKind.UserControl  => "ctl",
                ModuleKind.PropertyPage => "pag",
                _                       => "bas"
            };
            modulePath = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions()
            {
                DefaultExtension = ext,
                SuggestedFileName = module.Name + "." + ext,
                FileTypeChoices = [new("Module Files") { Patterns = [$"*.{ext}"] }, new("All Files") { Patterns = ["*.*"] }]
            });
            if (modulePath == null)
                throw new OperationCanceledException();
        }
        module.AbsolutePath = modulePath;

        if (module.FormPart is not null && module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var serializer = new FormSerializer();
            var fileName = Path.GetFileName(modulePath);
            var (text, binary) = serializer.Serialize(module.FormPart, module.Code, fileName);
            AtomicWriteText(modulePath, text);
            WriteCompanionBinary(modulePath, binary, WouldLoseBlobs(module.FormPart, binary));
            return;
        }

        // .bas/.cls: prepend the canonical VB6 header (ModuleFileFormat) so vb6.exe can load it; Code itself
        // is body-only. (Unmanaged kinds return Code unchanged.)
        AtomicWriteText(modulePath, ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind, module.OriginalHeader));
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
