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
/// A project file lists its members by their files, so one that has never been saved cannot be listed. The
/// save-changes prompt let the project be ticked while such a member was not, and the project file was then
/// written without it.
///
/// <para>
/// For the startup form that is worse than a missing file. The project file still said <c>Startup="Form1"</c>,
/// and vb6.exe compiles that silently, exit 0, into a program that starts at <c>Sub Main</c> instead
/// (hexide-io/HexIDE#497, measured in <c>docs/vb6-fidelity-oracle/when-a-document-gets-a-file-and-what-a-dangling-startup-does.md</c>). VB6 cannot write such a file: its
/// prompt is one Yes over the whole list. So the save is refused before anything is written, and the unload
/// that asked for it does not happen.
/// </para>
/// </summary>
public class UnsavedMemberRefusalTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-unlisted-" + Guid.NewGuid().ToString("N"));

    public UnsavedMemberRefusalTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private const string RefusalText = "Nothing was saved. {0} would leave out: {1}";

    private readonly List<ProjectDefinition> loaded = new();
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    /// <param name="untick">What the developer unticks in the save-changes prompt before pressing Yes.</param>
    private ProjectService MakeService(Func<ChangedFileViewModel, bool> untick)
    {
        projectManager.LoadedProjects.Returns(_ => loaded);

        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                foreach (var file in vm.SelectedFiles.Where(untick).ToList())
                    vm.SelectedFiles.Remove(file);
                vm.Yes();
            }
            return Task.FromResult(true);
        });
        // The project has never been saved, so its save asks where: here.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.UnsavedMembers.NothingWritten").Returns(RefusalText);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            localization,
            Substitute.For<IHeaderRefresher>());
    }

    /// <summary>A project as File ▸ New Project makes it: a <c>Form1</c> that is its startup form, and no files.</summary>
    private ProjectDefinition NewProject()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form1"));
        loaded.Add(project);
        return project;
    }

    private List<string> MessagesShown() => windowManager.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
        .Select(c => (string)c.GetArguments()[0]!)
        .ToList();

    private string[] FilesWritten() => Directory.GetFiles(dir).Select(Path.GetFileName).ToArray()!;

    [Fact]
    public async Task Closing_with_the_project_ticked_and_its_unsaved_startup_form_not_writes_nothing_and_stays_open()
    {
        var project = NewProject();
        var svc = MakeService(untick: file => file.Form is not null);
        project.StartupForm!.Name.Should().Be("Form1");

        Func<Task> close = () => svc.UnloadAllProjects();

        var refusal = (await close.Should().ThrowAsync<UnsavedMemberRefusal>()).Which;
        refusal.Project.Should().Be("Project1");
        refusal.Members.Should().Equal("Form1");
        FilesWritten().Should().BeEmpty("a project file without Form1 is one vb6.exe builds into the wrong program");
        projectManager.DidNotReceive().UnloadAllProjects();
        MessagesShown().Should().ContainSingle().Which.Should().Contain("Project1").And.Contain("Form1");
    }

    [Fact]
    public async Task An_unsaved_form_that_is_not_the_startup_form_is_refused_as_well()
    {
        // The startup form has a file here, so Startup= would be listed correctly. Form2 alone would be dropped,
        // which loses it as surely, and nothing about the startup line catches that.
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        loaded.Add(project);
        var svc = MakeService(untick: file => file.Form?.Name == "Form2");
        var form1 = await svc.AddNewForm(project, "Form1", interactive: false);
        (await svc.SaveForm(form1, saveAs: false)).Should().BeTrue(); // Form1 now has its file, by the picker
        project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Form2"));
        project.StartupForm!.Name.Should().Be("Form1");
        project.StartupForm.AbsolutePath.Should().NotBeNull();

        Func<Task> close = () => svc.UnloadAllProjects();

        (await close.Should().ThrowAsync<UnsavedMemberRefusal>()).Which.Members.Should().Equal("Form2");
        Directory.GetFiles(dir, "*.vbp").Should().BeEmpty();
        projectManager.DidNotReceive().UnloadAllProjects();
    }

    [Fact]
    public async Task A_module_that_has_never_been_saved_is_refused_as_a_form_is()
    {
        var project = NewProject();
        project.AddModule(new ModuleDefinition(project, "Module1", ModuleKind.StandardModule));
        var svc = MakeService(untick: file => file.Module is not null);

        Func<Task> close = () => svc.UnloadAllProjects();

        (await close.Should().ThrowAsync<UnsavedMemberRefusal>()).Which.Members.Should().Equal("Module1");
        Directory.GetFiles(dir, "*.vbp").Should().BeEmpty();
    }

    [Fact]
    public async Task Closing_one_project_refuses_the_same_way()
    {
        var project = NewProject();
        var svc = MakeService(untick: file => file.Form is not null);

        Func<Task> close = () => svc.UnloadProject(project);

        (await close.Should().ThrowAsync<UnsavedMemberRefusal>()).Which.Members.Should().Equal("Form1");
        FilesWritten().Should().BeEmpty();
        projectManager.DidNotReceive().UnloadProject(project);
    }

    [Fact]
    public async Task Ticking_the_form_as_well_saves_both_and_closes()
    {
        // The control: nothing about the refusal stops the ordinary close.
        var project = NewProject();
        var svc = MakeService(untick: _ => false);

        await svc.UnloadAllProjects();

        FilesWritten().Should().BeEquivalentTo(["Form1.frm", "Project1.vbp"]);
        File.ReadAllText(Path.Join(dir, "Project1.vbp")).Should().Contain("Form=Form1.frm\r\n").And.Contain("Startup=\"Form1\"\r\n");
        projectManager.Received().UnloadAllProjects();
    }
}
