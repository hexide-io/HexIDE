using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.IDE;

/// <summary>
/// Resolving a URI a language server handed back to a document this project holds.
///
/// <para>
/// <b>This is the inbound half of go-to-definition and it did not exist.</b> The client asked for
/// definitions, received <c>Location</c>s, and dropped every one that named another file —
/// <c>NavigateToUri</c> logged at debug and returned. The justification in the code was a statement about
/// the BUNDLED server ("currently only returns symbols from the same file"), which stops being true the
/// moment a foreign server answers properly, with nothing here changing and no error anywhere.
/// </para>
/// </summary>
public class EditorServiceNavigationTests
{
    private readonly IDocumentDockService _dock = Substitute.For<IDocumentDockService>();
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();

    public EditorServiceNavigationTests()
    {
        // Reporting a document as already open keeps these tests on the resolution logic: nothing has to
        // build a CodeEditorViewModel, which needs eleven services of its own.
        _dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>()).Returns(true);
        _dock.OpenDocuments.Returns([]);
    }

    private EditorService Sut() => new(
        _dock,
        () => throw new InvalidOperationException("no editor should be constructed in these tests"),
        () => throw new InvalidOperationException("no editor should be constructed in these tests"),
        () => throw new InvalidOperationException("no editor should be constructed in these tests"),
        () => throw new InvalidOperationException("no editor should be constructed in these tests"),
        () => _projects);

    private ProjectDefinition ProjectWith(
        string? moduleName = null, string? modulePath = null, string? formName = null)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        if (moduleName is not null)
        {
            var module = new ModuleDefinition(project, moduleName, ModuleKind.StandardModule);
            if (modulePath is not null) module.AbsolutePath = modulePath;
            project.AddModule(module);
        }
        if (formName is not null) project.AddForm(new FormDefinition(project, FormComponentClass.Instance, formName));

        _projects.LoadedProjects.Returns([project]);
        return project;
    }

    [Fact]
    public void TheUntitledUriTheIdeMintsResolvesToItsModule()
    {
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("untitled:P/Module1.bas", 1, 1).Should().BeTrue();

        _dock.Received().TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>());
    }

    [Fact]
    public void AFileUriResolvesToTheDocumentThatLivesThere()
    {
        // What a server that reads from disk answers with, and since #273 the shape HexIDE sends too for
        // any document that has a file. A document has more than one spelling only while it has no file.
        var path = OperatingSystem.IsWindows() ? @"C:\proj\Module1.bas" : "/proj/Module1.bas";
        ProjectWith(moduleName: "Module1", modulePath: path);

        var uri = new Uri(path).AbsoluteUri;

        Sut().NavigateTo(uri, 1, 1).Should().BeTrue();
    }

    [WindowsOnlyFact]
    public void AFileUriWhoseDriveLetterTheServerNormalisedStillResolves()
    {
        // THE case #236 measured, arriving on a path #236's fix never reached. A conformant server answered
        // `file:///c:/…` to our `file:///C:/…`, and an ordinal compare reads that as a different document —
        // so the definition is found, returned, and silently discarded.
        ProjectWith(moduleName: "Module1", modulePath: @"C:\proj\Module1.bas");

        Sut().NavigateTo("file:///c:/proj/Module1.bas", 1, 1).Should().BeTrue(
            "a server is under no obligation to echo a URI back byte for byte");
    }

    [Fact]
    public void AFormWithNoFileIsFoundByItsUntitledUri()
    {
        ProjectWith(formName: "Form1");

        Sut().NavigateTo("untitled:P/Form1.frm", 1, 1).Should().BeTrue();
    }

    [Fact]
    public void AnUntitledUriIsMatchedCaseInsensitivelyBecauseVb6NamesAre()
    {
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("untitled:p/MODULE1.bas", 1, 1).Should().BeTrue(
            "both segments are VB6 names, and VB6 compares names without regard to case");
    }

    [Fact]
    public void AnUntitledUriNamingAnotherProjectsDocumentIsRefused()
    {
        // The collision the retired vb6:// scheme could not express at all: it named a Module1 in every
        // project vb6://module/Module1, so a group's two of them were one document on the wire.
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("untitled:Elsewhere/Module1.bas", 1, 1).Should().BeFalse();
    }

    [Fact]
    public void AnUntitledUriWithTheWrongExtensionIsRefused()
    {
        // The extension is what routes a document to a server, so it is part of the name rather than
        // decoration on it.
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("untitled:P/Module1.cls", 1, 1).Should().BeFalse();
    }

    [Fact]
    public void AUriNamingNothingLoadedIsRefusedRatherThanGuessedAt()
    {
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("untitled:P/SomewhereElse.bas", 1, 1).Should().BeFalse();

        _dock.DidNotReceive().TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>());
        _dock.DidNotReceive().OpenDocument(Arg.Any<BaseEditorWindowViewModel>());
    }

    [Fact]
    public void AnEmptyUriOpensNothing()
    {
        ProjectWith(moduleName: "Module1");

        Sut().NavigateTo("", 1, 1).Should().BeFalse();
        Sut().NavigateTo("   ", 1, 1).Should().BeFalse();
    }

    [Fact]
    public void EveryLoadedProjectIsSearched_NotJustTheStartupOne()
    {
        // A group's members live in different directories, and a definition can perfectly well be in a
        // sibling project. Searching only the startup project would make cross-project navigation fail in
        // exactly the configuration a group exists to support (hexide-io/HexIDE#261).
        var first = new ProjectDefinition(VBProjectType.EXE, "First");
        first.AddModule(new ModuleDefinition(first, "OnlyInFirst", ModuleKind.StandardModule));

        var second = new ProjectDefinition(VBProjectType.EXE, "Second");
        second.AddModule(new ModuleDefinition(second, "OnlyInSecond", ModuleKind.StandardModule));

        _projects.LoadedProjects.Returns([first, second]);

        Sut().NavigateTo("untitled:Second/OnlyInSecond.bas", 1, 1).Should().BeTrue();
    }
}
