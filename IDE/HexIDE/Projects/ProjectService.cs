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

    public ProjectService(Func<NewProjectViewModel> newProjectVm,
        IWindowManager windowManager,
        IEventBus eventBus,
        IProjectManager projectManager,
        IRecentProjectsService recentProjects,
        IReferenceLibraryService referenceLibraryService,
        IUserSidecarService sidecar,
        IFileBaselineStore baselineStore)
    {
        this.newProjectVm = newProjectVm;
        this.windowManager = windowManager;
        this.eventBus = eventBus;
        this.projectManager = projectManager;
        this.recentProjects = recentProjects;
        this.referenceLibraryService = referenceLibraryService;
        this.sidecar = sidecar;
        this.baselineStore = baselineStore;
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

        var changedFilesVm = new SaveChangesViewModel();
        foreach (var loadedProject in projectManager.LoadedProjects)
        {
            changedFilesVm.Add(loadedProject);
            foreach (var form in loadedProject.Forms)
                changedFilesVm.Add(form);
        }
        changedFilesVm.SelectedFiles.AddRange(changedFilesVm.ChangedFiles);
        if (!await windowManager.ShowDialog(changedFilesVm))
            throw new OperationCanceledException();

        if (changedFilesVm.SaveChanges)
        {
            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Form != null))
                await SaveForm(selected.Form!, false);

            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Project != null))
                await SaveOnlyProject(selected.Project!, false);
        }

        projectManager.UnloadAllProjects();
    }

    public async Task UnloadProject(ProjectDefinition project)
    {
        var changedFilesVm = new SaveChangesViewModel();
        changedFilesVm.Add(project);
        foreach (var form in project.Forms)
            changedFilesVm.Add(form);

        changedFilesVm.SelectedFiles.AddRange(changedFilesVm.ChangedFiles);
        if (!await windowManager.ShowDialog(changedFilesVm))
            throw new OperationCanceledException();

        if (changedFilesVm.SaveChanges)
        {
            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Form != null))
                await SaveForm(selected.Form!, false);

            foreach (var selected in changedFilesVm.SelectedFiles.Where(f => f.Project != null))
                await SaveOnlyProject(selected.Project!, false);
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
            .Deserialize(await File.ReadAllTextAsync(groupPath));

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

        var serializedProject = projectDeserializer.Deserialize(await File.ReadAllTextAsync(projectPath), errorSink);

        var project = new ProjectDefinition(serializedProject.ProjectType, serializedProject.Name ?? "Project1");
        project.AbsolutePath = projectPath;

        // Pass 1: .ctl files — load UserControls first so FormParts are available when building the registry
        foreach (var (moduleName, modulePath, moduleKind) in serializedProject.RelativeModulePaths)
        {
            if (moduleKind != ModuleKind.UserControl)
                continue;

            try
            {
                var moduleAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, modulePath);
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                if (File.Exists(moduleAbsolutePath))
                {
                    var moduleSource = await File.ReadAllTextAsync(moduleAbsolutePath);
                    baselineStore.Record(moduleAbsolutePath, moduleSource);
                    module.UpdateCode(moduleSource);
                    var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink);
                    if (formPart != null)
                    {
                        formPart.AbsolutePath = moduleAbsolutePath;
                        module.UpdateFormPart(formPart);
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
            var formAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, formPath);

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

                // Load companion .frx binary resource file if it exists
                IReadOnlyDictionary<int, byte[]>? frxBlobs = null;
                var frxPath = Path.ChangeExtension(formAbsolutePath, ".frx");
                if (File.Exists(frxPath))
                {
                    var frxBytes = await File.ReadAllBytesAsync(frxPath);
                    baselineStore.Record(frxPath, frxBytes);
                    frxBlobs = FrxDeserializer.Read(frxBytes);
                }

                var formSource = await File.ReadAllTextAsync(formAbsolutePath);
                baselineStore.Record(formAbsolutePath, formSource);
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
                var moduleAbsolutePath = Path.Join(Path.GetDirectoryName(projectPath)!, modulePath);
                var module = new ModuleDefinition(project, moduleName, moduleKind);
                module.AbsolutePath = moduleAbsolutePath;
                if (File.Exists(moduleAbsolutePath))
                {
                    var moduleSource = await File.ReadAllTextAsync(moduleAbsolutePath);
                    baselineStore.Record(moduleAbsolutePath, moduleSource);
                    // .bas/.cls: strip the VB6 header so Code is the body only (no-op for .pag/.ctl).
                    module.UpdateCode(ModuleFileFormat.StripHeader(moduleSource, moduleKind));
                    if (moduleKind == ModuleKind.PropertyPage)
                    {
                        var formPart = formDeserializer.Deserialize(project, moduleSource, errorSink);
                        if (formPart != null)
                        {
                            formPart.AbsolutePath = moduleAbsolutePath;
                            module.UpdateFormPart(formPart);
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

    public async Task SaveForm(FormDefinition form, bool saveAs)
    {
        eventBus.Publish(new ApplyAllUnsavedChangesEvent());
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

        var source = await File.ReadAllTextAsync(path);
        baselineStore.Record(path, source);

        var fresh = formDeserializer.Deserialize(project, source, errorSink, frxBlobs, BuildUserControlRegistry(project));
        if (fresh is null)
            return false;

        form.UpdateCode(fresh.Code);
        form.UpdateComponents(fresh.Components);
        return true;
    }

    public async Task<bool> ReloadModuleFromDisk(ModuleDefinition module)
    {
        var path = module.AbsolutePath;
        if (path is null || !File.Exists(path))
            return false;

        var source = await File.ReadAllTextAsync(path);
        baselineStore.Record(path, source);
        module.UpdateCode(ModuleFileFormat.StripHeader(source, module.Kind));

        if (module.Kind is ModuleKind.UserControl or ModuleKind.PropertyPage)
        {
            var formDeserializer = new FormDeserializer();
            var errorSink = new DeserializeErrorSink();
            var fresh = formDeserializer.Deserialize(module.Owner, source, errorSink);
            if (fresh != null)
            {
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
        var diskContent = ModuleFileFormat.ToFileContent(module.Code, name, kind);
        File.WriteAllText(module.AbsolutePath, diskContent);
        baselineStore.Record(module.AbsolutePath, diskContent);
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
        File.WriteAllText(module.AbsolutePath, ctl);
        baselineStore.Record(module.AbsolutePath, ctl);
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
        File.WriteAllText(module.AbsolutePath, pag);
        baselineStore.Record(module.AbsolutePath, pag);
        project.AddModule(module);
        return Task.FromResult(module);
    }

    private static string ProjectFilesDirectory(ProjectDefinition project) =>
        project.AbsolutePath is { } p
            ? Path.GetDirectoryName(p)!
            : Path.Combine(Path.GetTempPath(), "hexide_" + project.Name);

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
        var frxPath = Path.ChangeExtension(formPath, ".frx");
        if (frxContent != null && frxContent.Length > 0)
            AtomicWriteBytes(frxPath, frxContent);
        else if (File.Exists(frxPath))
        {
            File.Delete(frxPath);
            baselineStore.Remove(frxPath);
        }
    }

    private static string CompanionBinaryExtension(string sourceFilePath) =>
        Path.GetExtension(sourceFilePath).ToLowerInvariant() switch
        {
            ".ctl" => ".ctx",
            ".pag" => ".pgx",
            _ => ".frx"
        };

    // Instance (not static) so each atomic write records a baseline. Recording the just-written
    // content immediately — well before the resulting FileSystemWatcher event is processed (it is
    // debounced) — is the primary self-write-suppression mechanism for the file watcher: the watcher
    // re-hashes the file, sees it matches the baseline, and ignores the IDE's own save.
    private void AtomicWriteText(string targetPath, string content)
    {
        var tmp = targetPath + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, targetPath, overwrite: true);
            baselineStore.Record(targetPath, content);
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
            var companionPath = Path.ChangeExtension(modulePath, CompanionBinaryExtension(modulePath));
            if (binary is not null)
                AtomicWriteBytes(companionPath, binary);
            else if (File.Exists(companionPath))
            {
                File.Delete(companionPath);
                baselineStore.Remove(companionPath);
            }
            return;
        }

        // .bas/.cls: prepend the canonical VB6 header (ModuleFileFormat) so vb6.exe can load it; Code itself
        // is body-only. (Unmanaged kinds return Code unchanged.)
        AtomicWriteText(modulePath, ModuleFileFormat.ToFileContent(module.Code, module.Name, module.Kind));
    }

    private void SerializeOnlyProjectToFile(ProjectDefinition definition, string projectPath)
    {
        definition.AbsolutePath = projectPath;

        var serializer = new ProjectSerializer();
        var serialized = serializer.Serialize(definition, projectPath);

        AtomicWriteText(projectPath, serialized);
    }
}
