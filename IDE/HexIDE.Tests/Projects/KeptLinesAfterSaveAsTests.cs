using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A project saved to another folder still names, from there, each member it keeps only as a line, and still knows
/// which file that line names (#737). The line was written as it was read, so the new project file named a file
/// beside itself, and the project went on reading the line from its new folder.
/// </summary>
public class KeptLinesAfterSaveAsTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-kept-saveas-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public KeptLinesAfterSaveAsTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
    }

    [Fact]
    public async Task Adding_a_kept_member_reindexes_the_remaining_save_slots()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "B") { AbsolutePath = Path.Join(dir, "B.vbp") };
        project.PreserveItemLine("Module=First; First.bas", 1);
        project.PreserveItemLine("Module=Second; Second.bas", 2);
        project.PreserveItemLine("Form=Last.frm", 3);
        var first = Path.Join(dir, "First.bas");
        File.WriteAllText(first, "Attribute VB_Name = \"First\"\r\n");
        var svc = MakeService();

        await svc.AddExistingModule(project, first, ModuleKind.StandardModule);

        project.PreservedItemLines.Should().Equal("Module=Second; Second.bas", "Form=Last.frm");
        project.PreservedItemLineSlots.Should().BeEquivalentTo(new Dictionary<int, int> { [0] = 2, [1] = 3 });
    }

    [Fact]
    public async Task A_kept_member_is_named_from_the_new_folder_and_still_found_by_its_own_file()
    {
        var svc = MakeService();
        // Not read by the loader, so nothing has pinned the folder its kept line was written in: the save must.
        var project = new ProjectDefinition(VBProjectType.EXE, "B") { AbsolutePath = Path.Join(dir, "B.vbp") };
        loaded.Add(project);
        project.PreservedItemLines.Add(@"Module=Kept; Module1.bas");
        var kept = Path.Join(dir, "Module1.bas");
        await File.WriteAllTextAsync(kept, "Attribute VB_Name = \"Kept\"\r\nOption Explicit\r\n",
            TestContext.Current.CancellationToken);

        var moved = Path.Join(dir, "Moved");
        await svc.SaveProjectToDirectory(project, moved);

        (await File.ReadAllTextAsync(Path.Join(moved, "B.vbp"), TestContext.Current.CancellationToken))
            .Should().Contain("Module=Kept; ..\\Module1.bas\r\n", "the file did not move, and the new project file is a folder down");

        // The file joining the project takes its kept line's place, or the project file would name it twice. Read
        // from the new folder, the line named Moved\Module1.bas and was never matched.
        await svc.AddExistingModule(project, kept, ModuleKind.StandardModule);
        project.PreservedItemLines.Should().BeEmpty();
    }
}
