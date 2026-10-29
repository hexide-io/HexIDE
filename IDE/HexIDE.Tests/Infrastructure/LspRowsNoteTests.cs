using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Redaction;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// list_lsp_messages explains its rows beside them (#2564): where a request's reply is, and why the sequence numbers have
/// gaps, which beside a field called framesDropped read as lost frames. Its description used to say it, at the cost of
/// the room its vocabularies need.
/// </summary>
public class LspRowsNoteTests
{
    private static (ConversationLog Capture, IdeContext Context, HexIdeTools Tools) Tools()
    {
        var capture = new ConversationLog();
        var context = new IdeContext(Substitute.For<IProjectManager>(), Substitute.For<IDocumentDockService>(),
            Substitute.For<ILspClient>(), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            null!, capture, new Pseudonymiser(), null!, null!);
        return (capture, context, new HexIdeTools(context));
    }

    [Fact]
    public async Task Rows_come_with_the_note_that_explains_them()
    {
        var (capture, context, tools) = Tools();
        await using var _ = capture;
        using var __ = context;
        capture.Record("probe", ConversationDirection.Sent, ConversationEntryKind.Notification, "initialized", null, 0);

        var reply = await tools.ListLspMessagesAsync(ct: TestContext.Current.CancellationToken);

        reply.Messages.Should().NotBeEmpty();
        reply.RowsNote.Should().Be(HexIdeTools.LspRowsNote);
        reply.RowsNote.Should().Contain("answerSizeBytes").And.Contain("answerSequence").And.Contain("completedSequence")
            .And.Contain("framesDropped is the only report of real loss");
    }

    [Fact]
    public async Task An_empty_page_has_no_rows_to_explain_and_its_note_says_why_it_is_empty()
    {
        var (capture, context, tools) = Tools();
        await using var _ = capture;
        using var __ = context;

        var reply = await tools.ListLspMessagesAsync(ct: TestContext.Current.CancellationToken);

        reply.Messages.Should().BeEmpty();
        reply.RowsNote.Should().BeNull();
        reply.Note.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void The_description_points_at_the_note_and_keeps_the_vocabularies()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == "list_lsp_messages");
        tool.Description.Should().Contain("'rowsNote'").And.Contain("NeverSent").And.Contain("TimedOut")
            .And.NotContain("completedSequence", "the reply explains it beside the rows");
    }
}
