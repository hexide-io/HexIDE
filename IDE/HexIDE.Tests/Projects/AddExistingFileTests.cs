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
/// Add File adopts a file that already exists, rather than authoring one — so the thing worth proving is
/// that an adopted member is indistinguishable from one that arrived in the <c>.vbp</c>. A module adopted
/// through a shortcut path would look right in the Project Explorer and then be rewritten on the next save,
/// which is the shape of hexide-io/HexIDE#245.
/// </summary>
public class AddExistingFileTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-addfile-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public AddExistingFileTests() => Directory.CreateDirectory(dir);

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
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    private ProjectDefinition NewProject()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test")
        {
            AbsolutePath = Path.Join(dir, "Test.vbp"),
        };
        loaded.Add(project);
        return project;
    }

    private string Stage(string fileName, string content)
    {
        var path = Path.Join(dir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private const string ModuleFile =
        "Attribute VB_Name = \"Helpers\"\r\n"
      + "Option Explicit\r\n"
      + "\r\n"
      + "Public Sub Greet()\r\n"
      + "    Debug.Print \"hi\"\r\n"
      + "End Sub\r\n";

    [Fact]
    public async Task AnAdoptedModuleTakesItsNameFromVbNameNotTheFilename()
    {
        // The file is Utils.bas but declares itself Helpers. VB_Name is the identity code qualifies calls
        // through, so taking the filename would silently rename the module — a rename that only surfaces
        // later, as something failing to resolve.
        var path = Stage("Utils.bas", ModuleFile);
        var project = NewProject();

        var module = await MakeService().AddExistingModule(project, path, ModuleKind.StandardModule);

        module.Name.Should().Be("Helpers");
        module.AbsolutePath.Should().Be(path, "the file joins where it lies; nothing is copied or moved");
    }

    [Fact]
    public async Task AnAdoptedModuleHoldsItsBodyWithoutTheHeader()
    {
        var path = Stage("Utils.bas", ModuleFile);
        var project = NewProject();

        var module = await MakeService().AddExistingModule(project, path, ModuleKind.StandardModule);

        module.Code.Should().Contain("Public Sub Greet()")
            .And.NotContain("Attribute VB_Name",
                "the editor shows the body; leaving the header in Code emits it twice on the next save");
    }

    [Fact]
    public async Task SavingAnAdoptedModuleLeavesItByteIdentical()
    {
        // The prize. Adoption reads through the same path project load uses precisely so that the preserved
        // header survives — get that wrong and a file the developer merely ADDED comes back rewritten.
        var path = Stage("Utils.bas", ModuleFile);
        var before = await File.ReadAllTextAsync(path);
        var project = NewProject();
        var service = MakeService();

        var module = await service.AddExistingModule(project, path, ModuleKind.StandardModule);
        await service.SaveModule(module, saveAs: false);

        (await File.ReadAllTextAsync(path)).Should().Be(before);
    }

    [Fact]
    public async Task AnAdoptedRelatedDocumentNeverEntersTheModuleCollection()
    {
        // The structural guarantee. Everything that could damage a non-code file — the interpreter, the
        // extension-based rename, the Attribute header writer — iterates Modules. Absence from that
        // collection is what makes the damage unreachable, rather than a guard someone must remember.
        var path = Stage("README.md", "# Notes\r\n");
        var project = NewProject();

        var document = await MakeService().AddExistingRelatedDocument(project, path);

        document.Name.Should().Be("README.md");
        document.AbsolutePath.Should().Be(path);
        project.RelatedDocuments.Should().ContainSingle();
        project.Modules.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAdoptedRelatedDocumentIsWrittenAsRelatedDocNotAsAPreservedLine()
    {
        // OriginalItemLine exists to stop HexIDE rewriting a line it merely REINTERPRETED on read. A
        // document joining the project now has no such line, and suppressing the modern key here would
        // leave the .vbp with no record of the file at all.
        var path = Stage("README.md", "# Notes\r\n");
        var project = NewProject();

        var document = await MakeService().AddExistingRelatedDocument(project, path);

        document.OriginalItemLine.Should().BeNull();
    }

    [Fact]
    public async Task AdoptingAFileDoesNotRewriteIt()
    {
        // Adding is not saving. VB6 marks the project dirty and writes nothing until the developer says so,
        // and a related document is never rewritten by HexIDE at all.
        var path = Stage("README.md", "# Notes\r\n");
        var before = await File.ReadAllTextAsync(path);
        var project = NewProject();

        await MakeService().AddExistingRelatedDocument(project, path);

        (await File.ReadAllTextAsync(path)).Should().Be(before);
        File.Exists(project.AbsolutePath).Should().BeFalse(
            "the .vbp is written when the developer saves, not as a side effect of adding");
    }

    [Fact]
    public async Task AnAdoptedClassKeepsItsHeaderAttributes()
    {
        const string cls =
            "VERSION 1.0 CLASS\r\n"
          + "BEGIN\r\n"
          + "  MultiUse = -1  'True\r\n"
          + "END\r\n"
          + "Attribute VB_Name = \"Widget\"\r\n"
          + "Attribute VB_GlobalNameSpace = False\r\n"
          + "Attribute VB_Creatable = True\r\n"
          + "Attribute VB_PredeclaredId = False\r\n"
          + "Attribute VB_Exposed = False\r\n"
          + "Option Explicit\r\n";
        var path = Stage("Widget.cls", cls);
        var before = await File.ReadAllTextAsync(path);
        var project = NewProject();
        var service = MakeService();

        var module = await service.AddExistingModule(project, path, ModuleKind.ClassModule);
        await service.SaveModule(module, saveAs: false);

        module.Kind.Should().Be(ModuleKind.ClassModule);
        (await File.ReadAllTextAsync(path)).Should().Be(before,
            "VB_Creatable and VB_PredeclaredId are not reconstructible from the model, so they must survive "
          + "adoption verbatim");
    }

    [Fact]
    public async Task AFormThatWillNotParseAddsNothing()
    {
        // A half-added form is worse than a refused one: the tree shows a node, the .vbp gains a Form= line,
        // and the file behind it was never understood.
        var path = Stage("Broken.frm", "this is not a form\r\n");
        var project = NewProject();

        var form = await MakeService().AddExistingForm(project, path);

        form.Should().BeNull();
        project.Forms.Should().BeEmpty();
    }

    [Fact]
    public async Task AnAdoptedFormJoinsTheProject()
    {
        const string frm =
            "VERSION 5.00\r\n"
          + "Begin VB.Form Form1 \r\n"
          + "   Caption         =   \"Form1\"\r\n"
          + "   ClientHeight    =   3000\r\n"
          + "   ClientWidth     =   4000\r\n"
          + "End\r\n"
          + "Attribute VB_Name = \"Form1\"\r\n"
          + "Option Explicit\r\n";
        var path = Stage("Form1.frm", frm);
        var project = NewProject();

        var form = await MakeService().AddExistingForm(project, path);

        form.Should().NotBeNull();
        form!.AbsolutePath.Should().Be(path);
        project.Forms.Should().ContainSingle();
    }
}
