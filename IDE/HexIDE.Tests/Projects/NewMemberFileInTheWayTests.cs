using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A new form or module is written the moment it is added, so the name it is given decides a path, and a file
/// already at that path is replaced.
///
/// <para>
/// The Add commands checked the name against the project's members only. A folder holding another project's
/// <c>Module1.bas</c>, which is ordinary when two <c>.vbp</c> files share one, lost that module's code to the
/// next Add Module, with nothing said (hexide-io/HexIDE#738). Each case here stages someone else's file first
/// and checks its bytes afterwards, because "it threw" or "it picked another name" does not show the file
/// survived.
/// </para>
/// </summary>
public class NewMemberFileInTheWayTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-inthe-way-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public NewMemberFileInTheWayTests() => Directory.CreateDirectory(dir);

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
        var project = new ProjectDefinition(VBProjectType.EXE, "B") { AbsolutePath = Path.Join(dir, "B.vbp") };
        loaded.Add(project);
        return project;
    }

    /// <summary>Another project's file, in bytes no new member's file could be mistaken for.</summary>
    private string Stage(string fileName)
    {
        var path = Path.Join(dir, fileName);
        File.WriteAllBytes(path, Foreign(fileName));
        return path;
    }

    private static byte[] Foreign(string fileName) =>
        System.Text.Encoding.ASCII.GetBytes($"Attribute VB_Name = \"Other\"\r\n' project A's {fileName}\r\n");

    private static string NextFreeName(ProjectService svc, ProjectDefinition project, string kind) => kind switch
    {
        "Form" => svc.NextFreeFormName(project),
        "Module" => svc.NextFreeModuleName(project, ModuleKind.StandardModule),
        "Class" => svc.NextFreeModuleName(project, ModuleKind.ClassModule),
        "UserControl" => svc.NextFreeModuleName(project, ModuleKind.UserControl),
        _ => svc.NextFreeModuleName(project, ModuleKind.PropertyPage),
    };

    private static Task AddNew(ProjectService svc, ProjectDefinition project, string kind, string name) => kind switch
    {
        "Form" => svc.AddNewForm(project, name, interactive: false),
        "Module" => svc.AddNewModule(project, name, ModuleKind.StandardModule, interactive: false),
        "Class" => svc.AddNewModule(project, name, ModuleKind.ClassModule, interactive: false),
        "UserControl" => svc.AddNewUserControl(project, name, interactive: false),
        _ => svc.AddNewPropertyPage(project, name, interactive: false),
    };

    // ── The Add commands: the name they pick ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Form", "Form1.frm")]
    [InlineData("Module", "Module1.bas")]
    [InlineData("Class", "Class1.cls")]
    [InlineData("UserControl", "UserControl1.ctl")]
    [InlineData("PropertyPage", "PropertyPage1.pag")]
    public async Task An_Add_command_passes_over_a_name_whose_file_is_already_in_the_folder(string kind, string inTheWay)
    {
        var svc = MakeService();
        var project = NewProject();
        var foreign = Stage(inTheWay);

        var name = NextFreeName(svc, project, kind);
        await AddNew(svc, project, kind, name);

        name.Should().Be(kind + "2", $"{inTheWay} is already in the project's folder, and the new file would replace it");
        File.ReadAllBytes(foreign).Should().Equal(Foreign(inTheWay));
    }

    [Theory]
    [InlineData("Form", "Form1.frx")]
    [InlineData("UserControl", "UserControl1.ctx")]
    [InlineData("PropertyPage", "PropertyPage1.pgx")]
    public async Task An_Add_command_passes_over_a_name_whose_binary_file_alone_is_in_the_folder(string kind, string inTheWay)
    {
        var svc = MakeService();
        var project = NewProject();
        var foreign = Stage(inTheWay);

        var name = NextFreeName(svc, project, kind);
        await AddNew(svc, project, kind, name);

        // Not replaced at once, because a new member has no binary data to write. But the new file would load
        // with a stranger's data beside it, and the first picture or list saved into it replaces that file.
        name.Should().Be(kind + "2", $"{inTheWay} is where the new {kind}1's binary data would be written");
        File.ReadAllBytes(foreign).Should().Equal(Foreign(inTheWay));
    }

    [Theory]
    [InlineData("Module", "Module1.frx")]
    [InlineData("Class", "Class1.frx")]
    public void A_module_has_no_binary_file_so_one_beside_its_name_is_not_in_the_way(string kind, string beside)
    {
        var svc = MakeService();
        var project = NewProject();
        Stage(beside);

        NextFreeName(svc, project, kind).Should().Be(kind + "1");
    }

    [Fact]
    public void A_file_the_project_file_names_for_a_member_that_did_not_load_is_in_the_way_though_it_is_missing()
    {
        // The member's line is kept so that a save writes it back (#729). A new Module1.bas written there would
        // be loaded in that member's place next time, and the project file would name Module1 twice.
        var svc = MakeService();
        var project = NewProject();
        project.PreservedItemLines.Add("Module=Module1; Module1.bas");

        svc.NextFreeModuleName(project, ModuleKind.StandardModule).Should().Be("Module2");
    }

    [Fact]
    public void A_project_with_no_folder_yet_is_given_none_by_asking_for_a_name()
    {
        // There is nothing in a folder that does not exist yet. Asking must not create one: a project's scratch
        // folder is chosen once, by the first write, and a refused add leaves it unchosen.
        var svc = MakeService();
        var project = new ProjectDefinition(VBProjectType.EXE, "Unsaved");

        svc.NextFreeModuleName(project, ModuleKind.StandardModule).Should().Be("Module1");
        project.WorkingDirectory.Should().BeNull();
    }

    // ── Asked for by name: refused, never renamed ────────────────────────────────────────────────────

    [Theory]
    [InlineData("Form", "Form1.frm")]
    [InlineData("Form", "Form1.frx")]
    [InlineData("Module", "Module1.bas")]
    [InlineData("Class", "Class1.cls")]
    [InlineData("UserControl", "UserControl1.ctl")]
    [InlineData("UserControl", "UserControl1.ctx")]
    [InlineData("PropertyPage", "PropertyPage1.pag")]
    [InlineData("PropertyPage", "PropertyPage1.pgx")]
    public async Task A_new_member_named_onto_a_file_already_in_the_folder_is_refused_and_the_file_kept(
        string kind, string inTheWay)
    {
        // add_file takes its name from the caller, so it cannot pick another: it has to refuse.
        var svc = MakeService();
        var project = NewProject();
        var foreign = Stage(inTheWay);

        Func<Task> add = () => AddNew(svc, project, kind, kind + "1");

        (await add.Should().ThrowAsync<FileInTheWayRefusal>()).Which.Path.Should().Be(foreign);
        File.ReadAllBytes(foreign).Should().Equal(Foreign(inTheWay));
        project.Forms.Should().BeEmpty();
        project.Modules.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_member_named_onto_a_file_the_project_file_names_is_refused()
    {
        var svc = MakeService();
        var project = NewProject();
        project.PreservedItemLines.Add("Module=Kept; Module1.bas");

        Func<Task> add = () => svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);

        (await add.Should().ThrowAsync<FileInTheWayRefusal>()).Which.Path.Should().Be(Path.Join(dir, "Module1.bas"));
        File.Exists(Path.Join(dir, "Module1.bas")).Should().BeFalse();
        project.Modules.Should().BeEmpty();
    }
}
