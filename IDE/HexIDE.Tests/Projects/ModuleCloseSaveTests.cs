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
/// Guards the close-time save prompt against dropping modules (issue #13).
///
/// <see cref="SaveChangesViewModel"/> had overloads for projects and forms only, so
/// <see cref="ProjectService.UnloadAllProjects"/> enumerated <c>project.Forms</c> and never
/// <c>project.Modules</c> — a .bas/.cls edit was discarded on File ▸ New, File ▸ Open, Open Recent or
/// Project ▸ Remove, with the dialog appearing and looking correct throughout.
/// </summary>
public class ModuleCloseSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-mod-" + Guid.NewGuid().ToString("N"));

    public ModuleCloseSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();
    private SaveChangesViewModel? shownDialog;

    private ProjectService MakeService(bool userChoosesSave = true)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            // Stand in for the user: tick every listed file and press Yes (or No).
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                shownDialog = vm;
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

    private string WriteProject()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Fact]
    public async Task Unloading_a_project_offers_its_edited_modules_for_saving()
    {
        var svc = MakeService();
        await svc.OpenProject(WriteProject());

        loaded.Single().Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        await svc.UnloadAllProjects();

        shownDialog.Should().NotBeNull();
        shownDialog!.ChangedFiles.Where(f => f.Module != null).Select(f => f.Name)
            .Should().Contain("Module1");
    }

    [Fact]
    public async Task Unloading_a_project_writes_edited_module_code_to_disk()
    {
        var basPath = Path.Join(dir, "Module1.bas");
        var svc = MakeService();
        await svc.OpenProject(WriteProject());

        loaded.Single().Modules.Single().UpdateCode("Public Sub Edited()\r\nEnd Sub\r\n");
        await svc.UnloadAllProjects();

        var onDisk = await File.ReadAllTextAsync(basPath);
        onDisk.Should().Contain("Public Sub Edited()");
        onDisk.Should().NotContain("Public Sub Original()");
    }

    [Fact]
    public async Task Removing_one_project_writes_its_edited_module_code_to_disk()
    {
        var basPath = Path.Join(dir, "Module1.bas");
        var svc = MakeService();
        await svc.OpenProject(WriteProject());
        var project = loaded.Single();

        project.Modules.Single().UpdateCode("Public Sub ViaRemove()\r\nEnd Sub\r\n");
        await svc.UnloadProject(project);

        (await File.ReadAllTextAsync(basPath)).Should().Contain("Public Sub ViaRemove()");
    }

    [Fact]
    public async Task Choosing_No_leaves_the_module_on_disk_untouched()
    {
        var basPath = Path.Join(dir, "Module1.bas");
        var svc = MakeService(userChoosesSave: false);
        await svc.OpenProject(WriteProject());

        loaded.Single().Modules.Single().UpdateCode("Public Sub Discarded()\r\nEnd Sub\r\n");
        await svc.UnloadAllProjects();

        var onDisk = await File.ReadAllTextAsync(basPath);
        onDisk.Should().Contain("Public Sub Original()");
        onDisk.Should().NotContain("Public Sub Discarded()");
    }
}
