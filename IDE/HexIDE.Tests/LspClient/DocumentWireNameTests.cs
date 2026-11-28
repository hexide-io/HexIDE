using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Sidecar;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// What a document is called on the wire (#273 task 2.1). The scheme HexIDE invented for itself,
/// <c>vb6://</c>, is gone: a server that had never heard of it could not open the document, resolve
/// anything relative to it, or say anything useful about it.
///
/// <para>
/// The <c>untitled:</c> spellings asserted here are the ones actually measured against five
/// externally-authored language servers in <see cref="UntitledDocumentNamesTests"/>, including the
/// percent-encoded form. These tests pin the converter to that measurement; those tests establish that a
/// real server accepts it.
/// </para>
/// </summary>
public class DocumentWireNameTests
{
    private static ProjectDefinition Project(string name = "Project1") => TestHelpers.CreateProject(name);

    private static ModuleDefinition Module(
        ProjectDefinition project, string name, ModuleKind kind = ModuleKind.StandardModule,
        string? path = null)
    {
        var module = new ModuleDefinition(project, name, kind) { AbsolutePath = path };
        project.AddModule(module);
        return module;
    }

    [Fact]
    public void AModuleWithNoFileIsNamedUntitledUnderItsProject()
    {
        var project = Project();
        DocumentWireName.For(Module(project, "Module1"))
            .Should().Be("untitled:Project1/Module1.bas");
    }

    [Theory]
    [InlineData(ModuleKind.StandardModule, ".bas")]
    [InlineData(ModuleKind.ClassModule, ".cls")]
    [InlineData(ModuleKind.UserControl, ".ctl")]
    [InlineData(ModuleKind.PropertyPage, ".pag")]
    public void TheExtensionComesFromTheKind_BecauseThatIsWhatRoutingReads(ModuleKind kind, string extension)
    {
        var project = Project();
        DocumentWireName.For(Module(project, "Thing", kind))
            .Should().Be($"untitled:Project1/Thing{extension}");
    }

    [Fact]
    public void ThereIsNoLeadingSlash()
    {
        // The two spellings normalise differently, so one is chosen and never mixed. Asserted as its own
        // case because it is invisible in a test that only compares whole strings it also wrote.
        DocumentWireName.For(Module(Project(), "Module1"))
            .Should().StartWith("untitled:Project1/")
            .And.NotStartWith("untitled:/");
    }

    [Fact]
    public void ANonAsciiNameIsPercentEncoded_WhichIsWhyItIsNotInterpolated()
    {
        // Measured, not stylistic: texlab silently DROPS a notification whose URI is not strictly valid —
        // no response, no error, nothing on stderr — and then never answers a request naming that
        // document, while the connection stays up and answers about later ones (#486). The same server in
        // the same process answers the encoded form. See UntitledDocumentNamesTests.
        var project = Project("Prøjekt");
        DocumentWireName.For(Module(project, "Модуль"))
            .Should().Be("untitled:Pr%C3%B8jekt/%D0%9C%D0%BE%D0%B4%D1%83%D0%BB%D1%8C.bas");
    }

    [Fact]
    public void ADocumentWithAFileIsNamedByThatFile()
    {
        var path = OperatingSystem.IsWindows() ? @"C:\src\proj\Module1.bas" : "/src/proj/Module1.bas";
        DocumentWireName.For(Module(Project(), "Module1", path: path))
            .Should().Be(LspDocumentUri.ForFile(path))
            .And.StartWith("file:///");
    }

    [Fact]
    public void ASavedDocumentWhoseFileIsDeletedKeepsItsFileName()
    {
        // untitled: means "no file yet", not "file missing" — the protocol maintainers' own guidance. This
        // falls out of reading the model rather than probing the filesystem, and is the reason not to probe.
        var path = OperatingSystem.IsWindows()
            ? @"C:\definitely\not\here\Gone.bas"
            : "/definitely/not/here/Gone.bas";
        File.Exists(path).Should().BeFalse("the premise of this test");

        DocumentWireName.For(Module(Project(), "Gone", path: path))
            .Should().StartWith("file:///");
    }

    [Fact]
    public void TwoProjectsInAGroupEachHoldingAModule1AreNamedApart()
    {
        // The collision the vb6:// scheme could not express: it named both vb6://module/Module1.
        DocumentWireName.For(Module(Project("Alpha"), "Module1"))
            .Should().NotBe(DocumentWireName.For(Module(Project("Beta"), "Module1")));
    }

    [Fact]
    public async Task AModuleInAProjectNotYetSaved()
    {
        // lsp-client's scenario of that name. A module added to a project that has never been saved has no file until
        // the project is saved, as in VB6 (#500), so it is named untitled:, and Add chose no folder for it. (A workspace
        // query also chooses none: an unsaved project has no root.)
        var service = new ProjectService(            () => throw new InvalidOperationException("no new-project dialog here"),
            Substitute.For<IWindowManager>(), Substitute.For<IEventBus>(), Substitute.For<IProjectManager>(),
            Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create());
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
        try
        {
            var module = await service.AddNewModule(project, "Module1", ModuleKind.StandardModule, interactive: false);

            project.AbsolutePath.Should().BeNull("the premise of this test");
            module.AbsolutePath.Should().BeNull();
            project.WorkingDirectory.Should().BeNull();
            DocumentWireName.For(module).Should().Be("untitled:Project1/Module1.bas");
        }
        finally
        {
            if (project.WorkingDirectory is { } scratch)
                try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }
}
