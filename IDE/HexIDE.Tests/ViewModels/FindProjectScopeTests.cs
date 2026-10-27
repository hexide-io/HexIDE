using System.Collections.Generic;
using System.IO;
using System.Text;
using HexIDE.Events;
using HexIDE.Projects;
using System.Threading.Tasks;
using AvaloniaEdit.Document;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.ViewModels;

public partial class FindReplaceViewModelTests
{
    private readonly IProjectManager _findProjects = Substitute.For<IProjectManager>();
    private readonly IEditorService _findEditors = Substitute.For<IEditorService>();
    private ProjectDefinition? _selectedFindProject;
    private readonly List<BaseEditorWindowViewModel> _findOpen = [];
    private readonly List<ProjectDefinition> _findLoaded = [];

    private FindProjectDocuments ProjectDocuments() => new(_documentDockService, _findProjects,
        _findEditors, () => _selectedFindProject);

    private FindReplaceViewModel ProjectFind()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        localization.GetString("Str.FindReplace.Msg.ScopeCurrentProject").Returns("Current Project ({0})");
        _documentDockService.OpenDocuments.Returns(_findOpen);
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo,
            MessageBoxIcon.Question).Returns(MessageBoxResult.Yes);
        return new(_windowManager, _documentDockService, localization, ProjectDocuments()) { SearchText = "needle" };
    }

    private ProjectDefinition SearchProject(string name)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, name);
        _findLoaded.Add(project);
        _findProjects.LoadedProjects.Returns(_findLoaded);
        _findProjects.StartupProject ??= project;
        return project;
    }

    private ModuleDefinition SearchModule(ProjectDefinition project, string name, string text)
    {
        var module = new ModuleDefinition(project, name, ModuleKind.StandardModule);
        module.UpdateCode(text);
        project.AddModule(module);
        _findEditors.When(service => service.EditCode(module)).Do(_ =>
        {
            var opened = _findOpen.OfType<CodeEditorViewModel>().FirstOrDefault(editor => editor.ModuleDefinition == module);
            if (opened is null)
            {
                opened = CreateMockEditor("").Initialize(module);
                _findOpen.Add(opened);
            }
            SetActiveEditor(opened);
        });
        return module;
    }

    [AvaloniaFact]
    public async Task ProjectScope_UsesUnsavedBufferWithoutFlushing()
    {
        var project = SearchProject("P");
        var module = SearchModule(project, "M", "deleted");
        using var editor = CreateMockEditor("").Initialize(module);
        editor.Document.Text += "needle";
        _findOpen.Add(editor);
        using var active = CreateMockEditor("").Initialize(SearchModule(project, "Active", "nothing"));
        _findOpen.Add(active);
        SetActiveEditor(active);
        var sut = ProjectFind();

        await sut.FindNextAsync();

        editor.Document.GetText(editor.SelectionStart, editor.SelectionLength).Should().Be("needle");
        module.Code.Should().Be("deleted");
        _findEditors.DidNotReceive().EditCode(Arg.Any<ModuleDefinition>());
    }

    [AvaloniaFact]
    public async Task ProjectScope_DoesNotFindTextDeletedFromUnsavedBuffer()
    {
        var project = SearchProject("P");
        var module = SearchModule(project, "M", "needle");
        using var editor = CreateMockEditor("").Initialize(module);
        editor.Document.Text = editor.Document.Text.Replace("needle", "gone");
        _findOpen.Add(editor);
        SetActiveEditor(editor);
        var closed = SearchModule(project, "Closed", "needle");
        var sut = ProjectFind();

        await sut.FindNextAsync();

        editor.SelectionLength.Should().Be(0);
        _findEditors.Received(1).EditCode(closed);
        var opened = _findOpen.OfType<CodeEditorViewModel>().Single(other => other.ModuleDefinition == closed);
        opened.SelectionLength.Should().Be(6);
        module.Code.Should().Be("needle");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectScope_OpensClosedModuleAtMatch(bool up)
    {
        var project = SearchProject("P");
        var activeModule = SearchModule(project, "Active", "nothing");
        var closed = SearchModule(project, "Closed", "needle middle needle");
        using var active = CreateMockEditor("").Initialize(activeModule);
        _findOpen.Add(active);
        SetActiveEditor(active);
        var sut = ProjectFind();
        sut.Direction = up ? FindDirection.Up : FindDirection.Down;

        await sut.FindNextAsync();

        var opened = _findOpen.OfType<CodeEditorViewModel>().Single(editor => editor.ModuleDefinition == closed);
        opened.Document.GetText(opened.SelectionStart, opened.SelectionLength).Should().Be("needle");
        opened.SelectionStart.Should().Be(up ? opened.Document.Text.LastIndexOf("needle", StringComparison.Ordinal)
            : opened.Document.Text.IndexOf("needle", StringComparison.Ordinal));
        _findEditors.Received(1).EditCode(closed);
    }

    [AvaloniaFact]
    public async Task ProjectScope_DoesNotOpenNonMatchingFilesOrSearchTheirHeaders()
    {
        var project = SearchProject("P");
        var module = SearchModule(project, "needle", "nothing");
        var sut = ProjectFind();

        await sut.FindNextAsync();

        _findOpen.Should().BeEmpty();
        _findEditors.DidNotReceive().EditCode(module);
        await _windowManager.Received().MessageBox(Arg.Is<string>(text => text.Contains("ProtectedOnly")),
            Arg.Any<string>(), MessageBoxButtons.Ok, MessageBoxIcon.Information);
    }

    [AvaloniaFact]
    public async Task ProjectScope_TargetsActiveDocumentsProjectRatherThanStartup()
    {
        var startup = SearchProject("Startup");
        SearchModule(startup, "Excluded", "needle");
        var focused = SearchProject("Focused");
        var module = SearchModule(focused, "Active", "nothing");
        using var editor = CreateMockEditor("").Initialize(module);
        _findOpen.Add(editor);
        SetActiveEditor(editor);
        _selectedFindProject = startup;
        var sut = ProjectFind();

        await sut.FindNextAsync();

        sut.ScopeItems.Should().Contain("Current Project (Focused)");
        _findEditors.DidNotReceive().EditCode(Arg.Any<ModuleDefinition>());
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectScope_TargetsFormOrDesignerThenExplorerThenStartup(bool designerActive)
    {
        var startup = SearchProject("Startup");
        var selected = SearchProject("Selected");
        var focused = SearchProject("Focused");
        var form = new FormDefinition(focused, FormComponentClass.Instance, "Form1");
        using BaseEditorWindowViewModel editor = designerActive
            ? new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<IProjectService>(),
                _findEditors, _windowManager, Substitute.For<ISettingsService>(),
                Substitute.For<ILocalizationService>()).Initialize(form)
            : CreateMockEditor("").Initialize(form);
        SetActiveEditor(editor);
        _selectedFindProject = selected;
        var documents = ProjectDocuments();
        documents.CurrentProject.Should().Be(focused);
        _documentDockService.ActiveDocument.Returns((BaseEditorWindowViewModel?)null);
        documents.CurrentProject.Should().Be(selected);
        _selectedFindProject = null;
        documents.CurrentProject.Should().Be(startup);
    }

    [AvaloniaFact]
    public async Task AllProjects_SearchesSiblingAndAppearsOnlyForGroup()
    {
        var first = SearchProject("First");
        SearchModule(first, "Empty", "nothing");
        var second = SearchProject("Second");
        var module = SearchModule(second, "Hit", "needle");
        var sut = ProjectFind();
        sut.ScopeItems.Should().HaveCount(5);
        _findProjects.CurrentGroup.Returns(new ProjectGroupDefinition("Group"));
        sut.RefreshScopes();
        sut.ScopeItems.Should().HaveCount(6);
        sut.Scope = FindScope.AllProjects;

        await sut.FindNextAsync();

        _findEditors.Received().EditCode(module);
        _findProjects.CurrentGroup.Returns((ProjectGroupDefinition?)null);
        sut.RefreshScopes();
        sut.Scope.Should().Be(FindScope.CurrentProject);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CrossFileReplaceAll_ConfirmsBeforeAnyOpenOrEdit(bool accept)
    {
        var project = SearchProject("P");
        var first = SearchModule(project, "First", "needle");
        var second = SearchModule(project, "Second", "needle");
        var sut = ProjectFind();
        sut.ReplaceText = "changed";
        var answer = new TaskCompletionSource<MessageBoxResult>();
        _windowManager.MessageBox(Arg.Any<string>(), Arg.Any<string>(), MessageBoxButtons.YesNo,
            MessageBoxIcon.Question).Returns(answer.Task);

        var replacing = sut.ReplaceAllAsync();

        _findOpen.Should().BeEmpty();
        first.Code.Should().Be("needle");
        second.Code.Should().Be("needle");
        _ = _windowManager.Received(1).MessageBox(Arg.Any<string>(), Arg.Any<string>(),
            MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        answer.SetResult(accept ? MessageBoxResult.Yes : MessageBoxResult.No);
        await replacing;
        _findOpen.Should().HaveCount(accept ? 2 : 0);
        if (accept)
            _findOpen.OfType<CodeEditorViewModel>().Should().OnlyContain(editor => editor.Document.Text.Contains("changed"));
    }

    [AvaloniaTheory]
    [InlineData(FindScope.CurrentProcedure)]
    [InlineData(FindScope.CurrentModule)]
    public async Task StructuralScope_UsesServerRangeAndFallsBackToDocument(FindScope scope)
    {
        var document = new SearchRangeDocument("needle\nneedle\nneedle");
        SetActiveEditor(document);
        var sut = ProjectFind();
        sut.Scope = scope;
        var range = new LspRange(new Position(1, 0), new Position(1, 6));
        document.Symbols = [new DocumentSymbol("Container", SymbolKind.Namespace,
            new(new(0, 0), new(2, 6)), range, Children: [new("Member",
                scope == FindScope.CurrentProcedure ? SymbolKind.Function : SymbolKind.Module, range, range)])];
        document.CaretOffset = 8;

        await sut.FindNextAsync();

        document.SelectionStart.Should().Be(7);
        document.SelectionLength.Should().Be(6);
        document.Symbols = [];
        document.CaretOffset = 0;
        document.SelectionLength = 0;
        await sut.FindNextAsync();
        document.SelectionStart.Should().Be(0);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedText_KeepsOriginalRangeAcrossFindAndReplacement(bool pattern)
    {
        var document = new SearchRangeDocument("needle needle needle needle");
        SetActiveEditor(document);
        document.SelectionStart = 7;
        document.SelectionLength = 13;
        document.CaretOffset = 7;
        var sut = ProjectFind();
        sut.Scope = FindScope.SelectedText;
        sut.UsePatternMatching = pattern;
        sut.ReplaceText = "x";

        await sut.FindNextAsync();
        await sut.FindNextAsync();
        await sut.ReplaceAllAsync();

        document.Document.Text.Should().Be("needle x x needle");
    }

    [AvaloniaFact]
    public async Task SelectedText_ReplaceOneDoesNotFindTextMovedInFromOutsideTheSelection()
    {
        var document = new SearchRangeDocument("needle needle needle");
        SetActiveEditor(document);
        document.SelectionStart = 7;
        document.SelectionLength = 7;
        document.CaretOffset = 7;
        var sut = ProjectFind();
        sut.Scope = FindScope.SelectedText;
        sut.ReplaceText = "";

        await sut.FindNextAsync();
        await sut.ReplaceOneAsync();

        document.Document.Text.Should().Be("needle  needle");
        document.SelectionLength.Should().Be(0);
    }

    [AvaloniaFact]
    public async Task ProjectScope_OpensAClosedFormAtItsCodeMatch()
    {
        var project = SearchProject("P");
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        form.UpdateCode("needle");
        project.AddForm(form);
        _findEditors.When(service => service.EditCode(form)).Do(_ =>
        {
            var editor = CreateMockEditor("").Initialize(form);
            _findOpen.Add(editor);
            SetActiveEditor(editor);
        });
        var sut = ProjectFind();

        await sut.FindNextAsync();

        var opened = _findOpen.OfType<CodeEditorViewModel>().Single();
        opened.FormDefinition.Should().Be(form);
        opened.Document.GetText(opened.SelectionStart, opened.SelectionLength).Should().Be("needle");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectScope_OpensClosedCarriedFileWithTheSameOffsetsAsItsSnapshot(bool bom)
    {
        var project = SearchProject("P");
        var path = Path.Combine(AppContext.BaseDirectory, "find-" + Guid.NewGuid().ToString("N") + ".txt");
        try
        {
            File.WriteAllText(path, "needle", new UTF8Encoding(bom));
            var related = new RelatedDocumentDefinition(project, "notes.txt", path);
            project.AddRelatedDocument(related);
            _findEditors.When(service => service.EditRelatedDocument(related)).Do(_ =>
            {
                var editor = CreateCarriedFileEditor("").Initialize(related);
                _findOpen.Add(editor);
                SetActiveEditor(editor);
            });
            var sut = ProjectFind();

            await sut.FindNextAsync();

            var opened = _findOpen.OfType<RelatedDocumentEditorViewModel>().Single();
            opened.SelectionStart.Should().Be(0);
            opened.Document.GetText(opened.SelectionStart, opened.SelectionLength).Should().Be("needle");
        }
        finally { File.Delete(path); }
    }

    private sealed class SearchRangeDocument(string text) : BaseEditorWindowViewModel, ISearchableDocument
    {
        public TextDocument Document { get; } = new(text);
        public int CaretOffset { get; set; }
        public int SelectionStart { get; set; }
        public int SelectionLength { get; set; }
        public DocumentSymbol[] Symbols { get; set; } = [];
        public Task<DocumentSymbol[]> RequestSearchSymbolsAsync() => Task.FromResult(Symbols);
        public override object? Icon => null;
        protected override string ComputeTitle() => "Test";
    }
}
