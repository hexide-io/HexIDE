using HexIDE.Conversations;
using HexIDE.Forms.Views;
using HexIDE.Lsp;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Why a code window's hover opened no tip: the point, what the editor asked for there, and what became of it (#2574).
/// </summary>
/// <remarks>
/// <b>"no tip opened" used to read the same for four different causes</b>: no language server running for the document,
/// the server answering with nothing, the server not answering within the watch, and the point missing the word. A
/// caller could tell them apart only by arming the protocol capture and reading the wire, which is the ambiguity the
/// capture exists to remove, reintroduced inside the tool that drives it.
///
/// <para>
/// Built from what was observed, never inferred: the route is the editor's own decision
/// (<see cref="CodeEditorView.RouteUnder"/>), and every request is a capture row recorded above the watermark taken
/// before the move, which the capture keeps unarmed. Where nothing was recorded the note says why it would not have
/// been sent from the connections' advertised capabilities, read afresh, because a server without
/// <c>hoverProvider</c> records its <c>NeverSent</c> row once per connection and a second hover leaves no new one.
/// </para>
/// </remarks>
internal static class HoverQuickInfoNote
{
    /// <summary>How long the code window waits, after the pointer stops, before it asks for quick info.</summary>
    internal const int QuickInfoDelayMs = 400;

    internal const string HoverMethod = "textDocument/hover";

    /// <summary>A hover request the capture recorded during the watch.</summary>
    /// <param name="Completed">False while it still waited for an answer when the watch ended.</param>
    /// <param name="Waited">For one still waiting, how long since it was sent.</param>
    internal readonly record struct Asked(
        string ConnectionId, long Sequence, ConversationOutcome Outcome, bool Completed, TimeSpan? Elapsed,
        int? AnswerSizeBytes, string? Detail, TimeSpan Waited);

    /// <summary>A language server registered for the document's extension, and whether it would be asked.</summary>
    internal readonly record struct Registered(string Id, LanguageConnectionState State, bool AdvertisesHover);

    /// <summary>The hover requests recorded after <paramref name="watermark"/>, in the order they were sent.</summary>
    internal static IReadOnlyList<Asked> AskedSince(
        IEnumerable<ConversationEnvelope> envelopes, long watermark, DateTimeOffset now) =>
        [.. envelopes
            .Where(e => e.Sequence > watermark && e.Kind == ConversationEntryKind.Request
                        && e.Direction == ConversationDirection.Sent && e.Method == HoverMethod)
            .OrderBy(e => e.Sequence)
            .Select(e => new Asked(e.ConnectionId, e.Sequence, e.Outcome,
                e.Outcome != ConversationOutcome.None || e.CompletedSequence is not null, e.Elapsed,
                e.AnswerSizeBytes, e.Detail, now - e.Timestamp))];

