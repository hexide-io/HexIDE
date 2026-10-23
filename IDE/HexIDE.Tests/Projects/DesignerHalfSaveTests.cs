using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Ctrl+S from a UserControl's or PropertyPage's designer, or from its code window, saves the <c>.ctl</c> or
/// <c>.pag</c> (hexide-io/HexIDE#474).
/// </summary>
/// <remarks>
/// Both doors save the designer half, a <see cref="FormDefinition"/>. A new one has no file until the project is saved
/// (#500), so the form's own save asked for a <c>.frm</c> and wrote the edit there, beside a module whose file the
/// project names and which was never written. A save that starts from a designer half is its module's save.
/// </remarks>
public class DesignerHalfSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-designerhalf-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();
    private readonly List<Avalonia.Platform.Storage.FilePickerSaveOptions> asked = new();

    public DesignerHalfSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        // Every picker is answered with the name it suggests, in this test's folder, and recorded.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(ci =>
            {
                var options = ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>();
                asked.Add(options);
                return Task.FromResult<string?>(Path.Join(dir, options.SuggestedFileName));
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

    private ProjectDefinition SavedProject()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        return project;
    }

    private static Task<ModuleDefinition> AddNew(ProjectService svc, ProjectDefinition project, string kind) =>
        kind == "UserControl"
            ? svc.AddNewUserControl(project, "UserControl1", interactive: false)
            : svc.AddNewPropertyPage(project, "PropertyPage1", interactive: false);

    [Theory]
    [InlineData("UserControl", "UserControl1.ctl", "Begin VB.UserControl UserControl1 ")]
    [InlineData("PropertyPage", "PropertyPage1.pag", "Begin VB.PropertyPage PropertyPage1 ")]
    public async Task Saving_a_new_designer_half_asks_for_its_module_file_and_writes_it(string kind, string file, string begin)
    {
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), kind);

        (await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false)).Should().BeTrue();

        asked.Should().ContainSingle().Which.SuggestedFileName.Should().Be(file, "the file the project names is the module's");
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo([file], "no .frm beside it");
        File.ReadAllText(Path.Join(dir, file)).Should().Contain(begin);
        module.AbsolutePath.Should().Be(Path.Join(dir, file));
        module.FormPart!.AbsolutePath.Should().Be(Path.Join(dir, file), "both halves are one file, as a load gives them");
    }

    [Theory]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task The_next_save_from_the_designer_half_writes_the_same_file_without_asking(string kind)
    {
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), kind);
        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);
        var written = module.AbsolutePath!;
        module.UpdateCode(module.Code + "Public Sub Added()\r\nEnd Sub\r\n");

        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        asked.Should().ContainSingle("the second save has the file the first chose");
        File.ReadAllText(written).Should().Contain("Public Sub Added()");
        Directory.GetFiles(dir).Should().ContainSingle();
    }

    [Fact]
    public async Task Save_As_from_a_designer_half_asks_for_a_ctl()
    {
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), "UserControl");
        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        await svc.SaveForm(module.FormPart!, saveAs: true, interactive: false);

        asked.Should().HaveCount(2);
        asked[1].SuggestedFileName.Should().EndWith(".ctl");
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().NotContain(f => f!.EndsWith(".frm"));
    }
}
