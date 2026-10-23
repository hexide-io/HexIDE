using System.IO;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Make Project Group with no group open writes nothing (#746). VB6 greys the command out until a group is open
/// (docs/vb6-fidelity-oracle.md, "Make Project Group"), so the menu no longer offers it. This is the service's own
/// guard, for a caller that reaches it without the menu. It used to save the project and then throw on the missing
/// group, with nothing said.
/// </summary>
public sealed class MakeProjectGroupTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-makegroup-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public MakeProjectGroupTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
    }

    private ProjectService MakeService()
    {
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));
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

    [Fact]
    public async Task With_one_project_and_no_group_it_writes_nothing_and_asks_nothing()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        var svc = MakeService();
        await svc.OpenProject(vbp);
        // An edit a save would write, and a time no write leaves alone.
        var module = loaded.Single().Modules.Single();
        module.UpdateCode(module.Code + "' edited\r\n");
        var old = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        foreach (var file in Directory.GetFiles(dir))
            File.SetLastWriteTimeUtc(file, old);
        windowManager.ClearReceivedCalls();

        var thrown = await Record.ExceptionAsync(svc.MakeProjectGroup);

        Directory.GetFiles(dir).Select(f => (Path.GetFileName(f), File.GetLastWriteTimeUtc(f)))
            .Should().BeEquivalentTo([("Module1.bas", old), ("Test.vbp", old)], "nothing is saved, and no group file is made");
        thrown.Should().BeNull();
        await windowManager.DidNotReceiveWithAnyArgs().SaveFilePickerAsync(default!);
    }
}