    /// <summary>The servers registered for <paramref name="extension"/>, whether started or not.</summary>
    internal static IReadOnlyList<Registered> RegisteredFor(IEnumerable<LanguageServerConnection> connections, string? extension) =>
        extension is null ? [] :
        [.. connections
            .Where(c => c.Kind == LanguageConnectionKind.LanguageServer
                        && c.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            .Select(c => new Registered(c.Id, c.State,
                HexIDE.Lsp.Messages.ServerCapabilities.Supports(c.Capabilities, "hoverProvider")))];

    /// <summary>
    /// The note, beginning "quick info: ". <paramref name="registered"/> and <paramref name="extension"/> are read only
    /// when nothing was recorded as asked.
    /// </summary>
    internal static string Describe(CodeEditorView.HoverRouteAt at, int dwellMs, IReadOnlyList<Asked> asked,
        IReadOnlyList<Registered> registered, string? extension)
    {
        if (at.Line is not { } line || at.Column is not { } column)
            return "quick info: the point is below the last line of text, so nothing was asked";

        var where = "at line " + line + ", column " + column + (at.Word is { } w ? " on '" + w + "'" : " on no word");
        return "quick info: " + where + ", " + at.Route switch
        {
            CodeEditorView.HoverRoute.ExternalDataTip => at.Word is null
                ? "with the program stopped in an external debugger, which is asked for a word's value instead of a "
                  + "language server, and there is no word there to ask about"
                : at.ExternalEvaluates
                    ? "with the program stopped in an external debugger: it was asked for '" + at.Word + "''s value "
                      + "instead of a language server, and gave none"
                    : "with the program stopped in an external debugger that does not evaluate for hovers, so nothing "
                      + "was asked",
            CodeEditorView.HoverRoute.DataTip => at.Word is null
                ? "with the program paused, when a hover evaluates the word for an Auto Data Tip instead of asking a "
                  + "language server, and there is no word there"
                : "with the program paused: '" + at.Word + "' was evaluated for an Auto Data Tip instead of asking a "
                  + "language server, and has no value to show (a keyword, out of scope, or an error)",
            CodeEditorView.HoverRoute.QuickInfoOff =>
                "and Auto Quick Info is off in Options, so no language server was asked; only a diagnostic at the "
                + "point would open a tip, and there is none",
            _ => asked.Count > 0 ? Outcomes(asked) : NothingAsked(at, dwellMs, registered, extension),
        };
    }

    // An answer with text opens a tip, and the next server is asked only after an answer with none, so with no tip open
    // every answered request answered with nothing to show: what the answers said, not a guess about them.
    private static string Outcomes(IReadOnlyList<Asked> asked) =>
        string.Join("; then ", asked.Select(a => HoverMethod + " went to '" + a.ConnectionId + "' (sequence "
            + a.Sequence + ") and " + Outcome(a)));

    private static string Outcome(Asked a) => !a.Completed
        ? "had no answer when the watch ended, " + Ms(a.Waited) + "ms after it was sent; a longer dwellMs may see one"
        : a.Outcome switch
        {
            ConversationOutcome.Answered => "was answered in " + Ms(a.Elapsed) + "ms with nothing to show"
                                            + (a.AnswerSizeBytes is { } bytes ? " (" + bytes + " bytes)" : string.Empty),
            ConversationOutcome.Failed => "failed" + (a.Detail is { Length: > 0 } d ? ": " + d : string.Empty),
            ConversationOutcome.TimedOut => "timed out without an answer",
            ConversationOutcome.Cancelled => "was cancelled before an answer came, which the editor does when the "
                                             + "pointer moves on",
            ConversationOutcome.Abandoned => "was left unanswered when the connection went away",
            _ => "ended without an outcome the capture names",
        };

    private static string NothingAsked(CodeEditorView.HoverRouteAt at, int dwellMs,
        IReadOnlyList<Registered> registered, string? extension)
    {
        if (dwellMs <= QuickInfoDelayMs)
            return "but the watch ended before the editor's " + QuickInfoDelayMs + "ms quick-info delay, so nothing had "
                   + "been asked yet; dwellMs must exceed " + QuickInfoDelayMs + " plus the server's round trip";
        if (at.DocumentUri is null)
            return "but the code window's document is not open with the language servers just now, so none was asked";
        var files = extension is null ? "this document" : "'" + extension + "' files";
        if (registered.Count == 0)
            return "and no language server is registered for " + files + ", so none was asked";
        var running = registered.Where(r => r.State == LanguageConnectionState.Running).ToList();
        if (running.Count == 0)
            return string.Join(" and ", registered.Select(r => "'" + r.Id + "' is " + State(r.State)))
                   + ", so no language server was asked";
        if (running.All(r => !r.AdvertisesHover))
            return string.Join(" and ", running.Select(r => "'" + r.Id + "'")) + " "
                   + (running.Count == 1 ? "is" : "are") + " running for " + files
                   + " but " + (running.Count == 1 ? "does" : "do") + " not advertise hoverProvider, so none was asked";
        // Registered, running and advertising, and still nothing recorded. Said as exactly that rather than given a
        // cause nobody observed.
        return "but no " + HoverMethod + " was recorded, though "
               + string.Join(" and ", running.Where(r => r.AdvertisesHover).Select(r => "'" + r.Id + "'"))
               + " is running for " + files + " and advertises hoverProvider; list_lsp_messages shows what was recorded";
    }

    private static string State(LanguageConnectionState state) => state switch
    {
        LanguageConnectionState.NotStarted => "not started",
        LanguageConnectionState.Starting => "still starting",
        LanguageConnectionState.Failed => "failed",
        LanguageConnectionState.Stopped => "stopped",
        _ => state.ToString(),
    };

    private static long Ms(TimeSpan? span) => span is { } s ? (long)Math.Round(s.TotalMilliseconds) : 0;
}
