using HexIDE.Bookmarks;
using HexIDE.Conversations;
using HexIDE.Dap;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Serialization;
using static HexIDE.Tests.ViewModels.BreakpointEditTests;

namespace HexIDE.Tests.ViewModels;

public class BookmarkEditTests
{
    // VB6 SP6 measurements for #5334 (oracle PR #5387).
    [AvaloniaTheory]
    [InlineData("control", 6)]
    [InlineData("insert", 7)]
    [InlineData("delete above", 5)]
    [InlineData("paste", 7)]
    [InlineData("undo", 6)]
    [InlineData("enter", 7)]
    [InlineData("delete own", null)]
    [InlineData("join", 5)]
    public void MeasuredEditsKeepTheBookmarkWithTheStatement(string edit, int? expected)
    {
        var bookmarks = new BookmarkService();
        var breakpoints = new BreakpointService();
        using var editor = NewEditor(breakpoints, bookmarks);
        editor.ReplaceBody(BreakpointEditTests.Program);
        var prefix = HeaderLines(editor);
        bookmarks.Toggle(editor.Identity, 5 + prefix);
        if (edit == "join")
            breakpoints.Toggle(editor.Identity, 6 + prefix);

        EditBody(editor, edit, prefix);
        if (edit == "undo")
            editor.Document.UndoStack.Undo();

        bookmarks.GetBookmarks(editor.Identity).Should().Equal(expected is { } line ? [line + prefix - 1] : []);
        if (edit == "join")
            breakpoints.GetBreakpoints(editor.Identity).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void UndoAndRedoMoveTheBookmarkBackAndForth()
    {
        var bookmarks = new BookmarkService();
        using var editor = NewEditor(new BreakpointService(), bookmarks);
        editor.ReplaceBody(BreakpointEditTests.Program);
        var prefix = HeaderLines(editor);
        bookmarks.SetBookmarks(editor.Identity, [5 + prefix]);
        var notifications = 0;
        bookmarks.BookmarksChanged += _ => notifications++;

        editor.Document.Insert(editor.Document.TextLength, " ");
        notifications.Should().Be(0);
        EditBody(editor, "insert", prefix);
        bookmarks.GetBookmarks(editor.Identity).Should().Equal(6 + prefix);
        editor.Document.UndoStack.Undo();
        bookmarks.GetBookmarks(editor.Identity).Should().Equal(5 + prefix);
        editor.Document.UndoStack.Redo();
        bookmarks.GetBookmarks(editor.Identity).Should().Equal(6 + prefix);
        notifications.Should().Be(3);
    }

    [AvaloniaFact]
    public void WholeTextReplacementKeepsBookmarkNumbers()
    {
        var bookmarks = new BookmarkService();
        using var editor = NewEditor(new BreakpointService(), bookmarks);
        editor.ReplaceBody(BreakpointEditTests.Program);
        bookmarks.SetBookmarks(editor.Identity, [6]);

        editor.ReplaceBody("\r\n" + BreakpointEditTests.Program);

        bookmarks.GetBookmarks(editor.Identity).Should().Equal(6);
    }

    [AvaloniaFact]
    public async Task AnActiveExternalDebuggerDoesNotStopABookmarkFromFollowing()
    {
        await using var capture = new ConversationLog();
        var external = new ExternalDebugService(capture);
        var configuration = new TaskCompletionSource<DapLaunchConfiguration>(TaskCreationOptions.RunContinuationsAsynchronously);
        var starting = external.StartAsync(_ => configuration.Task);
        try
        {
            external.IsActive.Should().BeTrue();
            var bookmarks = new BookmarkService();
            using var editor = NewEditor(new BreakpointService(), bookmarks, external);
            editor.ReplaceBody(BreakpointEditTests.Program);
            var prefix = HeaderLines(editor);
            bookmarks.SetBookmarks(editor.Identity, [5 + prefix]);

            EditBody(editor, "insert", prefix);

            bookmarks.GetBookmarks(editor.Identity).Should().Equal(6 + prefix);
        }
        finally
        {
            await external.StopAsync();
            configuration.TrySetCanceled();
            await starting.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static int HeaderLines(CodeEditorViewModel editor) =>
        FormCodeText.LineBreaks(editor.Document.GetText(0, editor.BufferPrefixLength));
}
