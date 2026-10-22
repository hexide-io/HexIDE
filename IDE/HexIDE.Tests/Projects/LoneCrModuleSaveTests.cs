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
/// A <c>.bas</c> whose lines end with a lone CR keeps its code through a real open and Save Project (#1020).
/// </summary>
/// <remarks>
/// Save Project writes every module, and the dirty check compares one render with another, so a module whose code the
/// header split lost was written back without anyone editing it and without a prompt. <c>LoneCrLineEndTests</c> pins
/// the readers; this pins the path from the file on disk to the file written back.
/// </remarks>
public class LoneCrModuleSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-lonecr-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public LoneCrModuleSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
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
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            Substitute.For<IHeaderRefresher>());
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    public async Task A_module_whose_lines_end_alone_keeps_its_code_through_Save_Project(string eol)
    {
        var lines = new[]
        {
            "Attribute VB_Name = \"Utils\"",
            "Option Explicit",
            "Public Function Twice(ByVal n As Long) As Long",
            "Attribute Twice.VB_Description = \"Doubles it\"",
            "    Twice = n * 2",
            "End Function",
        };
        var bas = Path.Join(dir, "Utils.bas");
        await File.WriteAllBytesAsync(bas, System.Text.Encoding.ASCII.GetBytes(string.Join(eol, lines) + eol),
            TestContext.Current.CancellationToken);
        var vbp = Path.Join(dir, "Test.vbp");
        await File.WriteAllTextAsync(vbp, "Type=Exe\r\nModule=Utils; Utils.bas\r\nName=\"Test\"\r\n", TestContext.Current.CancellationToken);

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        module.Code.Should().Contain("Public Function Twice", "the code is the module's, not part of its header");

        await svc.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllTextAsync(bas, TestContext.Current.CancellationToken)).Should().Be(string.Join("\r\n", lines) + "\r\n",
            "every line is written back, each ending CRLF as the header split joins them");
    }
}
