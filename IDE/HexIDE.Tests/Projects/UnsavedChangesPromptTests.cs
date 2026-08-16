using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Covers the save-changes prompt's dirty-tracking (issue #14).
///
/// Before this, the prompt listed every file unconditionally. That was tolerable on an explicit
/// File ▸ Open, but the same path now runs on every IDE close, so an undirty project must raise no
/// dialog at all. "Dirty" is defined as *saving would change the file*: each item is rendered through
/// the serializer its save path uses and compared against the baseline recorded at load/save.
///
/// These use a real <see cref="FileBaselineStore"/>, not a substitute. A substituted store returns null
/// from TryGet, which makes every item vacuously dirty and silently voids the whole suite.
/// </summary>
public class UnsavedChangesPromptTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-dirty-" + Guid.NewGuid().ToString("N"));

    public UnsavedChangesPromptTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();
    private SaveChangesViewModel? shownDialog;
    private int dialogsShown;

    private ProjectService MakeService(bool userChoosesSave = true, bool userCancels = false)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));
        projectManager.When(m => m.UnloadAllProjects()).Do(_ => loaded.Clear());

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                shownDialog = vm;
                dialogsShown++;
                if (userCancels) return Task.FromResult(false);
                if (userChoosesSave) vm.Yes(); else vm.No();
            }
            return Task.FromResult(true);
        });

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    /// <summary>
    /// Opens a project and immediately saves it, so every baseline matches this build's serializer
    /// output. Without that, a hand-authored fixture can differ cosmetically from what a save would
    /// write and register as dirty for reasons that have nothing to do with the test.
    /// </summary>
    private async Task<(ProjectService svc, ProjectDefinition project)> OpenNormalised()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Test.vbp"),
            "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");

        var svc = MakeService();
        await svc.OpenProject(Path.Join(dir, "Test.vbp"));
        var project = loaded.Single();
        await svc.SaveProject(project, saveAs: false);
        dialogsShown = 0;
        shownDialog = null;
        return (svc, project);
    }

    [Fact]
    public async Task An_unchanged_project_raises_no_prompt_at_all()
    {
        var (svc, _) = await OpenNormalised();

        await svc.UnloadAllProjects();

        dialogsShown.Should().Be(0, "closing a project you only looked at must not ask anything");
    }

    [Fact]
    public async Task An_edited_module_raises_the_prompt()
    {
        var (svc, project) = await OpenNormalised();

        project.Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        await svc.UnloadAllProjects();

        dialogsShown.Should().Be(1);
        shownDialog!.ChangedFiles.Select(f => f.Name).Should().Contain("Module1");
    }

    [Fact]
    public async Task The_prompt_lists_only_what_actually_changed()
    {
        var (svc, project) = await OpenNormalised();

        project.Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        await svc.UnloadAllProjects();

        // The .vbp itself is untouched, so it must not appear beside the module.
        shownDialog!.ChangedFiles.Where(f => f.Project != null).Should().BeEmpty();
        shownDialog.ChangedFiles.Where(f => f.Module != null).Should().ContainSingle();
    }

    [Fact]
    public async Task Cancelling_the_prompt_aborts_the_unload_and_keeps_the_project_loaded()
    {
        await OpenNormalised();
        // Rebuild the service so this run's dialog answers Cancel.
        var svc = MakeService(userCancels: true);
        var project = loaded.Single();
        project.Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");

        var act = async () => await svc.UnloadAllProjects();

        await act.Should().ThrowAsync<OperationCanceledException>();
        loaded.Should().ContainSingle("Cancel must abort the close, matching VB6");
    }

    [Fact]
    public async Task An_untouched_project_loaded_from_disk_raises_no_prompt()
    {
        // The real-world case: files HexIDE never wrote. If the serializers are not byte-faithful to
        // what VB6 emitted, every close of a genuine project would prompt for changes nobody made.
        // This .vbp is the shape of a real one — the demo/battleship project, which vb6.exe compiles.
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Ship.cls"),
            "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\n  Persistable = 0  'NotPersistable\r\n"
          + "  DataBindingBehavior = 0  'vbNone\r\n  DataSourceBehavior  = 0  'vbNone\r\n"
          + "  MTSTransactionMode  = 0  'NotAnMTSObject\r\nEND\r\nAttribute VB_Name = \"Ship\"\r\n"
          + "Attribute VB_GlobalNameSpace = False\r\nAttribute VB_Creatable = True\r\n"
          + "Attribute VB_PredeclaredId = False\r\nAttribute VB_Exposed = False\r\n"
          + "Public Sub Sink()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Test.vbp"),
            "Name=Battleship\r\nType=Exe\r\nModule=Module1; Module1.bas\r\nClass=Ship; Ship.cls\r\n"
          + "Startup=\"Sub Main\"\r\nExeName32=\"Battleship.exe\"\r\n"
          + "Reference=*\\G{00020430-0000-0000-C000-000000000046}#2.0#0##OLE Automation\r\n");
        var svc = MakeService();

        await svc.OpenProject(Path.Join(dir, "Test.vbp"));
        await svc.UnloadAllProjects();

        var kinds = shownDialog?.ChangedFiles
            .Select(f => f.Project != null ? "project:" + f.Name
                       : f.Module != null ? "module:" + f.Name
                       : "form:" + f.Name) ?? [];
        dialogsShown.Should().Be(0, "dirty items were: " + string.Join(", ", kinds));
    }

    [Fact]
    public async Task Saving_clears_the_dirty_state_so_a_second_close_is_silent()
    {
        var (svc, project) = await OpenNormalised();

        project.Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        await svc.SaveProject(project, saveAs: false);
        await svc.UnloadAllProjects();

        dialogsShown.Should().Be(0, "the save updated the baseline, so nothing is dirty any more");
    }
}
