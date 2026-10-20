using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Folding;
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

    // The module's Attribute VB_Name header is line 1, as the code window numbers it, so Foo's blank body is line 5, and
    // the text's closing line break makes 7.
    private const string Body = "Option Explicit\r\n\r\nPrivate Sub Foo()\r\n    \r\nEnd Sub\r\n";

    private CodeEditorViewModel Model(string code = Body, ModuleKind kind = ModuleKind.StandardModule)
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
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", kind);
        module.UpdateCode(code);
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

    private static FoldingManager Folds(TextEditor editor) =>
        (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;

    [AvaloniaFact]
    public void The_caret_goes_to_the_line_and_column_the_code_window_shows()
    {
        var vm = Model();
        var editor = Show(vm);

        CaretPlacement.Place(vm, 5, 5).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(5);
        var offset = vm.Document.Text.IndexOf("    \r\nEnd Sub", StringComparison.Ordinal) + 4;
        editor.CaretOffset.Should().Be(offset);
        CaretPlacement.Where(vm, 5, 5).Should()
            .Be($"The caret is at line 5, column 5: offset {offset} in the text get_file_content returns.");
    }

    [AvaloniaFact]
    public void A_caret_placed_before_the_editor_is_shown_is_where_the_editor_opens()
    {
        // open_file places it in the same dispatcher turn as it opens the document, before its view exists.
        var vm = Model();
        CaretPlacement.Place(vm, 5, 5).Should().BeTrue();

        var editor = Show(vm);

        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(5);
    }

    [AvaloniaFact]
    public void A_caret_placed_far_down_before_the_editor_is_shown_is_scrolled_to()
    {
        // ScrollToLine does nothing before the view is laid out, so a document opened at line 180 showed its top.
        var filler = string.Concat(Enumerable.Range(1, 200).Select(i => $"' filler {i}\r\n"));
        var vm = Model("Option Explicit\r\n" + filler);
        CaretPlacement.Place(vm, 180, 1).Should().BeTrue();

        var editor = Show(vm);
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.TextView.VisualLines.Select(l => l.FirstDocumentLine.LineNumber).Should().Contain(180);
    }

    [AvaloniaFact]
    public void A_caret_placed_in_the_header_before_the_editor_is_shown_leaves_the_header_open()
    {
        // The header's fold is made after the caret is placed, and folded, it hid the caret.
        var vm = Model("Option Explicit\r\n", ModuleKind.ClassModule);
        vm.Document.LineCount.Should().BeGreaterThan(4, "a class's header runs to several lines");
        CaretPlacement.Place(vm, 3, 2).Should().BeTrue();

        var editor = Show(vm);

        Folds(editor).AllFoldings.Should().NotBeEmpty();
        Folds(editor).GetFoldingsContaining(editor.CaretOffset).Should().OnlyContain(f => !f.IsFolded);
        editor.TextArea.Caret.Line.Should().Be(3);
        editor.TextArea.Caret.Column.Should().Be(2);
    }

    [AvaloniaFact]
    public void A_header_folded_by_hand_over_the_caret_stays_folded_when_the_server_s_folds_arrive()
    {
        // Only a header fold made after the caret was placed is left open for it; one the developer folded is theirs.
        var answer = new TaskCompletionSource<FoldingRange[]>();
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(answer.Task);
        var vm = Model("Option Explicit\r\n", ModuleKind.ClassModule);
        CaretPlacement.Place(vm, 3, 2).Should().BeTrue();
        var editor = Show(vm);
        var header = Folds(editor).AllFoldings.First();
        header.IsFolded.Should().BeFalse();

        header.IsFolded = true;
        answer.SetResult([]);
        Dispatcher.UIThread.RunJobs();

        Folds(editor).AllFoldings.First().IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void A_header_shown_with_the_caret_outside_it_stays_folded()
    {
        var vm = Model("Option Explicit\r\n", ModuleKind.ClassModule);

        var editor = Show(vm);

        Folds(editor).AllFoldings.First().IsFolded.Should().BeTrue();
    }

    private const string WithRun =
        "Option Explicit\r\n\r\nPublic Sub Init()\r\nAttribute Init.VB_Description = \"Sets it up\"\r\n    x = 1\r\nEnd Sub\r\n";

    [AvaloniaFact]
    public void A_caret_put_at_a_folded_member_run_s_end_opens_the_run_and_stays()
    {
        // Folded, the run's end is no place a caret can be seen, and the window moved it to the declaration's end.
        var vm = Model(WithRun);
        var editor = Show(vm);
        var run = Folds(editor).AllFoldings.Single(f => f.StartOffset > 0);
        run.IsFolded.Should().BeTrue("a member's run is shown folded");
        var column = "Attribute Init.VB_Description = \"Sets it up\"".Length + 1;

        CaretPlacement.Place(vm, 5, column).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        run.IsFolded.Should().BeFalse();
        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(column);
        CaretPlacement.Where(vm, 5, column).Should().NotContain("as asked");
    }

    [AvaloniaFact]
    public async Task Typing_after_placing_the_caret_lands_there()
    {
        var vm = Model();
        var editor = Show(vm);

        CaretPlacement.Place(vm, 5, 5).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "Beep");
        Dispatcher.UIThread.RunJobs();

        typed.Success.Should().BeTrue(typed.Error);
        editor.Document.Text.Should().Contain("Foo()\r\n    Beep\r\nEnd Sub");
    }

    [AvaloniaFact]
    public void One_past_a_line_s_last_character_is_its_end_and_a_tab_is_one_column()
    {
        var vm = Model("Option Explicit\r\n\tx = 1\r\n");
        var editor = Show(vm);

        CaretPlacement.Place(vm, 3, 2).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf("\tx", StringComparison.Ordinal) + 1);

        CaretPlacement.Place(vm, 3, "\tx = 1".Length + 1).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.Caret.Line.Should().Be(3);
        editor.TextArea.Caret.Column.Should().Be(7);
    }

    [AvaloniaTheory]
    [InlineData(0, 1, "line 0 is not one of its lines, numbered 1 to 7")]
    [InlineData(8, 1, "line 8 is not one of its lines, numbered 1 to 7")]
    [InlineData(5, 0, "column 0 is not on line 5, whose columns are numbered 1 to 5, its end")]
    [InlineData(5, 6, "column 6 is not on line 5, whose columns are numbered 1 to 5, its end")]
    public void A_place_off_the_text_is_refused_and_the_caret_stays(int line, int column, string expected)
    {
        var vm = Model();
        var editor = Show(vm);
        CaretPlacement.Place(vm, 2, 2).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        CaretPlacement.Refusal(vm.Document.Text, line, column).Should().Be(expected);
        CaretPlacement.Place(vm, line, column).Should().BeFalse();
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.Caret.Line.Should().Be(2);
        editor.TextArea.Caret.Column.Should().Be(2);
    }

    [AvaloniaFact]
    public void A_column_between_the_halves_of_one_character_is_refused()
    {
        var text = "Option Explicit\r\n' x" + char.ConvertFromUtf32(0x1F600) + "y\r\n";

        CaretPlacement.Refusal(text, 2, 5).Should()
            .Be("column 5 of line 2 falls between the two halves of one character; it is column 4 or 6");
        CaretPlacement.Refusal(text, 2, 4).Should().BeNull();
        CaretPlacement.Refusal(text, 2, 6).Should().BeNull();
    }

    [AvaloniaFact]
    public void A_caret_that_is_not_where_it_was_asked_for_says_so()
    {
        var vm = Model();
        CaretPlacement.Place(vm, 2, 1).Should().BeTrue();

        CaretPlacement.Where(vm, 5, 5).Should().Be($"The caret is at line 2, column 1: offset {vm.CaretOffset} in the text "
            + "get_file_content returns. That is not line 5, column 5, as asked: the editor moved it, so type_text would "
            + "write there.");
        CaretPlacement.Where(vm, 2, 3).Should().EndWith("That is not line 2, column 3, as asked: the editor moved it, so "
            + "type_text would write there.");
    }
}
