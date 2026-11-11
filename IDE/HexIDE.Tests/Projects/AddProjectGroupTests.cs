using System.IO;
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
            Substitute.For<IWindowManager>(), events, manager, Substitute.For<IRecentProjectsService>(),
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

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
