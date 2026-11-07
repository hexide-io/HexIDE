using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.IDE;

public class FileWatcherServiceTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "HexIDE-watcher-" + Guid.NewGuid());
    private readonly ProjectManager manager = new(Substitute.For<IEditorService>(), new EventBus());
    private readonly EventBus events = new();
    private readonly ConcurrentDictionary<string, byte> seen = new();
    private readonly FileWatcherService watcher;

    public FileWatcherServiceTests()
    {
        Directory.CreateDirectory(root);
        var settings = Substitute.For<ISettingsService>();
        settings.ReloadFilesChangedOutsideIde.Returns(true);
        // Observe detection before UI dispatch; matching the baseline suppresses reloads entirely.
        var baseline = Substitute.For<IFileBaselineStore>();
        baseline.Matches(Arg.Any<string>(), Arg.Any<byte[]>()).Returns(call =>
        {
            seen.TryAdd(call.ArgAt<string>(0), 0);
            return true;
        });
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(_ => DateTime.UtcNow);
        watcher = new FileWatcherService(manager, Substitute.For<IDocumentDockService>(),
            Substitute.For<IProjectRunnerService>(), settings, baseline, Substitute.For<IProjectService>(),
            events, Substitute.For<IWindowManager>(), Substitute.For<IStatusBarService>(),
            Substitute.For<ILocalizationService>(), clock, Substitute.For<IHeaderRefresher>());
    }

    private string FileAt(string directory, string name = "M.bas")
    {
        var dir = Path.Combine(root, directory);
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "initial");
        return path;
    }

    private ProjectDefinition Project(string? path)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P") { AbsolutePath = path };
        return project;
    }

    private static ModuleDefinition Module(ProjectDefinition project, string? path)
    {
        var module = new ModuleDefinition(project, "M", ModuleKind.StandardModule) { AbsolutePath = path };
        project.AddModule(module);
        return module;
    }

    private void Change(string path, bool expected = true)
    {
        seen.TryRemove(path, out _);
        File.AppendAllText(path, "external");
        SpinWait.SpinUntil(() => seen.ContainsKey(path), expected ? 5000 : 1200)
            .Should().Be(expected, path);
    }

    [Theory]
    [InlineData("project/Modules")]
    [InlineData("Shared")]
    public void LoadedDocumentOutsideProjectDirectory_IsWatched(string directory)
    {
        var project = Project(FileAt("project", "P.vbp"));
        // A real filesystem path, so it takes the host's separator; the watcher reports events in that form.
        var path = FileAt(directory.Replace('/', Path.DirectorySeparatorChar));
        Module(project, path);
        manager.AddProject(project);
        Change(path);
    }

    private ProjectService ProjectService() => new(
        () => throw new InvalidOperationException(), Substitute.For<IWindowManager>(), new EventBus(),
        manager, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
        Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
        Substitute.For<ILocalizationService>(), Substitute.For<IHeaderRefresher>());

    [Fact]
    public async Task FirstSavePaths_StartWatching()
    {
        var project = Project(null);
        Module(project, null);
        manager.AddProject(project);
        await ProjectService().SaveProjectToDirectory(project, Path.Combine(root, "saved"));
        Change(project.Modules[0].AbsolutePath!);
    }

    [Fact]
    public void SaveWithUnchangedPath_RetriesDirectoryThatDidNotExistAtLoad()
    {
        var project = Project(null);
        var path = Path.Combine(root, "saved", "M.bas");
        var module = Module(project, path);
        manager.AddProject(project);
        FileAt("saved");
        events.Publish(new DocumentSavedEvent(null, module));
        Change(path);
    }

    [Fact]
    public async Task SaveProjectToAnotherDirectory_MovesWatchers()
    {
        var project = Project(FileAt("old", "P.vbp"));
        var oldPath = FileAt("old");
        Module(project, oldPath);
        manager.AddProject(project);
        await ProjectService().SaveProjectToDirectory(project, Path.Combine(root, "new"));
        Change(project.Modules[0].AbsolutePath!);
        Change(oldPath, false);
        manager.UnloadProject(project);
        Change(project.Modules[0].AbsolutePath!, false);
    }

    [Fact]
    public void FormAddedRepointedAndRemoved_UpdatesDirectories()
    {
        var project = Project(FileAt("project", "P.vbp"));
        manager.AddProject(project);
        var path = FileAt("forms", "F.frm");
        var form = new FormDefinition(project, FormComponentClass.Instance, "F") { AbsolutePath = path };
        project.AddForm(form);
        Change(path);
        form.AbsolutePath = FileAt("moved", "F.frm");
        Change(form.AbsolutePath);
        Change(path, false);
        project.DeleteForm(form);
        Change(form.AbsolutePath, false);
    }

    [Fact]
    public void RepointedDocument_WatchesNewDirectoryAndReleasesOld()
    {
        var project = Project(FileAt("project", "P.vbp"));
        var oldPath = FileAt("old");
        var module = Module(project, oldPath);
        manager.AddProject(project);
        var newPath = FileAt("new");
        module.AbsolutePath = newPath;
        Change(newPath);
        Change(oldPath, false);
    }

    [Fact]
    public void AddedAndRemovedDocument_UpdatesDirectories()
    {
        var project = Project(FileAt("project", "P.vbp"));
        manager.AddProject(project);
        var path = FileAt("added");
        var module = Module(project, path);
        Change(path);
        project.DeleteModule(module);
        Change(path, false);
        module.AbsolutePath = FileAt("removed");
        Change(module.AbsolutePath, false);
    }

    [Fact]
    public void UnloadAfterProjectMove_ReleasesOriginalDirectory()
    {
        var oldPath = FileAt("old");
        var project = Project(FileAt("old", "P.vbp"));
        manager.AddProject(project);
        project.AbsolutePath = FileAt("new", "P.vbp");
        manager.UnloadProject(project);
        Change(oldPath, false);
    }

    [Fact]
    public void SharedDocumentDirectory_RemainsUntilLastProjectUnloads()
    {
        var path = FileAt("shared");
        var first = Project(FileAt("one", "P.vbp"));
        var second = Project(FileAt("two", "P.vbp"));
        Module(first, path);
        Module(second, path);
        manager.AddProject(first);
        manager.AddProject(second);
        manager.UnloadProject(first);
        Change(path);
        manager.UnloadProject(second);
        Change(path, false);
    }

    public void Dispose()
    {
        watcher.Dispose();
        Directory.Delete(root, true);
    }
}
