using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

public sealed class ExternalProjectChangeTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-external-project-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly ProjectService _service;
    private string ProjectFile => Path.Join(_dir, "Test.vbp");
    private string CodeFile => Path.Join(_dir, "Code.bas");

    public ExternalProjectChangeTests()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(CodeFile, "Attribute VB_Name = \"Code\"\r\nSub Main()\r\nEnd Sub\r\n");
        File.WriteAllText(ProjectFile, "Type=Exe\r\nModule=Code; Code.bas\r\nName=\"Test\"\r\n");
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        projects.When(p => p.AddProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Add(call.Arg<ProjectDefinition>()));
        projects.When(p => p.UnloadProject(Arg.Any<ProjectDefinition>())).Do(call => _loaded.Remove(call.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        _service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException("no new-project dialog"), _windows,
            Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), Substitute.For<ILocalizationService>(),
            Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry());
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private async Task<ProjectDefinition> Opened()
    {
        await _service.OpenProject(ProjectFile);
        var project = _loaded.Single();
        await _service.SaveProject(project, saveAs: false);
        return project;
    }

    private void Answer(Action<FileConflictViewModel> choose) =>
        _windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var conflict = call.Arg<FileConflictViewModel>();
            var result = true;
            conflict.CloseRequested += answer => result = answer;
            choose(conflict);
            return Task.FromResult(result);
        });

    [AvaloniaFact]
    public async Task Reload_preserves_the_external_project_and_reopens_its_settings()
    {
        var project = await Opened();
        project.Name = "Ours";
        var theirs = File.ReadAllText(ProjectFile).Replace("Name=\"Test\"", "Name=\"Theirs\"");
        File.WriteAllText(ProjectFile, theirs);
        Answer(c => c.ReloadAll());

        var save = () => _service.SaveProject(project, saveAs: false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        File.ReadAllText(ProjectFile).Should().Be(theirs);
        _loaded.Should().ContainSingle().Which.Name.Should().Be("Theirs");
    }

    [AvaloniaFact]
    public async Task Keep_my_changes_asks_before_overwriting_the_external_project()
    {
        var project = await Opened();
        project.Name = "Ours";
        File.AppendAllText(ProjectFile, "Title=\"Theirs\"\r\n");
        Answer(c => c.KeepAll());

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Contain("Name=\"Ours\"").And.NotContain("Title=\"Theirs\"");
        await _windows.Received(1).ShowDialog(Arg.Is<FileConflictViewModel>(c => c.CanCancel && c.Files.Single().Name == "Test.vbp"));
    }

    [AvaloniaFact]
    public async Task Cancel_leaves_the_project_and_dirty_member_files_untouched()
    {
        var project = await Opened();
        project.Name = "Ours";
        project.Modules.Single().UpdateCode(project.Modules.Single().Code + "' unsaved\r\n");
        var code = File.ReadAllBytes(CodeFile);
        File.AppendAllText(ProjectFile, "Title=\"Theirs\"\r\n");
        var theirs = File.ReadAllBytes(ProjectFile);
        Answer(c => c.Cancel());

        var save = () => _service.SaveProject(project, saveAs: false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        File.ReadAllBytes(ProjectFile).Should().Equal(theirs);
        File.ReadAllBytes(CodeFile).Should().Equal(code);
    }

    [AvaloniaFact]
    public async Task An_external_change_with_no_project_edit_is_left_without_a_question()
    {
        var project = await Opened();
        File.AppendAllText(ProjectFile, "Title=\"Theirs\"\r\n");
        var theirs = File.ReadAllBytes(ProjectFile);

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllBytes(ProjectFile).Should().Equal(theirs);
        await _windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }

    [AvaloniaFact]
    public async Task A_project_edit_with_unchanged_disk_saves_without_a_question()
    {
        var project = await Opened();
        project.Name = "Ours";

        await _service.SaveProject(project, saveAs: false);

        File.ReadAllText(ProjectFile).Should().Contain("Name=\"Ours\"");
        await _windows.DidNotReceive().ShowDialog(Arg.Any<FileConflictViewModel>());
    }
}
