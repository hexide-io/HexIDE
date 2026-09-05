using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Which writes to disk count as a save, and which do not.
///
/// <para>
/// The signal is raised where the bytes are written rather than in an editor, and these tests are what
/// makes that choice worth its extra type. Saving a project writes every form and module without an
/// editor being involved at all — as do saving every project and the prompt shown when something closes
/// with unsaved work — so an editor-raised signal would be silent for the IDE's primary save gesture and
/// loud only for the one that is bound to a broken command.
/// </para>
/// </summary>
public class DocumentSavedAnnouncementTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "hexide-saved-" + Guid.NewGuid().ToString("N"));

    private readonly IEventBus _eventBus = Substitute.For<IEventBus>();
    private readonly List<DocumentSavedEvent> _announced = [];
    private readonly List<ProjectDefinition> _loaded = [];

    public DocumentSavedAnnouncementTests()
    {
        Directory.CreateDirectory(_dir);
        _eventBus.When(b => b.Publish(Arg.Any<DocumentSavedEvent>()))
            .Do(call => _announced.Add(call.Arg<DocumentSavedEvent>()));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => _loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => _loaded.Add(ci.Arg<ProjectDefinition>()));

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            Substitute.For<IWindowManager>(),
            _eventBus,
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    private ProjectDefinition AProjectWith(params string[] moduleNames)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P")
        {
            AbsolutePath = Path.Combine(_dir, "P.vbp"),
        };
        foreach (var name in moduleNames)
        {
            var module = new ModuleDefinition(project, name, ModuleKind.StandardModule)
            {
                AbsolutePath = Path.Combine(_dir, name + ".bas"),
            };
            module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
            project.AddModule(module);
        }
        _loaded.Add(project);
        return project;
    }

    // ── What counts ───────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SavingAModuleAnnouncesIt()
    {
        var project = AProjectWith("Module1");

        await MakeService().SaveModule(project.Modules[0], false);

        _announced.Should().ContainSingle()
            .Which.Module.Should().BeSameAs(project.Modules[0]);
    }

    [Fact]
    public async Task SavingTheWholeProjectAnnouncesEveryModule()
    {
        // The gesture an editor-raised signal would miss entirely: File > Save Project and the toolbar
        // button write every document with no editor involved.
        var project = AProjectWith("Module1", "Module2", "Module3");

        await MakeService().SaveProject(project, false);

        _announced.Select(e => e.Module!.Name).Should().BeEquivalentTo(["Module1", "Module2", "Module3"]);
    }

    [Fact]
    public async Task EachSaveIsAnnouncedOnceNotOncePerCall()
    {
        var project = AProjectWith("Module1");
        var service = MakeService();

        await service.SaveModule(project.Modules[0], false);
        await service.SaveModule(project.Modules[0], false);

        _announced.Should().HaveCount(2, "two saves happened, and each is a separate fact about the file");
    }

    // ── What does not ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AModuleTheIdeRefusedToWriteIsNotAnnounced()
    {
        // A refusal is the opposite of a save. Announcing one would have a server re-read a file that
        // still holds the previous content and conclude the developer's edit had no effect.
        //
        // A REFUSAL, not an exception, and the distinction is why this test looks the way it does. My
        // first version pointed the module at an unwritable path, which throws — so the announcement was
        // never reached whether or not it was guarded, and the test passed against a version that
        // announced unconditionally. This drives the real refusal: a UserControl whose designer half
        // cannot be reproduced faithfully, which returns false without throwing.
        var project = new ProjectDefinition(VBProjectType.EXE, "P")
        {
            AbsolutePath = Path.Combine(_dir, "P.vbp"),
        };
        var control = new ModuleDefinition(project, "UserControl1", ModuleKind.UserControl)
        {
            AbsolutePath = Path.Combine(_dir, "UserControl1.ctl"),
        };
        var designerHalf = new FormDefinition(
            project, [new ComponentInstance(FormComponentClass.Instance, "UserControl1")], "");
        designerHalf.MarkUnfaithfulToSave(
            UnfaithfulSaveCause.NestedContainers, "it nests controls that HexIDE would flatten on save");
        control.UpdateFormPart(designerHalf);
        project.AddModule(control);
        _loaded.Add(project);

        var written = await MakeService().SaveModuleCore(control, saveAs: false);

        written.Should().BeFalse("the fixture is meant to be refused, not merely to fail");
        _announced.Should().BeEmpty();
    }

    [Fact]
    public async Task WritingACopyElsewhereIsNotAnnounced()
    {
        // Make EXE writes every module into a temporary directory through this same code and then puts
        // every AbsolutePath back. Announcing those would report saves the developer never made, and point
        // a server at files that are about to be deleted.
        //
        // Driven through the core directly because Make EXE itself needs a published standalone runtime
        // and refuses long before it reaches this loop. That is also why the flag exists rather than a
        // rule about temporary paths: SaveProjectToDirectory writes elsewhere too and IS a real save.
        var project = AProjectWith("Module1");

        var written = await MakeService().SaveModuleCore(
            project.Modules[0], saveAs: false, announceSave: false);

        written.Should().BeTrue("the file is still written — only the announcement is suppressed");
        _announced.Should().BeEmpty();
    }

    [Fact]
    public async Task SavingTheProjectToANewDirectoryStillAnnounces()
    {
        // The sibling of the Make-EXE path, and the reason the suppression is a parameter rather than a
        // rule about temporary directories: both write every module somewhere else through the same code,
        // and this one is a real save. It repoints each AbsolutePath PERMANENTLY, so those files are now
        // the project's files and a server should know they were written.
        var project = AProjectWith("Module1", "Module2");
        var destination = Path.Combine(_dir, "elsewhere");
        Directory.CreateDirectory(destination);

        await MakeService().SaveProjectToDirectory(project, destination);

        _announced.Select(e => e.Module!.Name).Should().BeEquivalentTo(["Module1", "Module2"]);
    }

    [Fact]
    public void TheEventCarriesTheDefinitionRatherThanAPath()
    {
        // A path cannot be turned back into the identity a server knows the document by: that is fixed
        // when the document is opened and held by the editor's session, and for a VB6 document it bears no
        // relation to where the file lives. Subscribers match on the definition, as they do for every
        // other event about their document.
        var project = AProjectWith("Module1");

        var e = new DocumentSavedEvent(null, project.Modules[0]);

        e.Module.Should().BeSameAs(project.Modules[0]);
        e.Form.Should().BeNull();
    }
}
