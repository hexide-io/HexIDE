using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using HexIDE.Localization;

namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class WorkingCopyReplyTests
{
    [Theory]
    [InlineData("set_control_property")]
    [InlineData("set_file_content")]
    [InlineData("add_control")]
    public void First_contact_explains_the_new_project_working_copy(string name)
    {
        var description = ToolSource.Tools.Single(t => t.Name == name).Description;
        description.Should().Contain("working copy").And.Contain("temp folder").And.Contain("without a picker");
        description.Length.Should().BeLessThanOrEqualTo(1900);
    }

    [AvaloniaTheory]
    [InlineData(true, "property")]
    [InlineData(false, "property")]
    [InlineData(true, "source")]
    [InlineData(false, "source")]
    [InlineData(true, "control")]
    [InlineData(false, "control")]
    public async Task Write_reply_distinguishes_the_working_copy_from_a_chosen_file(bool scratch, string operation)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-2707-" + Guid.NewGuid().ToString("N"));
        var form = project.Forms[0];
        form.AbsolutePath = Path.Combine(project.WorkingDirectory + (scratch ? "" : "-chosen"), "Form1.frm");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns([]);
        var events = new EventBus();
        var windows = Substitute.For<IWindowManager>();
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, events,
            projects, Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>());
        var directory = Path.GetDirectoryName(form.AbsolutePath)!;
        await service.SaveProjectToDirectory(project, directory);
        try
        {
            var localization = Substitute.For<ILocalizationService>();
            var toolbox = new ToolBoxToolViewModel(dock, Substitute.For<IComponentRegistry>(), localization);
            using var designer = operation == "control"
                ? new FormEditViewModel(toolbox, events, service, Substitute.For<IEditorService>(), windows,
                    Substitute.For<ISettingsService>(), localization).Initialize(form)
                : null;
            if (designer is not null)
                dock.OpenDocuments.Returns([designer]);
            using var context = new IdeContext(projects, dock, Substitute.For<ILspClient>(),
                null!, Substitute.For<IProjectRunnerService>(), service, events, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
            var tools = new HexIdeTools(context);
            bool success;
            string? note;
            if (operation == "control")
            {
                var added = await tools.AddControlAsync("Form1", "CommandButton", 10, 10, 81, 33);
                success = added.Success;
                note = added.Note;
            }
            else
            {
                var result = operation == "source"
                    ? await tools.SetFileContentAsync("Form1", "Option Explicit")
                    : await tools.SetControlPropertyAsync("Form1", "Form1", "Caption", "Go");
                success = result.Success;
                note = result.Note;
            }
            success.Should().BeTrue();
            note.Should().Contain(form.AbsolutePath);
            if (scratch)
                note.Should().Contain("working copy").And.Contain("unsaved changes").And.Contain("Save Project");
            else
                note.Should().Contain("Wrote the file").And.NotContain("working copy");
            File.ReadAllText(form.AbsolutePath!).Should().Contain(operation switch
            {
                "source" => "Option Explicit", "control" => "Command1", _ => "Go"
            });
            service.HasUnsavedChanges(form).Should().Be(scratch);
            await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Project_info_explains_a_project_without_a_home(bool scratch)
    {
        var project = TestHelpers.CreateProjectWithForm("Project1");
        project.WorkingDirectory = Path.Combine(Path.GetTempPath(), "hexide-2707");
        project.AbsolutePath = scratch ? Path.Combine(project.WorkingDirectory, "Project1.vbp") : null;
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        projects.StartupProject.Returns(project);
        using var context = new IdeContext(projects, Substitute.For<IDocumentDockService>(), Substitute.For<ILspClient>(),
            null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);
        var result = await new HexIdeTools(context).GetProjectInfoAsync(TestContext.Current.CancellationToken);
        result.Note.Should().Contain("Project1").And.Contain("no chosen home").And.Contain("Save Project");
    }
}
