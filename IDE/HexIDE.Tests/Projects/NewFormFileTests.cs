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
/// The files Add Form, Add User Control and Add Property Page write as they are added, read back from disk.
///
/// <para>
/// A new form now carries what VB6 writes for one: CRLF throughout, its five <c>Attribute</c> lines, and
/// <c>StartUpPosition = 3</c> (hexide-io/HexIDE#931, #516, #932). The designer kinds share the form's constructor
/// for their designer half, so the second half of this pins that none of a form's lines leaks into theirs: no
/// VB6-authored <c>.ctl</c> or <c>.pag</c> in the corpus has a <c>StartUpPosition</c> line, and theirs is not a
/// form's <c>Attribute</c> block.
/// </para>
/// </summary>
public class NewFormFileTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-newform-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public NewFormFileTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
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

    private ProjectDefinition NewProject()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        return project;
    }

    [Fact]
    public async Task Add_Form_writes_a_form_in_VB6s_shape()
    {
        var form = await MakeService().AddNewForm(NewProject(), "Form2", interactive: false);

        var text = File.ReadAllText(form.AbsolutePath!);
        text.Replace("\r\n", "").Should().NotContain("\n");
        text.Should().EndWith("\r\n");
        text.Should().Contain("\r\n   StartUpPosition =   3  'Windows Default\r\n");
        text.Should().Contain("End\r\n"
            + "Attribute VB_Name = \"Form2\"\r\n"
            + "Attribute VB_GlobalNameSpace = False\r\n"
            + "Attribute VB_Creatable = False\r\n"
            + "Attribute VB_PredeclaredId = True\r\n"
            + "Attribute VB_Exposed = False\r\n");
    }

    [Fact]
    public void A_new_Standard_EXE_starts_from_a_form_in_VB6s_shape()
    {
        var project = IProjectTemplate.StandardEXE.Create("Project1");

        var form = project.Forms.Single();
        form.Code.Should().StartWith("Attribute VB_Name = \"Form1\"\r\n");
        form.Components[0].GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.StartUpPositionProperty)
            .Should().Be(HexIDE.Runtime.BuiltinTypes.VBStartupPosition.StartUpWindowsDefault);
    }

    [Theory]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task A_new_designer_module_takes_none_of_a_forms_lines(string kind)
    {
        var svc = MakeService();
        var module = kind == "UserControl"
            ? await svc.AddNewUserControl(NewProject(), "Thing1", interactive: false)
            : await svc.AddNewPropertyPage(NewProject(), "Thing1", interactive: false);

        var text = File.ReadAllText(module.AbsolutePath!);
        text.Should().NotContain("StartUpPosition");
        text.Should().NotContain("VB_PredeclaredId = True");
        text.Replace("\r\n", "").Should().NotContain("\n");
    }
}
