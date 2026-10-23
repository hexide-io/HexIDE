using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A save is the second of the three triggers that refresh the header the code window shows
/// (hexide-io/HexIDE#273 task 3.3a): the buffer follows the file, so after a save it holds the text that
/// save wrote.
/// </summary>
/// <remarks>
/// <b>This fires even when the model did not change, which is why it is a trigger of its own.</b> Every
/// write of a form goes through the serializer, and the serializer is a reproduction rather than a
/// byte-faithful copy of the file that was read — the <c>VERSION</c> line comes from a literal and the
/// block is rebuilt at a computed indent. A Save As or a first save to a name of the developer's choosing
/// also rewrites every companion citation in the header, from a name that did not exist when the file was
/// opened.
/// </remarks>
public class SaveRecordsTheHeaderTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-savehdr-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();

    public SaveRecordsTheHeaderTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// The real refresher over real stores and an empty dock — the slice is what is under test. A test that
    /// needs to assert the adoption was not REACHED passes a substitute instead, because for a document
    /// citing no companion the two renders are the same string and no effect distinguishes them.
    /// </summary>
    private ProjectService MakeService(IHeaderRefresher? refresher = null)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());

        // A member with no file asks where its first save goes: here, under the name it suggests.
        var windowManager = Substitute.For<IWindowManager>();
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(ci => Task.FromResult<string?>(
                Path.Join(dir, ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>().SuggestedFileName)));

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
            refresher ?? new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService()));
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Indented with four spaces and a trailing space after the form name -- neither of which the serializer
    // reproduces, so the render necessarily differs from the text that was read. That difference is the
    // whole reason a save is a trigger rather than a no-op.
    private const string OddlyFormatted =
        "VERSION 5.00\r\n"
      + "Begin VB.Form Form1 \r\n"
      + "    Begin VB.Label Label1\r\n"
      + "        Caption         =   \"Hi\"\r\n"
      + "    End\r\n"
      + "End\r\n"
      + "Attribute VB_Name = \"Form1\"\r\n"
      + "Option Explicit\r\n";

    [Fact]
    public async Task TheHeaderTheSaveWroteBecomesTheHeaderTheBufferShows()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var form = new FormDeserializer().Deserialize(project, OddlyFormatted, NullSink.Instance)!;
        form.AbsolutePath = Path.Join(dir, "Form1.frm");
        project.AddForm(form);
        var asRead = form.DesignerText!;

        (await MakeService().SaveForm(form, saveAs: false)).Should().BeTrue();

        var onDisk = await File.ReadAllTextAsync(form.AbsolutePath, TestContext.Current.CancellationToken);
        form.DesignerText.Should().NotBe(asRead, "the render is a reproduction, not the bytes that were read");
        form.DesignerText.Should().Be(onDisk[..^form.Code.Length]);
        FormCodeText.WholeFile(form).Should().Be(onDisk,
            "the invariant in one line: the buffer is the text of the file as it stands");
    }

    [Fact]
    public async Task ANewUserControlsBufferMatchesTheFileItsFirstSaveWrites()
    {
        // A new member has no file until it is first saved (#500), so its header is held from creation. Without
        // that, a brand-new .ctl opens with no header in the window while the file it is saved as has one -- and
        // every line number in it is then off by the height of that header.
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var service = MakeService();

        var module = await service.AddNewUserControl(project, "MyControl");
        module.FormPart!.DesignerText.Should().NotBeNull();
        var asCreated = FormCodeText.WholeFile(module);
        (await service.SaveModule(module, saveAs: false)).Should().BeTrue();

        var onDisk = await File.ReadAllTextAsync(module.AbsolutePath!, TestContext.Current.CancellationToken);
        asCreated.Should().Be(onDisk);
        FormCodeText.WholeFile(module).Should().Be(onDisk);
    }

    [Fact]
    public async Task AUserControlsHeaderFollowsASubsequentSaveToo()
    {
        // The .ctl/.pag branch of SaveModuleCore is a separate write with a separate slice -- the code it
        // pairs the designer half with is the MODULE's, not the FormPart's -- and nothing else exercises it
        // with a real refresher. A UserControl whose code changed and was saved must still compose to its
        // file, or the code window's line numbers drift from the ones a server reading the file reports.
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var service = MakeService();
        var module = await service.AddNewUserControl(project, "MyControl");

        module.UpdateCode("Option Explicit\r\nPublic Sub Spin()\r\nEnd Sub\r\n");
        (await service.SaveModule(module, saveAs: false)).Should().BeTrue();

        var onDisk = await File.ReadAllTextAsync(module.AbsolutePath!, TestContext.Current.CancellationToken);
        FormCodeText.WholeFile(module).Should().Be(onDisk);
        module.FormPart!.DesignerText.Should().NotContain("Public Sub Spin",
            "the slice takes the module's code off the render, so the code must not end up in the header");
    }

    [Fact]
    public void AWriteThatIsOnlyACopyDoesNotBecomeTheDocumentsHeader()
    {
        // Make packages every form into a temporary directory under a name built from the form's OWN name,
        // which is not always the name of its file -- so the header it renders there cites a companion the
        // real file does not. Adopting that would leave it on the model and in the open code window after
        // the temp directory had been deleted. The path repointing beside it is already undone in a finally;
        // this is the half that could not be, because the model has no second copy of the header.
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var form = new FormDeserializer().Deserialize(project, OddlyFormatted, NullSink.Instance)!;
        form.AbsolutePath = Path.Join(dir, "Form1.frm");
        project.AddForm(form);
        var asRead = form.DesignerText!;

        var copyPath = Path.Join(dir, "packaged", "Form1.frm");
        Directory.CreateDirectory(Path.GetDirectoryName(copyPath)!);
        MakeService().SerializeFormToFile(form, copyPath, adoptHeader: false).Should().BeTrue();

        File.Exists(copyPath).Should().BeTrue("the copy is still written -- only the adoption is skipped");
        form.DesignerText.Should().Be(asRead);
    }

    [Fact]
    public void AndTheSameWriteWithAdoptionOnDoesBecomeIt()
    {
        // The other side of the same parameter, so the test above cannot pass because the write silently
        // did nothing.
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var form = new FormDeserializer().Deserialize(project, OddlyFormatted, NullSink.Instance)!;
        form.AbsolutePath = Path.Join(dir, "Form1.frm");
        project.AddForm(form);
        var asRead = form.DesignerText!;

        MakeService().SerializeFormToFile(form, Path.Join(dir, "Form1.frm")).Should().BeTrue();

        form.DesignerText.Should().NotBe(asRead);
    }

    [Fact]
    public async Task AUserControlWrittenAsACopyDoesNotAdoptTheHeaderItWroteThere()
    {
        // The .ctl half of the same rule, asserted as a call rather than as an effect -- and deliberately.
        // A designer half's render depends on the file name only through its COMPANION citations, and a
        // UserControl carrying no blob cites none, so for the common case the packaged render and the real
        // one are the same string and no effect exists to observe. The rule still has to hold for the one
        // that does carry a blob, and what it turns on is whether the adoption is reached at all.
        var refresher = Substitute.For<IHeaderRefresher>();
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var service = MakeService(refresher);
        var module = await service.AddNewUserControl(project, "MyControl");
        module.AbsolutePath = Path.Join(dir, "MyControl.ctl");

        (await service.SaveModuleCore(module, saveAs: false)).Should().BeTrue();
        // Saved is the save's adoption since #273 task 4.4, which also hands the open buffer the qualifiers the
        // save rewrote; it calls ApplyHeader for the designer half.
        refresher.Received(1).Saved(DocumentIdentity.For(module), Arg.Is<string?>(header => header != null));

        refresher.ClearReceivedCalls();
        module.AbsolutePath = Path.Join(dir, "packaged-ctl", "MyControl.ctl");
        Directory.CreateDirectory(Path.GetDirectoryName(module.AbsolutePath)!);

        (await service.SaveModuleCore(module, saveAs: false, announceSave: false)).Should().BeTrue();

        File.Exists(module.AbsolutePath).Should().BeTrue("the copy is still written");
        refresher.DidNotReceive().Saved(Arg.Any<DocumentIdentity>(), Arg.Any<string?>());
        refresher.DidNotReceive().ApplyHeader(Arg.Any<FormDefinition>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ARefusedSaveLeavesTheHeaderAlone()
    {
        // The gate is the same one the write path uses, and it has to be: a buffer re-headed from a render
        // the save refused to write would show the developer a file that does not exist -- and the refusal
        // exists precisely because that render is wrong.
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { AbsolutePath = Path.Join(dir, "Test.vbp") };
        loaded.Add(project);
        var form = new FormDeserializer().Deserialize(project, OddlyFormatted, NullSink.Instance)!;
        form.AbsolutePath = Path.Join(dir, "Form1.frm");
        project.AddForm(form);
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        var asRead = form.DesignerText!;

        (await MakeService().SaveForm(form, saveAs: false)).Should().BeFalse();

        form.DesignerText.Should().Be(asRead);
        File.Exists(form.AbsolutePath).Should().BeFalse();
    }
}
