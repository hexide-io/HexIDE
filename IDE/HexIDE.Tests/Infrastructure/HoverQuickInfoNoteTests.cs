using HexIDE.Conversations;
using HexIDE.Desktop.Server;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using static HexIDE.Desktop.Server.HoverQuickInfoNote;
using Route = HexIDE.Forms.Views.CodeEditorView.HoverRoute;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// hover's account of why a code window opened no tip (#2574). Each cause it once hid behind one sentence — no server, an
/// empty answer, a late answer, a missed word — must now read differently, and say only what was observed.
/// </summary>
public class HoverQuickInfoNoteTests
{
    private const string Uri = "file:///c%3A/p/Module1.bas";

    private static CodeEditorView.HoverRouteAt At(Route route = Route.QuickInfo, string? word = "MsgBox", int? line = 19,
        string? uri = Uri, bool evaluates = true) =>
        new(route, line, line is null ? null : 7, word, uri, evaluates);

    private static Asked Answered(string id = "vb6", long sequence = 41, int? bytes = 38) =>
        new(id, sequence, ConversationOutcome.Answered, true, TimeSpan.FromMilliseconds(12), bytes, null, TimeSpan.Zero);

    private static readonly Registered RunningWithHover = new("vb6", LanguageConnectionState.Running, true);

    private static string Note(CodeEditorView.HoverRouteAt at, IReadOnlyList<Asked>? asked = null,
        IReadOnlyList<Registered>? registered = null, int dwellMs = 1500, string? extension = ".bas") =>
        Describe(at, dwellMs, asked ?? [], registered ?? [RunningWithHover], extension);

    [Fact]
    public void A_server_that_answered_with_nothing_is_named_with_the_word_and_the_answers_size()
    {
        Note(At(), [Answered()]).Should().Be(
            "quick info: at line 19, column 7 on 'MsgBox', textDocument/hover went to 'vb6' (sequence 41) and was "
            + "answered in 12ms with nothing to show (38 bytes)");
    }

    [Fact]
    public void A_request_still_waiting_when_the_watch_ended_says_so_and_how_long_it_had_waited()
    {
        var waiting = new Asked("vb6", 41, ConversationOutcome.None, false, null, null, null, TimeSpan.FromMilliseconds(1100));
        Note(At(), [waiting]).Should().Contain("had no answer when the watch ended, 1100ms after it was sent")
            .And.Contain("a longer dwellMs").And.NotContain("nothing to show");
    }

    [Theory]
    [InlineData(ConversationOutcome.Failed, "failed: boom")]
    [InlineData(ConversationOutcome.TimedOut, "timed out without an answer")]
    [InlineData(ConversationOutcome.Cancelled, "was cancelled before an answer came")]
    [InlineData(ConversationOutcome.Abandoned, "left unanswered when the connection went away")]
    public void Every_other_outcome_is_named_and_none_is_called_empty(ConversationOutcome outcome, string said)
    {
        var asked = new Asked("vb6", 41, outcome, true, null, null, "boom", TimeSpan.Zero);
        Note(At(), [asked]).Should().Contain(said).And.NotContain("nothing to show");
    }

    [Fact]
    public void Each_server_asked_is_reported_in_order_because_a_hover_asks_claimants_one_after_another()
    {
        Note(At(), [Answered("first", 41), Answered("second", 43)]).Should()
            .Contain("went to 'first' (sequence 41)").And.Contain("; then textDocument/hover went to 'second' (sequence 43)");
    }

    [Fact]
    public void No_server_registered_for_the_extension_is_named_as_the_reason_nothing_was_asked()
    {
        Note(At(), registered: []).Should().EndWith(
            "and no language server is registered for '.bas' files, so none was asked");
    }

    [Theory]
    [InlineData(LanguageConnectionState.NotStarted, "'vb6' is not started")]
    [InlineData(LanguageConnectionState.Failed, "'vb6' is failed")]
    [InlineData(LanguageConnectionState.Stopped, "'vb6' is stopped")]
    public void A_registered_server_that_is_not_running_is_named_with_its_state(LanguageConnectionState state, string said)
    {
        Note(At(), registered: [new Registered("vb6", state, true)]).Should()
            .Contain(said).And.EndWith("so no language server was asked");
    }

    [Fact]
    public void A_running_server_without_hoverProvider_is_named_rather_than_inferred_from_a_missing_row()
    {
        // Its NeverSent row is written once per connection, so a second hover finds none above the watermark.
        Note(At(), registered: [new Registered("vb6", LanguageConnectionState.Running, false)]).Should()
            .Contain("'vb6' is running for '.bas' files but does not advertise hoverProvider");
    }

    [Fact]
    public void Nothing_recorded_from_a_server_that_should_have_been_asked_is_said_as_exactly_that()
    {
        Note(At()).Should().Contain("no textDocument/hover was recorded").And.Contain("'vb6' is running")
            .And.Contain("list_lsp_messages");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(400)]
    public void A_watch_no_longer_than_the_editors_delay_says_nothing_had_been_asked_yet(int dwellMs)
    {
        Note(At(), dwellMs: dwellMs).Should().Contain("before the editor's 400ms quick-info delay")
            .And.Contain("dwellMs must exceed 400");
    }

