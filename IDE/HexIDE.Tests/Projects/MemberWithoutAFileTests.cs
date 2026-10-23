using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A saved project that gains a member with no file has changed, though rendering it would not show that.
///
/// <para>
/// The dirty check renders the project through the serializer and compares it with what was saved. The serializer
/// skips a member with no file, so a project whose only change was such a member rendered as it was saved and read
/// as clean. The Project Explorer's own Add Form adds exactly that. At close, the prompt then listed the form and no
/// project; saving wrote <c>Form1.frm</c>, the <c>.vbp</c> never listed it, and the next open had no Form1, even
/// when the developer said Yes to everything (hexide-io/HexIDE#1359).
/// </para>
/// </summary>
public class MemberWithoutAFileTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-nofile-" + Guid.NewGuid().ToString("N"));

    public MemberWithoutAFileTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private SaveChangesViewModel? shownDialog;

    private ProjectService MakeService()
    {
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                shownDialog = vm;
                vm.Yes(); // everything listed, saved
            }
            return Task.FromResult(true);
        });
        // A form with no file asks where to go: the project's folder, under its own name.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));

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
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
    }

    /// <summary>A saved project, its baselines current, so nothing about it reads as changed.</summary>
    private async Task<(ProjectService Svc, ProjectDefinition Project)> SavedProject()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "P.vbp"),
            "Type=Exe\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"P\"\r\n");
        var svc = MakeService();
        await svc.OpenProject(Path.Join(dir, "P.vbp"));
        var project = loaded.Single();
        await svc.SaveProject(project, saveAs: false);
        return (svc, project);
    }

    [Fact]
    public async Task A_form_added_with_no_file_brings_its_project_into_the_close_prompt()
    {
        var (svc, project) = await SavedProject();
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1")); // the explorer's Add Form

        await svc.UnloadAllProjects();

        shownDialog!.ChangedFiles.Select(f => (f.Project is not null, f.Name))
            .Should().Contain((true, "P"), "the project file has to change to list the new form");
    }

    [Fact]
    public async Task Saving_everything_at_close_lists_the_new_form_in_the_project_file()
    {
        var (svc, project) = await SavedProject();
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));

        await svc.UnloadAllProjects();

        File.Exists(Path.Join(dir, "Form1.frm")).Should().BeTrue();
        File.ReadAllText(Path.Join(dir, "P.vbp")).Should().Contain("Form=Form1.frm\r\n",
            "a form written and named by nothing is lost from the project at its next open");
    }

    [Fact]
    public async Task A_module_added_with_no_file_counts_the_same()
    {
        var (svc, project) = await SavedProject();
        project.AddModule(new ModuleDefinition(project, "Module2", ModuleKind.StandardModule));

        await svc.UnloadAllProjects();

        shownDialog!.ChangedFiles.Should().Contain(f => f.Project != null);
        File.ReadAllText(Path.Join(dir, "P.vbp")).Should().Contain("Module=Module2; Module2.bas\r\n");
    }

    [Fact]
    public async Task A_saved_project_with_every_member_on_disk_still_raises_no_prompt()
    {
        // The control: counting a member with no file must not make every project read as changed.
        var (svc, _) = await SavedProject();
        shownDialog = null;

        await svc.UnloadAllProjects();

        shownDialog.Should().BeNull();
    }
}
