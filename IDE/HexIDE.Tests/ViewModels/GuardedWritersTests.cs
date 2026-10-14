using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The writers that reach a code window's buffer without passing through typing, each held to its row of the
/// design record's writer policy (hexide-io/HexIDE#273 task 3.9). Formatting has its own file,
/// <see cref="GuardedFormattingTests"/>.
/// </summary>
/// <remarks>
/// Every one of these used to write the document directly, so none met the read-only section provider that
/// protects typing. Each test here was checked by removing the guard it covers and watching it fail.
/// </remarks>
public class GuardedWritersTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public GuardedWritersTests()
    {
        localization.GetString("Str.CodeEditor.Msg.RenameTouchesReadOnly").Returns("rename refused");
    }

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Dim Caption As String\r\n" +
        "    Caption = Command1.Caption\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private CodeEditorViewModel NewEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            windowManager, Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), localization);
        made.Add(vm);
        return vm;
    }

    private CodeEditorViewModel OpenForm() =>
        NewEditor().Initialize(new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!);

    /// <summary>
    /// The same form, held read-only as a whole: marked as one whose .frx content HexIDE cannot re-emit,
    /// before the window opens, as the deserializer marks it.
    /// </summary>
    private CodeEditorViewModel OpenFormHeldReadOnly()
    {
        var form = new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!;
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.UnreproducibleBinaryContent,
            "it references companion binary content HexIDE cannot re-emit (0 of 1 blob(s) reached the model)");
        return NewEditor().Initialize(form);
    }

    private CodeEditorViewModel OpenClass()
    {
        var module = TestHelpers.CreateModule(name: "Order", kind: ModuleKind.ClassModule);
        module.UpdateCode(ClassCode);
        return NewEditor().Initialize(module);
    }

    private static int HeaderEnd(CodeEditorViewModel vm) =>
        vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal);

    /// <summary>
    /// What the bundled server's rename sends: a whole-word, case-insensitive edit at every occurrence
    /// outside strings and comments, header included.
    /// </summary>
    private static List<TextEdit> LexicalRename(TextDocument document, string word, string newName)
    {
        var edits = new List<TextEdit>();
        foreach (var line in document.Lines)
        {
            var text = document.GetText(line);
            for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0;
                 at = text.IndexOf(word, at + word.Length, StringComparison.OrdinalIgnoreCase))
            {
                var left = at == 0 || !char.IsLetterOrDigit(text[at - 1]) && text[at - 1] != '_';
                var end = at + word.Length;
                var right = end == text.Length || !char.IsLetterOrDigit(text[end]) && text[end] != '_';
                var inString = text[..at].Count(c => c == '"') % 2 == 1;
                if (left && right && !inString)
                    edits.Add(new TextEdit(
                        new HexIDE.Lsp.Messages.Range(new Position(line.LineNumber - 1, at), new Position(line.LineNumber - 1, end)),
                        newName));
            }
        }
        return edits;
    }

    // ── Server rename: refused as a whole if it touches the header ─────────────────────────────────────

    [AvaloniaFact]
    public void RenamingALocalThatAlsoNamesADesignerPropertyIsRefusedAsAWhole()
    {
        // The case that makes the refusal common rather than rare: a local called Caption shares its name
        // with a property in the designer block, and the server's rename is lexical.
        var vm = OpenForm();
        var before = vm.Document.Text;
        var edits = LexicalRename(vm.Document, "Caption", "Title");
        edits.Should().Contain(e => e.Range.Start.Line < 7, "the fixture must actually reach the header");

        var refusal = vm.ApplyRename(edits, "Caption");

        refusal.Should().Be("rename refused");
        vm.Document.Text.Should().Be(before, "a rename applied in part leaves one name meaning two things");
        vm.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ARenameInTheCodeAloneIsAppliedAsOneUndoStep()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var edits = LexicalRename(vm.Document, "Enabled", "Visible");

        vm.ApplyRename(edits, "Enabled").Should().BeNull();

        vm.Document.Text.Should().Be(before.Replace("Command1.Enabled", "Command1.Visible"));
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void RenamingAProcedureThatHasADescription()
    {
        // The code-editor delta's scenario: the attribute line names the procedure's new name. The member's
        // own qualifier is the one write into a region a rename may make.
        var vm = OpenClass();
        var edits = LexicalRename(vm.Document, "Total", "GrandTotal");

        vm.ApplyRename(edits, "Total").Should().BeNull();

        vm.BufferBody.Should().Be(ClassCode.Replace("Total", "GrandTotal"));
        vm.Document.Text.Should().Contain("Attribute GrandTotal.VB_Description = \"The order total\"");
    }

    [AvaloniaFact]
    public void ARenameFromAReadOnlyLineIsRefusedBeforeANameIsAskedFor()
    {
        // The bundled server answers a rename from inside the header with nothing (#273 task 3.10), which the
        // code window would otherwise show as nothing: no message, and no rename.
        var form = OpenForm();
        var text = form.Document.Text;

        form.RenameRefusalAt(text.IndexOf("Caption", StringComparison.Ordinal) + 2, "Caption").Should().Be("rename refused");
        form.RenameRefusalAt(text.IndexOf("Dim Caption", StringComparison.Ordinal) + 6, "Caption")
            .Should().BeNull("the layout's Caption lines set a property; they do not declare this local");
        form.RenameRefusalAt(text.IndexOf("Option Explicit", StringComparison.Ordinal), "Option")
            .Should().BeNull("the first line of code starts where the header ends, and is the developer's");

        var cls = OpenClass();
        var attribute = cls.Document.Text.IndexOf("Attribute Total.", StringComparison.Ordinal);
        cls.RenameRefusalAt(attribute + "Attribute ".Length + 1, "Total")
            .Should().Be("rename refused", "a member is renamed from its declaration, and the qualifier follows");
        cls.RenameRefusalAt(cls.Document.Text.IndexOf("Function Total", StringComparison.Ordinal) + "Function ".Length, "Total")
            .Should().BeNull();
    }

    [AvaloniaFact]
    public void RenamingAControlFromItsCodeIsRefused()
    {
        // The designer block declares Command1, so a rename that keeps off the header would rename every
        // reference in the code and not the control. Before the server kept off the header this was refused
        // because the answer reached the header; now it is refused because of what the name is.
        var form = OpenForm();
        var text = form.Document.Text;
        var reference = text.IndexOf("Command1.Enabled", StringComparison.Ordinal);

        form.RenameRefusalAt(reference + 2, "Command1").Should().Be("rename refused");
        form.RenameRefusalAt(reference + 2, "command1").Should().Be("rename refused", "VB6 compares names ignoring case");
        form.RenameRefusalAt(text.IndexOf("Command1.Enabled", StringComparison.Ordinal) + "Command1.".Length + 2, "Enabled")
            .Should().BeNull();
    }

    [AvaloniaFact]
    public void ARenameEditToAnAttributeLineThatIsNotTheQualifierIsRefused()
    {
        var vm = OpenClass();
        var before = vm.Document.Text;
        var line = vm.Document.GetLineByOffset(vm.Document.Text.IndexOf("Attribute Total.", StringComparison.Ordinal));
        var column = vm.Document.GetText(line).IndexOf("VB_Description", StringComparison.Ordinal);

        var refusal = vm.ApplyRename(
            [new TextEdit(new HexIDE.Lsp.Messages.Range(
                new Position(line.LineNumber - 1, column), new Position(line.LineNumber - 1, column + "VB_Description".Length)),
                "VB_Other")],
            "VB_Description");

        refusal.Should().NotBeNull();
        vm.Document.Text.Should().Be(before);
    }

    // ── Add-in ApplyEdits: all or nothing ───────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void EditsOneOfWhichTouchesTheHeaderAreAllRefused()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var inCode = vm.Document.Text.IndexOf("False", StringComparison.Ordinal);
        var inHeader = vm.Document.Text.IndexOf("\"Orders\"", StringComparison.Ordinal);

        var refusal = vm.ApplyEdits([new TextChange(inCode, 5, "True"), new TextChange(inHeader, 8, "\"Sales\"")]);

        refusal.Should().NotBeNull();
        vm.Document.Text.Should().Be(before, "applying the rest would leave the add-in's change half made");
    }

    [AvaloniaFact]
    public void EditsOutsideEveryRegionAreAppliedAsOneUndoStep()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var dim = vm.Document.Text.IndexOf("Dim Caption", StringComparison.Ordinal);
        var enabled = vm.Document.Text.IndexOf("False", StringComparison.Ordinal);

        // Given out of order on purpose: an add-in's edits are positions in the text it read, not a sequence.
        vm.ApplyEdits([new TextChange(dim, 3, "Static"), new TextChange(enabled, 5, "True")]).Should().BeNull();

        vm.Document.Text.Should().Be(before.Replace("Dim Caption", "Static Caption").Replace("= False", "= True"));
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AnEditRemovingTheLineBreakAnAttributeRunHangsFromIsRefused()
    {
        // Outside the run, but it would join the attribute line onto the declaration (#273 task 3.11).
        var vm = OpenClass();
        var before = vm.Document.Text;
        var terminator = before.IndexOf("\r\nAttribute Total", StringComparison.Ordinal);

        vm.ApplyEdits([new TextChange(terminator, 2, " ")]).Should().NotBeNull();

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void ARenameEditRemovingTheLineBreakAnAttributeRunHangsFromIsRefused()
    {
        var vm = OpenClass();
        var before = vm.Document.Text;
        var declaration = vm.Document.GetLineByOffset(before.IndexOf("Public Function Total", StringComparison.Ordinal));

        var refusal = vm.ApplyRename(
            [new TextEdit(new HexIDE.Lsp.Messages.Range(
                new Position(declaration.LineNumber - 1, declaration.Length), new Position(declaration.LineNumber, 0)),
                " ")],
            "Currency");

        refusal.Should().NotBeNull();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AnEditInsideAMembersAttributeLineIsRefused()
    {
        var vm = OpenClass();
        var before = vm.Document.Text;
        var description = vm.Document.Text.IndexOf("The order total", StringComparison.Ordinal);

        vm.ApplyEdits([new TextChange(description, 3, "A")]).Should()
            .Contain("would change the Attribute lines describing Total", "the refusal names the member (#273 task 4.5b)");

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AnEditJoiningAMembersAttributeLineOntoItsDeclarationIsRefusedNamingTheMember()
    {
        // The line break the run hangs from is outside it, and joining across it is a change to the run (task 3.11).
        var vm = OpenClass();
        var anchor = vm.Document.Text.IndexOf("Currency\r\nAttribute", StringComparison.Ordinal) + "Currency".Length;

        vm.ApplyEdits([new TextChange(anchor, 2, " ")]).Should().Contain("the Attribute lines describing Total");
    }

    [AvaloniaFact]
    public void AnEditReachingTheHeaderAndAMembersRunIsRefusedForTheHeader()
    {
        var vm = OpenClass();
        var description = vm.Document.Text.IndexOf("The order total", StringComparison.Ordinal);

        vm.ApplyEdits([new TextChange(2, description - 2, "")]).Should().Contain("would change the document's header")
            .And.NotContain("describing Total");
    }

    [AvaloniaFact]
    public void AWholeReplacementChangingAMembersAttributeLineIsRefusedNamingTheMember()
    {
        var vm = OpenClass();
        var before = vm.Document.Text;

        var result = vm.ReplaceContent(before.Replace("The order total", "The total"));

        result.Refusal.Should().Contain("the Attribute lines describing Total are changed");
        vm.Document.Text.Should().Be(before);
    }

    // ── Add-in SetContent and automation set_file_content: whole file with its header, or code alone ────

    [AvaloniaFact]
    public void ApplyingABlockOfCodeAlone()
    {
        var vm = OpenForm();
        var header = vm.Document.Text[..HeaderEnd(vm)];

        var result = vm.ReplaceContent("Option Explicit\r\n");

        result.Refusal.Should().BeNull();
        vm.Document.Text.Should().Be(header + "Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void RewritingAFormAndKeepingItsHeader()
    {
        var vm = OpenForm();
        var whole = vm.Document.Text.Replace("Command1.Enabled = False", "Command1.Enabled = True");

        vm.ReplaceContent(whole).Refusal.Should().BeNull();

        vm.Document.Text.Should().Be(whole);
    }

    [AvaloniaFact]
    public void AnAddinReplacesADocumentsContentWithADifferentHeader()
    {
        // The code-editor delta's scenario: the replacement is refused and the caller is told why.
        var vm = OpenForm();
        var before = vm.Document.Text;

        var result = vm.ReplaceContent(before.Replace("\"Orders\"", "\"Sales\""));

        result.Refusal.Should().NotBeNullOrEmpty();
        vm.Document.Text.Should().Be(before);
    }

    // ── Insert File: after the region, replacing nothing ────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFileInsertedWithTheCaretInTheHeaderGoesAfterIt()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var headerEnd = HeaderEnd(vm);

        var caret = vm.InsertPastReadOnlyRegion(before.IndexOf("Caption", StringComparison.Ordinal), 0, "Dim x As Long");

        vm.Document.Text.Should().Be(before[..headerEnd] + "Dim x As Long\r\n" + before[headerEnd..]);
        caret.Should().Be(headerEnd + "Dim x As Long\r\n".Length);
    }

    [AvaloniaFact]
    public void AFileInsertedOverASelectionReachingIntoTheHeaderReplacesNothing()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var start = before.IndexOf("Attribute VB_PredeclaredId", StringComparison.Ordinal);
        var end = before.IndexOf("Private Sub", StringComparison.Ordinal);
        var headerEnd = HeaderEnd(vm);

        vm.InsertPastReadOnlyRegion(start, end - start, "' inserted\r\n");

        vm.Document.Text.Should().Be(before[..headerEnd] + "' inserted\r\n" + before[headerEnd..]);
    }

    [AvaloniaFact]
    public void AFileInsertedInTheCodeIsLeftToTheEditor()
    {
        // Outside a region the selection is the developer's choice, and the view replaces it as it always has.
        var vm = OpenForm();
        var before = vm.Document.Text;

        vm.InsertPastReadOnlyRegion(before.IndexOf("Dim Caption", StringComparison.Ordinal), 3, "x").Should().BeNull();

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void AFileInsertedAfterAHeaderWithNoFinalLineBreakStartsALineOfItsOwn()
    {
        // The header's attribute run carries on into the code, whose last line has no terminator.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Attribute VB_Description = \"Helpers\"");
        var vm = NewEditor().Initialize(module);
        var before = vm.Document.Text;

        vm.InsertPastReadOnlyRegion(3, 0, "Sub Main()\r\nEnd Sub\r\n");

        vm.Document.Text.Should().Be(before + "\r\nSub Main()\r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public async Task InsertFileAsksTheWindowManagerForTheFile()
    {
        // The window manager's picker is the one automation can answer. The view used to open the storage
        // provider's directly, so an automated Insert File left a native dialog waiting for a person.
        var path = Path.Combine(Path.GetTempPath(), $"hexide-insert-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "' from a file\r\n", TestContext.Current.CancellationToken);
        try
        {
            windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
                .Returns(Task.FromResult<IReadOnlyList<string>?>([path]));
            var vm = OpenForm();
            var before = vm.Document.Text;
            var inCode = before.IndexOf("Dim Caption", StringComparison.Ordinal);

            await vm.InsertFileAsync(inCode, 0);

            vm.Document.Text.Should().Be(before.Insert(inCode, "' from a file\r\n"));
            vm.CaretOffset.Should().Be(inCode + "' from a file\r\n".Length);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public async Task InsertFileCancelledWritesNothing()
    {
        windowManager.OpenFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerOpenOptions>())
            .Returns(Task.FromResult<IReadOnlyList<string>?>(null));
        var vm = OpenForm();
        var before = vm.Document.Text;

        await vm.InsertFileAsync(0, 0);

        vm.Document.Text.Should().Be(before);
    }

    // ── Completion commit: never into a region ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ACompletionCommittedInsideTheHeaderWritesNothing()
    {
        var vm = OpenForm();
        var before = vm.Document.Text;
        var area = new TextArea { Document = vm.Document };
        var inHeader = before.IndexOf("Caption", StringComparison.Ordinal);

        new VbCompletionData(new CompletionItem("CaptionText", CompletionItemKind.Variable), vm.RefusesTyping)
            .Complete(area, new TextSegment { StartOffset = inHeader, Length = 7 }, EventArgs.Empty);

        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void ACompletionCommittedInTheCodeIsApplied()
    {
        var vm = OpenForm();
        var area = new TextArea { Document = vm.Document };
        var inCode = vm.Document.Text.IndexOf("Enabled", StringComparison.Ordinal);

        new VbCompletionData(new CompletionItem("Visible", CompletionItemKind.Variable), vm.RefusesTyping)
            .Complete(area, new TextSegment { StartOffset = inCode, Length = 7 }, EventArgs.Empty);

        vm.Document.Text.Should().Contain("Command1.Visible = False");
    }

    [AvaloniaFact]
    public void ACompletionCommittedInAFileHeldReadOnlyAsAWholeWritesNothing()
    {
        // The window's banner says typing is refused anywhere in such a file, and a completion's commit is
        // typing that writes the document itself. The same span is applied in a file that can be saved.
        var vm = OpenFormHeldReadOnly();
        vm.IsReadOnly.Should().BeTrue();
        var before = vm.Document.Text;
        var area = new TextArea { Document = vm.Document };
        var inCode = before.IndexOf("Enabled", StringComparison.Ordinal);

        new VbCompletionData(new CompletionItem("Visible", CompletionItemKind.Variable), vm.RefusesTyping)
            .Complete(area, new TextSegment { StartOffset = inCode, Length = 7 }, EventArgs.Empty);

        vm.Document.Text.Should().Be(before);
    }
}
