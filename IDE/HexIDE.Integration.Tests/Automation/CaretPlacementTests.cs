using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Automation;

/// <summary>
/// open_file's line and column put the caret where the code window numbers them (#700). Placing it took a buffer offset
/// the caller had to count out.
/// </summary>
public class CaretPlacementTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public CaretPlacementTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
        lsp.RequestCompletionAsync(Arg.Any<string>(), Arg.Any<Position>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<CompletionItem>());
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    // The module's Attribute VB_Name header is line 1, as the code window numbers it, so Foo's blank body is line 5, and the text's closing line break makes 7.
    private const string Body = "Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    \r\nEnd Sub\r\n";

    private CodeEditorViewModel Model()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.UpdateCode(Body);
        vm.Initialize(module);
        return vm;
    }

    private TextEditor Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    [AvaloniaFact]
    public void The_caret_goes_to_the_line_and_column_the_code_window_shows()
    {
        var vm = Model();
        var editor = Show(vm);

        var (placed, refusal) = CaretPlacement.Place(vm, 5, 5);
        Dispatcher.UIThread.RunJobs();

        refusal.Should().BeNull();
        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(5);
        var offset = vm.Document.Text.IndexOf("    \r\nEnd Sub", StringComparison.Ordinal) + 4;
        placed.Should().Be($"The caret is at line 5, column 5: offset {offset} in the text get_file_content returns.");
        editor.CaretOffset.Should().Be(offset);
    }

    [AvaloniaFact]
    public void A_caret_placed_before_the_editor_is_shown_is_where_the_editor_opens()
    {
        // open_file places it in the same dispatcher turn as it opens the document, before its view exists.
        var vm = Model();
        CaretPlacement.Place(vm, 5, 5).Refusal.Should().BeNull();

        var editor = Show(vm);

        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(5);
    }

    [AvaloniaFact]
    public async Task Typing_after_placing_the_caret_lands_there()
    {
        var vm = Model();
        var editor = Show(vm);

        CaretPlacement.Place(vm, 5, 5).Refusal.Should().BeNull();
        Dispatcher.UIThread.RunJobs();
        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "Beep");
        Dispatcher.UIThread.RunJobs();

        typed.Success.Should().BeTrue(typed.Error);
        editor.Document.Text.Should().Contain("Foo()\r\n    Beep\r\nEnd Sub");
    }

    [AvaloniaFact]
    public void One_past_a_line_s_last_character_is_its_end()
    {
        var vm = Model();
        var editor = Show(vm);

        CaretPlacement.Place(vm, 4, "Private Sub Foo()".Length + 1).Refusal.Should().BeNull();
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.Caret.Line.Should().Be(4);
        editor.TextArea.Caret.Column.Should().Be(18);
    }

    [AvaloniaTheory]
    [InlineData(0, 1, "it has 7 lines, so line 0 is not one of them; the caret was left where it was")]
    [InlineData(8, 1, "it has 7 lines, so line 8 is not one of them; the caret was left where it was")]
    [InlineData(5, 0, "line 5 has 4 characters, so column 0 is not on it (column 5 is its end); the caret was left where it was")]
    [InlineData(5, 6, "line 5 has 4 characters, so column 6 is not on it (column 5 is its end); the caret was left where it was")]
    public void A_place_off_the_text_is_refused_and_the_caret_stays(int line, int column, string expected)
    {
        var vm = Model();
        var editor = Show(vm);
        CaretPlacement.Place(vm, 2, 2).Refusal.Should().BeNull();
        Dispatcher.UIThread.RunJobs();

        var (placed, refusal) = CaretPlacement.Place(vm, line, column);
        Dispatcher.UIThread.RunJobs();

        placed.Should().BeNull();
        refusal.Should().Be(expected);
        editor.TextArea.Caret.Line.Should().Be(2);
        editor.TextArea.Caret.Column.Should().Be(2);
    }
}
