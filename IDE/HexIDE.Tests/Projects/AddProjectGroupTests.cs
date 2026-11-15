using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tools;

namespace HexIDE.Tests.Projects;

public sealed class AddProjectGroupTests : IDisposable
{
    private readonly string directory = Path.Join(Path.GetTempPath(), "hexide-add-group-" + Guid.NewGuid().ToString("N"));
    private readonly ProjectManager manager;
    private readonly ProjectService service;
    private readonly ProjectToolViewModel explorer;
    private readonly ContributedProjects types = new();
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();

    public AddProjectGroupTests()
    {
        Directory.CreateDirectory(directory);
        var events = Substitute.For<IEventBus>();
        var editors = Substitute.For<IEditorService>();
        manager = new ProjectManager(editors, events);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString("Str.Tool.Project.TitleProject").Returns("Project - {0}");
        strings.GetString("Str.Tool.Project.TitleGroup").Returns("Project Group - {0}");
        service = new ProjectService(new Vb6ReadOnlyRegionWriter(), () => throw new InvalidOperationException(),
            windows, events, manager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            strings, Substitute.For<IHeaderRefresher>(), new Vb6ProjectFileFormat(), new ComponentRegistry(), contributed: types);
        explorer = new ProjectToolViewModel(manager, events, service, editors, strings);
    }

    private string WriteProject(string name)
    {
        var path = Path.Join(directory, name + ".vbp");
        File.WriteAllText(path, $"Type=Exe\r\nName=\"{name}\"\r\n");
        return path;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddingAnExistingProject_CreatesTheGroup(bool contributed)
    {
        await service.OpenProject(WriteProject("First"));
        var second = WriteProject("Second");
        using var registration = types.Register(new AddinProjectType("Stand-in projects")
        {
            FileNames = ["*.sproj"],
            OpenAsync = (_, _, _) => Task.FromResult(new AddinProjectContent("Second")),
        }, new AddinOrigin("provider", "A provider"));
        if (contributed)
        {
            second = Path.Join(directory, "Second.sproj");
            File.WriteAllText(second, "stand-in");
        }

        await service.OpenProject(second);

        manager.LoadedProjects.Should().HaveCount(2);
        manager.CurrentGroup.Should().NotBeNull();
        manager.CurrentGroup!.Name.Should().Be("Group1");
        explorer.Title.Should().Be("Project Group - Group1");
        explorer.RootItems.Should().ContainSingle().Which.Should().BeOfType<ProjectGroupViewModel>();
    }

    [Fact]
    public async Task OpeningASingleProject_DoesNotCreateAGroup()
    {
        await service.OpenProject(WriteProject("Only"));

        manager.CurrentGroup.Should().BeNull();
        explorer.Title.Should().Be("Project - Only");
    }

    [Fact]
    public async Task OpeningAGroup_UsesItsOwnNamePathAndStartupProject()
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "OuterGroup.vbg");
        File.WriteAllText(path, "VBGROUP 5.00\r\nProject=First.vbp\r\nProject=Second.vbp\r\nStartupProject=Second.vbp\r\n");

        await service.OpenProject(path);

        manager.LoadedProjects.Should().HaveCount(2);
        manager.CurrentGroup.Should().NotBeNull();
        manager.CurrentGroup!.Name.Should().Be("OuterGroup");
        manager.CurrentGroup.AbsolutePath.Should().Be(path);
        manager.StartupProject!.Name.Should().Be("Second");
        explorer.Title.Should().Be("Project Group - OuterGroup");
    }

    [Theory]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "First")]
    [InlineData("Project=First.vbp\r\nStartupProject=Second.vbp\r\n", "Second")]
    public async Task OpeningVb6Group_LoadsStartupMemberInFileOrder(string members, string startup)
    {
        WriteProject("First");
        WriteProject("Second");
        var path = Path.Join(directory, "Oracle.vbg");
        File.WriteAllText(path, "VBGROUP 5.0\r\n" + members);

        await service.OpenProject(path);

        manager.LoadedProjects.Select(p => p.Name).Should().Equal("First", "Second");
        manager.StartupProject!.Name.Should().Be(startup);
        manager.CurrentGroup!.UnknownLines.Should().BeEmpty();
    }

    // Reload, chosen when a save finds a project's file changed outside HexIDE, keeps the project's place in its group:
    // the group, the order and the startup project, so saving the group writes what it read (#4009).
    [AvaloniaTheory]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "First")]
    [InlineData("StartupProject=First.vbp\r\nProject=Second.vbp\r\n", "Second")]
    [InlineData("Project=First.vbp\r\nStartupProject=Second.vbp\r\nProject=Third.vbp\r\nKept=1\r\n", "Second")]
    public async Task Reload_after_an_outside_change_keeps_the_project_in_its_place_in_the_group(string members, string reloaded)
    {
        WriteProject("First");
        WriteProject("Second");
        WriteProject("Third");
        var path = Path.Join(directory, "G.vbg");
        var groupBytes = System.Text.Encoding.ASCII.GetBytes("VBGROUP 5.0\r\n" + members);
        File.WriteAllBytes(path, groupBytes);
        await service.OpenProject(path);
        var group = manager.CurrentGroup;
        var order = manager.LoadedProjects.Select(p => p.Name).ToList();
        var startup = manager.StartupProject!.Name;
        var project = manager.LoadedProjects.Single(p => p.Name == reloaded);
        await service.SaveProject(project, saveAs: false);
        project.Name = "Ours";
        File.AppendAllText(project.AbsolutePath!, "Title=\"Outside\"\r\n");
        windows.ShowDialog(Arg.Any<FileConflictViewModel>()).Returns(call =>
        {
            var answer = true;
            var conflict = call.Arg<FileConflictViewModel>();
            conflict.CloseRequested += chosen => answer = chosen;
            conflict.ReloadAll();
            return Task.FromResult(answer);
        });

        var save = () => service.SaveProject(project, saveAs: false);
        await save.Should().ThrowAsync<OperationCanceledException>();

        manager.CurrentGroup.Should().BeSameAs(group);
        manager.CurrentGroup!.AbsolutePath.Should().Be(path);
        manager.LoadedProjects.Select(p => p.Name).Should().Equal(order);
        manager.LoadedProjects.Should().NotContain(project);
        manager.StartupProject!.Name.Should().Be(startup);
        explorer.LoadedProjects.Select(p => p.Name).Should().Equal(order);
        explorer.Title.Should().Be("Project Group - G");
        await service.SaveAllProjects(saveAs: false);
        File.ReadAllBytes(path).Should().Equal(groupBytes);
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
