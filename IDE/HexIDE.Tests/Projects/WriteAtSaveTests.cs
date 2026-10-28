using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A new form or module gets its file when the project is saved, not when it is added, as VB6 does it.
///
/// <para>
/// Measured against the VB6 IDE (#489, <c>docs/vb6-fidelity-oracle/when-a-document-gets-a-file-and-what-a-dangling-startup-does.md</c>): no gesture that creates a document
/// writes a file, in an unsaved project or a saved one, and discarding an unsaved document leaves nothing behind.
/// HexIDE wrote each one at once, into the project's folder or, for a project never saved, a scratch folder under
/// <c>%TEMP%</c>. Its first Save Project then wrote the <c>.vbp</c> where the developer chose and left those members
/// where they were, so the project file reached back into <c>%TEMP%</c> for them
/// (<c>Module=Module1; ..\hexide_Project1_&lt;guid&gt;\Module1.bas</c>), and the next temp clean-up lost them
/// (hexide-io/HexIDE#500).
/// </para>
/// </summary>
public class WriteAtSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-writeatsave-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public WriteAtSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        // Every picker is answered with the chosen folder and the name it suggests, as a developer accepting
        // each suggestion there would.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));
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

    /// <summary>A project as File ▸ New Project leaves it: no file and no folder.</summary>
    private ProjectDefinition Unsaved()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        loaded.Add(project);
        return project;
    }

    private static Task<DocumentIdentity> AddNew(ProjectService svc, ProjectDefinition project, string kind, string name) =>
        kind switch
        {
            "Form" => svc.AddNewForm(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "Module" => svc.AddNewModule(project, name, ModuleKind.StandardModule, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "Class" => svc.AddNewModule(project, name, ModuleKind.ClassModule, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            "UserControl" => svc.AddNewUserControl(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
            _ => svc.AddNewPropertyPage(project, name, interactive: false).ContinueWith(t => DocumentIdentity.For(t.Result)),
        };

    [Fact]
    public async Task SavingPreservesFilesAtOldStagingNames()
    {
        var svc = MakeService();
        var project = Unsaved();
        await svc.AddNewForm(project, "Form1", interactive: false);
        await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        string[] names = ["Project1.vbp", "Form1.frm", "Module1.bas"];
        foreach (var name in names)
            File.WriteAllText(Path.Join(dir, name + ".tmp"), "user file");

        await svc.SaveProject(project, saveAs: false);

        foreach (var name in names)
        {
            File.Exists(Path.Join(dir, name)).Should().BeTrue();
            File.ReadAllText(Path.Join(dir, name + ".tmp")).Should().Be("user file");
        }
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("Class")]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task Adding_a_member_to_an_unsaved_project_writes_nothing(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull("a new document has no file until the project is saved");
        project.WorkingDirectory.Should().BeNull("Add chose no folder, because it wrote nothing");
        DocumentLookup.DocumentsOf(project).Should().Contain(added);
    }

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("UserControl")]
    public async Task Adding_a_member_to_a_saved_project_writes_nothing_in_its_folder(string kind)
    {
        var svc = MakeService();
        var project = Unsaved();
        project.AbsolutePath = Path.Join(dir, "Project1.vbp");

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull();
        Directory.GetFiles(dir).Should().BeEmpty("VB6 writes neither the new file nor the project file until it is saved");
    }

    [Theory]
    [InlineData("Form", false)]
    [InlineData("Module", false)]
    [InlineData("Class", false)]
    [InlineData("UserControl", false)]
    [InlineData("PropertyPage", false)]
    [InlineData("Form", true)]
    [InlineData("Module", true)]
    [InlineData("Class", true)]
    [InlineData("UserControl", true)]
    [InlineData("PropertyPage", true)]
    public async Task A_new_member_has_unsaved_changes_until_its_first_save(string kind, bool savedProject)
    {
        var svc = MakeService();
        var project = Unsaved();
        if (savedProject)
            await svc.SaveProject(project, saveAs: false);

        var added = await AddNew(svc, project, kind, kind + "1");

        added.AbsolutePath.Should().BeNull();
        bool HasUnsavedChanges() => added.Form is { } form
            ? svc.HasUnsavedChanges(form)
            : svc.HasUnsavedChanges(added.Module!);
        HasUnsavedChanges().Should().BeTrue("a new document has no file yet (#597)");

        await svc.SaveProject(project, saveAs: false);

        added.AbsolutePath.Should().NotBeNull();
        File.Exists(added.AbsolutePath).Should().BeTrue();
        HasUnsavedChanges().Should().BeFalse();
    }

    [Theory]
    [InlineData("Form", "VERSION 5.00\r\nBegin VB.Form Form1 ")]
    [InlineData("UserControl", "VERSION 5.00\r\nBegin VB.UserControl UserControl1 ")]
    [InlineData("PropertyPage", "VERSION 5.00\r\nBegin VB.PropertyPage PropertyPage1 ")]
    public async Task A_new_designer_document_holds_the_header_its_first_save_will_write(string kind, string opening)
    {
        // Stored at creation, which is the answer #885 asked for: the code window shows the file it will be, and a
        // later refresh has the header it replaces to compare against.
        var svc = MakeService();
        var project = Unsaved();

        var added = await AddNew(svc, project, kind, kind + "1");

        var designer = added.Module is { } module ? module.FormPart!.DesignerText : added.Form!.DesignerText;
        designer.Should().StartWith(opening);
    }

    [Fact]
    public async Task Saving_an_unsaved_project_puts_every_new_member_beside_its_project_file()
    {
        // The probe #500 records, as a regression test.
        var svc = MakeService();
        var project = Unsaved();
        var module = await svc.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);
        var form = await svc.AddNewForm(project, "Form1", interactive: false);

        await svc.SaveProject(project, saveAs: false);

        module.AbsolutePath.Should().Be(Path.Join(dir, "Module1.bas"));
        form.AbsolutePath.Should().Be(Path.Join(dir, "Form1.frm"));
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo(["Form1.frm", "Module1.bas", "Project1.vbp"]);
        var vbp = File.ReadAllText(Path.Join(dir, "Project1.vbp"));
        vbp.Should().Contain("Form=Form1.frm\r\n").And.Contain("Module=Module1; Module1.bas\r\n");
        vbp.Should().NotContain("..", "a member written beside the project file is named without leaving its folder");
        project.WorkingDirectory.Should().BeNull("neither Add nor the save chose a scratch folder");
    }

    [Fact]
    public async Task A_new_member_is_saved_with_the_header_it_was_created_with()
    {
        var svc = MakeService();
        var project = Unsaved();
        var form = await svc.AddNewForm(project, "Form1", interactive: false);
        var header = form.DesignerText;

        await svc.SaveProject(project, saveAs: false);

        File.ReadAllText(form.AbsolutePath!).Should().StartWith(header!);
    }
}
