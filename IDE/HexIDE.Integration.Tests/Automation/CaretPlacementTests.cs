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

    private CodeEditorViewModel Model(string code = Body, ModuleKind kind = ModuleKind.StandardModule, bool form = false)
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
        if (form) vm.Initialize(FormDefinition.NewForm(new ProjectDefinition(VBProjectType.EXE, "P"),
            HexIDE.Runtime.Components.FormComponentClass.Instance, "Form1"));
        else vm.Initialize(module);
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
    public async Task Typing_after_selecting_a_new_event_lands_in_its_body()
    {
        var vm = Model(form: true);
        var editor = Show(vm);

        var view = (CodeEditorView)windows[^1].Content!;
        view.FindControl<ComboBox>("ObjectCombo")!.SetCurrentValue(ComboBox.SelectedItemProperty, "Form");
        Dispatcher.UIThread.RunJobs();
        vm.SelectedObject.Should().Be("Form");
        view.FindControl<ComboBox>("ProcedureCombo")!.SetCurrentValue(ComboBox.SelectedItemProperty, "Click");
        Dispatcher.UIThread.RunJobs();
        vm.ProcedureNames.Should().Contain("Click");
        vm.SelectedProcedure.Should().Be("Click");
        vm.Document.Text.Should().Contain("Private Sub Form_Click()");
        editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf("Private Sub Form_Click()\r\n", StringComparison.Ordinal)
            + "Private Sub Form_Click()\r\n".Length);
        lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            lsp, new PublishDiagnosticsParams(vm.GetDocumentUriPublic(), []));
        Dispatcher.UIThread.RunJobs();
        await lsp.Received().RequestDocumentSymbolsAsync(vm.GetDocumentUriPublic());
        var typed = await UiAutomationDriver.TypeKeysAsync(editor, "Debug.Print \"loaded\"");
        Dispatcher.UIThread.RunJobs();

        typed.Success.Should().BeTrue(typed.Error);
        editor.Document.Text.ReplaceLineEndings("\n").Should()
            .Contain("Private Sub Form_Click()\nDebug.Print \"loaded\"\nEnd Sub");
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

    // ── A caret where type_text would be refused (#2578) ─────────────────────────────────────────────────────────

    // The refusal used to be the first a caller heard of it, after open_file put the caret in a form's header and said
    // only where it was.
    [AvaloniaFact]
    public void A_caret_in_a_form_s_header_says_so_and_names_the_nearest_place_that_can_be_edited()
    {
        var vm = Model(code: "", form: true);
        CaretPlacement.Place(vm, 2, 1).Should().BeTrue();
        vm.IsReadOnlyRegion(vm.CaretOffset, 0).Should().BeTrue("line 2 is the header's");

        var past = vm.PastReadOnlyRegion(vm.CaretOffset);
        var to = vm.Document.GetLocation(past);
        CaretPlacement.Where(vm, 2, 1).Should().Be($"The caret is at line 2, column 1: offset {vm.CaretOffset} in the text "
            + "get_file_content returns. That is in the file's header, which only the IDE changes, so type_text there would "
            + $"be refused. The nearest place that can be edited is line {to.Line}, column {to.Column}: offset {past}.");
        vm.IsReadOnlyRegion(past, 0).Should().BeFalse("the place it names is one type_text writes at");
    }

    [AvaloniaFact]
    public void A_caret_in_a_member_s_Attribute_lines_names_the_member()
    {
        var vm = Model("Public Sub Foo()\r\nAttribute Foo.VB_Description = \"Says hello\"\r\n    \r\nEnd Sub\r\n");
        CaretPlacement.Place(vm, 3, 1).Should().BeTrue();

        CaretPlacement.Where(vm, 3, 1).Should().Contain("That is in the Attribute lines describing Foo, which only the IDE "
            + "changes, so type_text there would be refused. The nearest place that can be edited is line 4, column 1:");
    }

    [AvaloniaFact]
    public void A_caret_where_type_text_writes_adds_nothing()
    {
        var vm = Model();
        CaretPlacement.Place(vm, 5, 5).Should().BeTrue();

        CaretPlacement.Where(vm, 5, 5).Should().Be($"The caret is at line 5, column 5: offset {vm.CaretOffset} in the text "
            + "get_file_content returns.");
    }

    // ── A carried file's editor, which open_file refused a line for until #2403 ─────────────────────────────────

    private const string Carried = "int once(int x);\nint twice(int x)\n{\n    return once(once(x));\n}\n";

    private (RelatedDocumentEditorViewModel Model, TextEditor Editor) ShowCarried(RelatedDocumentEditorViewModel? vm = null)
    {
        vm ??= CarriedModel();
        var view = new RelatedDocumentEditorView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, view.FindControl<TextEditor>("TextEditor")!);
    }

    private RelatedDocumentEditorViewModel CarriedModel()
    {
        var vm = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
        vm.Document.Text = Carried;
        return vm;
    }

    [AvaloniaFact]
    public void A_carried_file_s_caret_goes_where_its_editor_numbers_the_line_and_column()
    {
        var (vm, editor) = ShowCarried();

        CaretPlacement.Place(vm, 4, 12).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        editor.TextArea.Caret.Line.Should().Be(4);
        editor.TextArea.Caret.Column.Should().Be(12);
        CaretPlacement.Where(vm, 4, 12).Should().Be($"The caret is at line 4, column 12: offset {vm.CaretOffset} in the "
            + "text get_file_content returns.");
    }

    [AvaloniaFact]
    public void A_carried_file_opened_for_a_caret_already_placed_takes_it_once_laid_out()
    {
        // open_file places the caret in the model it has just opened, before its view exists.
        var vm = CarriedModel();
        CaretPlacement.Place(vm, 4, 12).Should().BeTrue();
        var placed = vm.CaretOffset;

        var (_, editor) = ShowCarried(vm);

        editor.CaretOffset.Should().Be(placed);
        vm.CaretOffset.Should().Be(placed, "the editor's own caret, at 0 when its document arrived, must not overwrite it");
    }

    [AvaloniaFact]
    public void A_fold_ending_where_a_carried_file_s_caret_goes_is_opened_onto_it()
    {
        // AvaloniaEdit opens a fold a caret lands strictly inside by itself. One the caret lands at the end of, it leaves
        // closed, with the caret's line hidden in it, as the code window found at a member run's end (#700 review).
        var (vm, editor) = ShowCarried();
        var folds = editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single().FoldingManager;
        var body = folds.CreateFolding(Carried.IndexOf('{'), Carried.LastIndexOf('}') + 1);
        body.IsFolded = true;

        // Line 5, column 2: just after the closing brace, the fold's end.
        CaretPlacement.Place(vm, 5, 2).Should().BeTrue();
        Dispatcher.UIThread.RunJobs();

        vm.CaretOffset.Should().Be(body.EndOffset);
        body.IsFolded.Should().BeFalse("the line the caret is on would sit unseen in the fold");
        editor.TextArea.Caret.Line.Should().Be(5);
        editor.TextArea.Caret.Column.Should().Be(2);
    }

    [AvaloniaFact]
    public void Set_property_reads_the_carried_file_caret_after_the_view_clamps_it()
    {
        var vm = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>());
        vm.Document.Text = "0123456789";
        var view = new RelatedDocumentEditorView { DataContext = vm };
        var window = new Window { Width = 800, Height = 600, Content = view };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var editor = view.FindControl<TextEditor>("TextEditor")!;

        var outcome = UiAutomationDriver.Interact(editor, "set_property", "CaretOffset=99999");

        editor.CaretOffset.Should().Be(10);
        vm.CaretOffset.Should().Be(10);
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("set RelatedDocumentEditorViewModel.CaretOffset: sent 99999, now 10");
        UiAutomationDriver.Interact(editor, "set_property", "CaretOffset=5").Detail.Should()
            .Be("set RelatedDocumentEditorViewModel.CaretOffset = 5");
    }

    [AvaloniaFact]
    public void Set_property_reads_the_code_editor_caret_after_the_view_clamps_it()
    {
        var vm = Model();
        // Replace the generated header too: the buffer has exactly ten characters on either checkout's line endings.
        vm.Document.Text = "0123456789";
        var editor = Show(vm);

        var outcome = UiAutomationDriver.Interact(editor, "set_property", "CaretOffset=99999");

        vm.CaretOffset.Should().Be(10);
        editor.CaretOffset.Should().Be(10);
        outcome.Success.Should().BeTrue(outcome.Error);
        outcome.Detail.Should().Be("set CodeEditorViewModel.CaretOffset: sent 99999, now 10");
    }

    [AvaloniaTheory]
    [InlineData(99999, 10)]
    [InlineData(-1, 0)]
    [InlineData(5, 5)]
    public void The_code_editor_and_model_agree_on_a_clamped_caret(int requested, int expected)
    {
        var vm = Model();
        vm.Document.Text = "0123456789";
        var editor = Show(vm);
        // Exercise both a moved caret and a clamp onto its existing position (which raises no caret event).
        foreach (var initial in new[] { 0, 7, expected })
        {
            vm.CaretOffset = initial;
            vm.CaretOffset = requested;
            Dispatcher.UIThread.RunJobs();

            editor.CaretOffset.Should().Be(expected);
            vm.CaretOffset.Should().Be(expected);
        }
    }

    [AvaloniaFact]
    public void Set_property_reads_an_ordinary_setter_in_invariant_culture_and_explains_a_refusal()
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
            var target = new Border { DataContext = new ClampingModel() };
            UiAutomationDriver.Interact(target, "set_property", "Width=99.5").Detail.Should()
                .Be("set ClampingModel.Width: sent 99.5, now 10.5");
            UiAutomationDriver.Interact(target, "set_property", "Width=5.5").Detail.Should()
                .Be("set ClampingModel.Width = 5.5");
            var refused = UiAutomationDriver.Interact(target, "set_property", "Refused=1");
            refused.Success.Should().BeFalse();
            refused.Error.Should().Be("set_property 'Refused=1' was refused: locked");
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = previous; }
    }

    private sealed class ClampingModel
    {
        private decimal width;
        public decimal Width { get => width; set => width = Math.Min(value, 10.5m); }
        public int Refused { get => 0; set => throw new InvalidOperationException("locked"); }
    }
}