    [Fact]
    public void A_point_below_the_text_says_nothing_was_asked()
    {
        Note(At(line: null, word: null)).Should().Be("quick info: the point is below the last line of text, so nothing was asked");
    }

    [Fact]
    public void A_point_on_no_word_says_so_beside_what_the_server_answered()
    {
        Note(At(word: null), [Answered()]).Should().StartWith("quick info: at line 19, column 7 on no word, ");
    }

    [Fact]
    public void Quick_info_off_is_named_as_the_reason_no_server_was_asked()
    {
        Note(At(Route.QuickInfoOff), [Answered()]).Should().Contain("Auto Quick Info is off").And.NotContain("went to");
    }

    [Fact]
    public void A_paused_program_evaluates_the_word_instead_of_asking_a_server()
    {
        Note(At(Route.DataTip)).Should().Contain("'MsgBox' was evaluated for an Auto Data Tip instead of asking a language server");
        Note(At(Route.DataTip, word: null)).Should().Contain("there is no word there");
    }

    [Fact]
    public void An_external_debugger_is_named_with_whether_it_evaluates_for_hovers()
    {
        Note(At(Route.ExternalDataTip)).Should().Contain("it was asked for 'MsgBox''s value");
        Note(At(Route.ExternalDataTip, evaluates: false)).Should().Contain("does not evaluate for hovers, so nothing was asked");
    }

    [Fact]
    public void A_document_not_open_with_the_servers_is_named()
    {
        Note(At(uri: null)).Should().Contain("not open with the language servers");
    }

    [Fact]
    public void Only_this_hovers_hover_requests_are_taken_from_the_capture()
    {
        var now = DateTimeOffset.UtcNow;
        ConversationEnvelope Row(long seq, string? method, ConversationEntryKind kind = ConversationEntryKind.Request,
            ConversationDirection direction = ConversationDirection.Sent, ConversationOutcome outcome = ConversationOutcome.None,
            long? completed = null) =>
            new(seq, "vb6", now.AddMilliseconds(-250), direction, kind, method, seq.ToString(), 90, outcome,
                CompletedSequence: completed);

        var asked = AskedSince([
            Row(9, HoverMethod, outcome: ConversationOutcome.Answered, completed: 10),   // before the watermark
            Row(10, HoverMethod, outcome: ConversationOutcome.Answered, completed: 10),  // the watermark itself
            Row(11, "textDocument/completion"),                                           // another method
            Row(12, HoverMethod, direction: ConversationDirection.Received),             // not ours
            Row(13, null, ConversationEntryKind.NeverSent),                               // not a request
            Row(15, HoverMethod),                                                         // still waiting
            Row(14, HoverMethod, outcome: ConversationOutcome.Answered, completed: 16),
        ], watermark: 10, now);

        asked.Select(a => a.Sequence).Should().Equal(14, 15);
        asked[0].Completed.Should().BeTrue();
        asked[1].Completed.Should().BeFalse();
        asked[1].Waited.Should().BeCloseTo(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(5));
    }

    [Fact]
    public void Registered_servers_are_those_claiming_the_extension_with_hoverProvider_read_from_their_capabilities()
    {
        var caps = System.Text.Json.JsonDocument.Parse("""{"hoverProvider":true}""").RootElement;
        var none = System.Text.Json.JsonDocument.Parse("""{"hoverProvider":false}""").RootElement;
        LanguageServerConnection Conn(string id, string ext, System.Text.Json.JsonElement? capabilities,
            LanguageConnectionKind kind = LanguageConnectionKind.LanguageServer) =>
            new(id, id, kind, LanguageConnectionState.Running, [ext], "vb", capabilities);

        RegisteredFor([Conn("a", ".BAS", caps), Conn("b", ".bas", none), Conn("c", ".md", caps),
                Conn("d", ".bas", caps, LanguageConnectionKind.DebugAdapter)], ".bas")
            .Should().Equal(new Registered("a", LanguageConnectionState.Running, true),
                new Registered("b", LanguageConnectionState.Running, false));
        RegisteredFor([Conn("a", ".bas", caps)], null).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false, true, true, true, "NotOverText")]
    [InlineData(true, true, true, false, "ExternalDataTip")]
    [InlineData(true, false, true, false, "DataTip")]
    [InlineData(true, false, false, true, "QuickInfo")]
    [InlineData(true, false, false, false, "QuickInfoOff")]
    public void The_editors_route_is_decided_in_its_own_order(bool overText, bool externalStop, bool paused,
        bool autoQuickInfo, string expected)
    {
        // By name: the enum is internal, and a public test method cannot take it.
        CodeEditorView.RouteFor(overText, externalStop, paused, autoQuickInfo).Should().Be(Enum.Parse<Route>(expected));
    }
}
