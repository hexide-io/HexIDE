using HexIDE.Runtime.Serialization;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Sidecar;
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
        () => _projects, Substitute.For<HexIDE.Localization.ILocalizationService>());

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

    [Theory]
    [InlineData("module")]
    [InlineData("form")]
    [InlineData("carried")]
    public void Linked_sources_navigate_only_to_the_named_file(string kind)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-navigation-" + Guid.NewGuid());
        var real = Path.Combine(directory, "real");
        var link = Path.Combine(directory, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);
            var project = ProjectWith();
            var path = Path.Combine(link, "Source.bas");
            switch (kind)
            {
                case "module":
                    project.AddModule(new ModuleDefinition(project, "Source", ModuleKind.StandardModule) { AbsolutePath = path });
                    break;
                case "form":
                    project.AddForm(new FormDefinition(project, FormComponentClass.Instance, "Source") { AbsolutePath = path });
                    break;
                case "carried":
                    project.AddRelatedDocument(new RelatedDocumentDefinition(project, "Source.bas", path));
                    _dock.TryActivate(Arg.Any<Func<RelatedDocumentEditorViewModel, bool>>()).Returns(true);
                    break;
            }
            Sut().NavigateTo(new Uri(Path.Combine(real, "Source.bas")).AbsoluteUri, 1, 1).Should().BeTrue();
            Sut().NavigateTo(new Uri(Path.Combine(real, "Other.bas")).AbsoluteUri, 1, 1).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void Exact_navigation_matches_win_even_in_a_later_project()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-navigation-priority-" + Guid.NewGuid());
        var real = Path.Combine(directory, "real");
        var link = Path.Combine(directory, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);
            var first = ProjectWith(moduleName: "Linked", modulePath: Path.Combine(link, "Source.bas"));
            var second = ProjectWith(moduleName: "Exact", modulePath: Path.Combine(real, "Source.bas"));
            _projects.LoadedProjects.Returns([first, second]);
            CodeEditorViewModel Editor(ModuleDefinition module) => new CodeEditorViewModel(
                Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
                Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), Substitute.For<HexIDE.Lsp.ILspClient>(),
                Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
                Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
                Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
                Substitute.For<IRunScope>(), Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(module);
            using var editor = Editor(second.Modules.Single());
            using var linkedEditor = Editor(first.Modules.Single());
            Func<CodeEditorViewModel, bool>? selected = null;
            _dock.TryActivate(Arg.Do<Func<CodeEditorViewModel, bool>>(predicate => selected = predicate)).Returns(true);

            Sut().NavigateTo(new Uri(Path.Combine(real, "Source.bas")).AbsoluteUri, 1, 1).Should().BeTrue();

            selected.Should().NotBeNull();
            selected!(editor).Should().BeTrue("the exact match must beat the earlier linked spelling");
            selected(linkedEditor).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [AvaloniaFact]
    public void ACarriedDiagnosticOpensItsFileAndPlacesTheCaret()
    {
        var path = Path.Combine(Path.GetTempPath(), "hexide-1991-" + Guid.NewGuid() + ".md");
        File.WriteAllText(path, "first\nsecond line\n");
        try
        {
            var project = ProjectWith();
            var carried = new RelatedDocumentDefinition(project, "notes.md", path);
            project.AddRelatedDocument(carried);
            using var editor = new RelatedDocumentEditorViewModel(Substitute.For<HexIDE.Lsp.ILspClient>(),
                Substitute.For<IWindowManager>(), Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<HexIDE.IDE.ISettingsService>());
            editor.Initialize(carried);
            _dock.TryActivate(Arg.Any<Func<RelatedDocumentEditorViewModel, bool>>()).Returns(true);
            _dock.OpenDocuments.Returns([editor]);

            Sut().NavigateTo(new Uri(path).AbsoluteUri, 2, 4).Should().BeTrue();

            editor.CaretOffset.Should().Be(editor.Document.GetLineByNumber(2).Offset + 3);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ShowDocumentSelectsTheModuleRangeWithTheRequestedFocus(bool takeFocus)
    {
        var project = ProjectWith(moduleName: "Module1");
        var module = project.Modules.Single();
        module.UpdateCode("Sub Example()\n    Debug.Print 1\nEnd Sub");
        using var editor = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), Substitute.For<HexIDE.Lsp.ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), Substitute.For<HexIDE.Localization.ILocalizationService>());
        editor.Initialize(module);
        _dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>(), takeFocus).Returns(true);
        _dock.OpenDocuments.Returns([editor]);
        var line = editor.Document.Lines.First(l => editor.Document.GetText(l).Contains("Debug.Print"));
        var range = new HexIDE.Lsp.Messages.Range(new(line.LineNumber - 1, 4), new(line.LineNumber - 1, 9));

        Sut().ShowDocument(new("untitled:P/Module1.bas", TakeFocus: takeFocus, Selection: range)).Should().BeTrue();

        editor.CaretOffset.Should().Be(line.Offset + 4);
        editor.SelectionStart.Should().Be(line.Offset + 4);
        editor.SelectionLength.Should().Be(5);
        _dock.Received().TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>(), takeFocus);
        _dock.ClearReceivedCalls();
        Sut().ShowDocument(new("untitled:P/Module1.bas", External: true)).Should().BeFalse();
        Sut().ShowDocument(new("untitled:P/Missing.bas")).Should().BeFalse();
        _dock.DidNotReceive().TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>(), Arg.Any<bool>());
        _dock.DidNotReceive().OpenDocument(Arg.Any<BaseEditorWindowViewModel>(), Arg.Any<bool>());
    }

    // NavigateTo opens a module reached through a link (#5111), so the selection has to find the same editor: the
    // exact spelling first, as NavigateTo chose it, and only then the linked one.
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ShowDocumentSelectsTheRangeInAModuleReachedThroughALink(bool exactSpellingAlsoOpen)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-show-linked-" + Guid.NewGuid());
        var real = Path.Combine(directory, "real");
        var link = Path.Combine(directory, "link");
        Directory.CreateDirectory(real);
        try
        {
            Directory.CreateSymbolicLink(link, real);
            var linkedProject = ProjectWith(moduleName: "Module1", modulePath: Path.Combine(link, "Module1.bas"));
            var exactProject = ProjectWith(moduleName: "Module1", modulePath: Path.Combine(real, "Module1.bas"));
            _projects.LoadedProjects.Returns(exactSpellingAlsoOpen ? [linkedProject, exactProject] : [linkedProject]);
            CodeEditorViewModel Editor(ModuleDefinition module)
            {
                module.UpdateCode("Sub Example()\n    Debug.Print 1\nEnd Sub");
                return new CodeEditorViewModel(
                    Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
                    Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), Substitute.For<HexIDE.Lsp.ILspClient>(),
                    Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
                    Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
                    Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
                    Substitute.For<IRunScope>(), Substitute.For<HexIDE.Localization.ILocalizationService>()).Initialize(module);
            }
            using var linked = Editor(linkedProject.Modules.Single());
            using var exact = Editor(exactProject.Modules.Single());
            _dock.TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>(), Arg.Any<bool>()).Returns(true);
            _dock.OpenDocuments.Returns(exactSpellingAlsoOpen ? [linked, exact] : [linked]);
            var line = linked.Document.Lines.First(l => linked.Document.GetText(l).Contains("Debug.Print"));
            var range = new HexIDE.Lsp.Messages.Range(new(line.LineNumber - 1, 4), new(line.LineNumber - 1, 9));

            Sut().ShowDocument(new(new Uri(Path.Combine(real, "Module1.bas")).AbsoluteUri, Selection: range))
                .Should().BeTrue();

            var selected = exactSpellingAlsoOpen ? exact : linked;
            selected.SelectionStart.Should().Be(line.Offset + 4);
            selected.SelectionLength.Should().Be(5);
            if (exactSpellingAlsoOpen) linked.SelectionLength.Should().Be(0, "the exact spelling wins, as NavigateTo chose it");
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData("untitled:P/Module1.bas", "Module1")]
    [InlineData("untitled:P/Form1.frm", "Form1")]
    public void RenameRefusalNamesLoadedMembersWithoutOpeningThem(string uri, string name)
    {
        ProjectWith(moduleName: "Module1", formName: "Form1");
        Sut().DocumentNameForUri(uri).Should().Be(name);
        _dock.DidNotReceive().TryActivate(Arg.Any<Func<CodeEditorViewModel, bool>>());
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
