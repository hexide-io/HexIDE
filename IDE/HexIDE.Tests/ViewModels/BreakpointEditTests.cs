using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;

namespace HexIDE.Tests.ViewModels;

public class BreakpointEditTests
{
    // Measured program and edits: docs/vb6-fidelity-oracle/a-breakpoint-through-edits-above-it.md.
    internal const string Program = "Option Explicit\r\nDim n As Integer\r\n\r\nSub Main()\r\n    L \"one\"\r\n    L \"two\"\r\n    L \"three\"\r\n    L \"four\"\r\nEnd Sub";

    internal static void Edit(TextDocument document, string edit)
    {
        var one = document.GetLineByNumber(5);
        var two = document.GetLineByNumber(6);
        switch (edit)
        {
            case "control": break;
            case "insert":
            case "undo": document.Insert(one.EndOffset, "\r\n"); break;
            case "delete above":
                var main = document.GetLineByNumber(4);
                document.Remove(main.EndOffset, one.EndOffset - main.EndOffset);
                break;
            case "paste": document.Insert(one.EndOffset, "\r\n    L \"one\""); break;
            case "enter": document.Insert(two.Offset + 4, "\r\n"); break;
            case "delete own": document.Remove(two.Offset + 4, document.GetLineByNumber(7).Offset - two.Offset); break;
            case "join": document.Remove(one.Offset + 4, two.Offset - one.Offset); break;
        }
    }

    [AvaloniaTheory]
    [InlineData("control", 6)]
    [InlineData("insert", 7)]
    [InlineData("delete above", 5)]
    [InlineData("paste", 7)]
    [InlineData("undo", 6)]
    [InlineData("enter", 7)]
    [InlineData("delete own", null)]
    [InlineData("join", null)]
    public void MeasuredEditsKeepTheConditionWithTheStatement(string edit, int? expected)
    {
        var breakpoints = new BreakpointService();
        using var editor = NewEditor(breakpoints, new BookmarkService());
        editor.ReplaceBody(Program);
        // Include the real module header in file line numbers.
        var prefix = editor.Document.LineCount - new TextDocument(Program).LineCount;
        breakpoints.Toggle(editor.Identity, 6 + prefix);
        breakpoints.SetCondition(editor.Identity, 6 + prefix, "n = 2");
        var body = new TextDocument(Program);
        DocumentChangeEventArgs? change = null;
        body.Changing += (_, e) => change = e;
        Edit(body, edit);
        if (change is not null)
            editor.Document.Replace(change.Offset + editor.Document.GetLineByNumber(prefix + 1).Offset,
                change.RemovalLength, change.InsertedText.Text);
        if (edit == "undo")
        {
            breakpoints.GetBreakpoints(editor.Identity).Should().Equal(7 + prefix);
            editor.Document.UndoStack.Undo();
        }
        breakpoints.GetBreakpoints(editor.Identity).Should().Equal(expected is { } line ? new[] { line + prefix } : []);
        if (expected is { } moved)
            breakpoints.GetCondition(editor.Identity, moved + prefix).Should().Be("n = 2");
        else
            breakpoints.GetCondition(editor.Identity, 6 + prefix).Should().BeNull();
    }

    [AvaloniaFact]
    public void UndoAndRedoMoveTheConditionBackAndForth()
    {
        var breakpoints = new BreakpointService();
        using var editor = NewEditor(breakpoints, new BookmarkService());
        editor.ReplaceBody(Program);
        var line = editor.Document.GetLineByOffset(editor.Document.Text.IndexOf("L \"two\"", StringComparison.Ordinal)).LineNumber;
        breakpoints.Toggle(editor.Identity, line);
        breakpoints.SetCondition(editor.Identity, line, "n = 2");
        editor.Document.Insert(editor.Document.GetLineByNumber(line).Offset, "\r\n");
        breakpoints.GetBreakpoints(editor.Identity).Should().Equal(line + 1);
        editor.Document.UndoStack.Undo();
        breakpoints.GetBreakpoints(editor.Identity).Should().Equal(line);
        breakpoints.GetCondition(editor.Identity, line).Should().Be("n = 2");
        editor.Document.UndoStack.Redo();
        breakpoints.GetBreakpoints(editor.Identity).Should().Equal(line + 1);
        breakpoints.GetCondition(editor.Identity, line + 1).Should().Be("n = 2");
    }

    [AvaloniaFact]
    public void BookmarksKeepTheirNumbersUntilMeasured()
    {
        var bookmarks = new BookmarkService();
        using var editor = NewEditor(new BreakpointService(), bookmarks);
        editor.ReplaceBody(Program);
        bookmarks.SetBookmarks(editor.Identity, [6]);
        editor.Document.Insert(editor.Document.GetLineByNumber(5).EndOffset, "\r\n");
        bookmarks.GetBookmarks(editor.Identity).Should().Equal(6);
    }

    [AvaloniaFact]
    public void WholeTextReplacementKeepsBreakpointNumbersAndConditions()
    {
        var breakpoints = new BreakpointService();
        using var editor = NewEditor(breakpoints, new BookmarkService());
        editor.ReplaceBody(Program);
        breakpoints.Toggle(editor.Identity, 7);
        breakpoints.SetCondition(editor.Identity, 7, "n = 2");
        editor.ReplaceBody("\r\n" + Program);
        breakpoints.GetBreakpoints(editor.Identity).Should().Equal(7);
        breakpoints.GetCondition(editor.Identity, 7).Should().Be("n = 2");
    }

    [AvaloniaFact]
    public async Task AnActiveExternalDebuggerLeavesTheInterpreterStoreAlone()
    {
        await using var capture = new HexIDE.Conversations.ConversationLog();
        var external = new HexIDE.Debugging.ExternalDebugService(capture);
        var configuration = new TaskCompletionSource<HexIDE.Dap.DapLaunchConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = external.StartAsync(_ => configuration.Task);
        try
        {
            external.IsActive.Should().BeTrue();
            var breakpoints = new BreakpointService();
            using var editor = NewEditor(breakpoints, new BookmarkService(), external);
            editor.ReplaceBody(Program);
            breakpoints.SetDocument(editor.Identity, [7]);
            breakpoints.SetCondition(editor.Identity, 7, "n = 2");

            editor.Document.Insert(editor.Document.GetLineByNumber(5).EndOffset, "\n");

            breakpoints.GetBreakpoints(editor.Identity).Should().Equal(7);
            breakpoints.GetCondition(editor.Identity, 7).Should().Be("n = 2");
        }
        finally
        {
            await external.StopAsync();
            configuration.TrySetCanceled();
            await starting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static CodeEditorViewModel NewEditor(IBreakpointService breakpoints, IBookmarkService bookmarks,
        HexIDE.Debugging.ExternalDebugService? external = null) =>
        new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), new EventBus(), Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(), bookmarks, breakpoints,
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(),
            Substitute.For<ILocalizationService>(), externalDebugger: external).Initialize(TestHelpers.CreateModule());
}
