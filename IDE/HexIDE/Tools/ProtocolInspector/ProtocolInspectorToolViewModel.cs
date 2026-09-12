using System.Collections.ObjectModel;
using Avalonia.Platform.Storage;
using HexIDE.Forms.ViewModels;
using Dock.Model.Mvvm.Controls;
using HexIDE.Conversations;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Redaction;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools.ProtocolInspector;

/// <summary>
/// What actually went over the wire to a language server.
/// </summary>
/// <remarks>
/// <b>The audience is a language-server author, and users are served as a consequence.</b> The connection
/// list already answers a user's question — what is attached, how far each got, what it advertised. What
/// nothing answered is what was actually said, which is the author's question, and it is also the question
/// that separates "we never asked" from "the answer came back empty" from "the answer was fine and the
/// panel rendered it wrong". Only the last of those is a defect in the editor.
///
/// <para>
/// <b>One interleaved timeline with the server as a filter, not one server at a time.</b> This IDE routes a
/// document to every server that claims it and merges the answers, so "which of you answered, and was the
/// other even asked" is a native question here in a way it is not in editors that assume one server per
/// language. A per-server channel cannot express it.
/// </para>
///
/// <para>
/// <b>It never refreshes itself.</b> A grid that moved while being read would be unusable for the thing it
/// is for — comparing two rows — and a capture is not a log to watch scroll. Refresh is an action, and the
/// header says how many entries arrived since the last one so the reader knows there is something to get.
/// </para>
///
/// <para>
/// A <see cref="Document"/> rather than a docked <see cref="Tool"/>: the content is wide and
/// column-shaped, the document region gives it most of the window, and it is the same choice the
/// connection list made for the same reasons.
/// </para>
/// </remarks>
public partial class ProtocolInspectorToolViewModel : Document
{
    /// <summary>The filter entry meaning "every connection", which is the default.</summary>
    /// <remarks>
    /// A real id can never collide with it: an id comes from a configuration file and this is not a legal
    /// one, being parenthesised. Held as a constant rather than repeated so the comparison and the display
    /// cannot drift apart.
    /// </remarks>
    public const string AllConnections = "(all)";

    private readonly ConversationLog _capture;
    private readonly ILocalizationService _localization;
    private readonly Pseudonymiser _pseudonyms;
    private readonly IWindowManager _windows;

    /// <summary>What the grid is showing, in the order it happened.</summary>
    public ObservableCollection<ProtocolInspectorRowViewModel> Rows { get; } = [];

    /// <summary>Every connection the capture knows, plus the all-connections entry, for the filter.</summary>
    public ObservableCollection<string> Connections { get; } = [AllConnections];

    [Notify] private string selectedConnection = AllConnections;
    [Notify] private bool failuresOnly;
    [Notify] private ProtocolInspectorRowViewModel? selectedRow;

    /// <summary>
    /// The selected message's body, as the bytes that crossed the wire.
    /// </summary>
    /// <remarks>
    /// <b>Raw, never redacted.</b> The live view belongs to the person who owns the files and needs no
    /// protection from their own paths; redacting here would also break the one affordance that proves
    /// what actually crossed the wire, which is the whole reason to open a row. Redaction governs egress,
    /// and export is where that boundary sits.
    /// </remarks>
    [Notify] private string selectedBody = "";

    /// <summary>Which message the pane is showing, in the terms the grid used.</summary>
    [Notify] private string selectedBodyHeader = "";

    /// <summary>
    /// Why there is no body, when there is not.
    /// </summary>
    /// <remarks>
    /// <b>Three states a blank pane would collapse</b>, and telling them apart is one of the questions the
    /// design record left open rather than guessed at: nothing was kept because the connection was not
    /// armed, something was kept and has since been evicted, or the row is a note that never had a body at
    /// all. The capture cannot attribute one envelope to the first two, and says so rather than inventing
    /// an answer.
    /// </remarks>
    [Notify] private string selectedBodyUnavailable = "";

    [Notify] private bool hasSelectedBody;

    /// <summary>Whether the pane is showing at all. Nothing selected means no pane.</summary>
    [Notify] private bool isDetailOpen;

    /// <summary>
    /// How much of the window the detail pane takes.
    /// </summary>
    /// <remarks>
    /// <b>A star share rather than a content-sized row, and that was measured rather than reasoned.</b>
    /// Sized to its content, the pane was fine while a body was one wrapped paragraph and then swallowed
    /// the entire window the moment the pane showed an indented forty-line trace — the grid was pushed off
    /// screen with no scrollbar and no way back. A star row keeps its share whatever is in it and lets the
    /// text scroll inside, which is what a splitter is for.
    ///
    /// <para>
    /// Zero rather than collapsed, because a star row reserves its share even when its content is hidden.
    /// Held as a value the row binds to so the pane can genuinely disappear when nothing is selected.
    /// </para>
    /// </remarks>
    [Notify] private Avalonia.Controls.GridLength detailHeight = new(0);

    /// <summary>
    /// The selected message as the text trace a server author already reads, ready to be pasted.
    /// </summary>
    /// <remarks>
    /// <b>Redacted, unlike the pane above it, and the difference is the whole rule.</b> The pane is the
    /// developer looking at their own machine; this is the thing that leaves it. Held as a property rather
    /// than composed inside the copy handler so it can be asserted on — by a test, and by an automation
    /// client, which cannot read a clipboard.
    /// </remarks>
    [Notify] private string selectedTrace = "";

    /// <summary>Set while there is a message to copy, so the button can say so.</summary>
    [Notify] private bool canCopy;

    /// <summary>
    /// Why the selected body is not valid JSON, when it is not.
    /// </summary>
    /// <remarks>
    /// <b>Only the failure is shown, and that is the whole design of it.</b> This is service-to-service
    /// traffic: it parses essentially always, so a tick on every message would be a badge nobody reads
    /// beside the one that matters. Silence means valid — the same shape the losses banner already uses,
    /// for the same reason a warning on every clean capture would be ignored on the one that is not.
    ///
    /// <para>
    /// A server emitting malformed JSON is a real defect and this window is where it would surface, so it
    /// is worth saying loudly when it happens. A truncated body is exempt: it is not meant to parse, it
    /// says so already, and reporting it as broken would be the inspector misreading its own output.
    /// </para>
    /// </remarks>
    [Notify] private string bodyProblem = "";

    [Notify] private bool hasBodyProblem;

    /// <summary>
    /// Whether the pane is showing what would leave rather than what crossed the wire.
    /// </summary>
    /// <remarks>
    /// <b>The preview for the copy action, and it is a toggle rather than a dialog.</b> Every outbound gate
    /// in this tree shows its payload first, and a preview is the only thing that catches a secret sitting
    /// in a string literal — no content-agnostic redactor will ever find one. A modal on every copy would
    /// be dismissed unread within a day; a switch on the pane the reader is already looking at is the same
    /// disclosure at a cost they will keep paying.
    ///
    /// <para>
    /// Off by default, because the raw bytes are what the window is for. What is on screen is labelled
    /// either way, so neither state can be mistaken for the other.
    /// </para>
    /// </remarks>
    [Notify] private bool showsWhatWouldBeShared;

    /// <summary>What the pane is actually rendering: the raw body, or the redacted trace.</summary>
    /// <remarks>
    /// One property rather than two visibility-swapped text boxes, so the scroll position, the selection
    /// and the copy behaviour belong to one control and the toggle reads as a change of view rather than a
    /// change of place.
    /// </remarks>
    [Notify] private string paneText = "";

    /// <summary>Where the last export went, or why it did not go.</summary>
    /// <remarks>
    /// Stated in the window rather than as a transient notice: an export exists to be handed to somebody
    /// else, and the first thing its author needs is the path. A notice that fades is gone by the time
    /// they look for it.
    /// </remarks>
    [Notify] private string exportStatus = "";

    /// <summary>Set while an export is running, so a second one cannot be started on top of it.</summary>
    [Notify] private bool isExporting;

    /// <summary>How many rows the grid holds, and how many of those are failures.</summary>
    [Notify] private int shownCount;
    [Notify] private int failureCount;

    /// <summary>
    /// The counters as text and as visibility, rather than as numbers a view has to convert.
    /// </summary>
    /// <remarks>
    /// Formatted here because the format string is a localisation key and the view has no business
    /// composing one; exposed as booleans as well because an integer bound to <c>IsVisible</c> is a
    /// coercion Avalonia does not do, and a converter for it would be three more things to keep in step.
    /// </remarks>
    [Notify] private string shownSummary = "";
    [Notify] private string failureSummary = "";
    [Notify] private string pendingSummary = "";
    [Notify] private bool hasFailures;
    [Notify] private bool hasPending;

    /// <summary>Set when the capture has entries the grid has not been shown.</summary>
    /// <remarks>
    /// The alternative to refreshing underneath the reader. A count rather than a dot, because "there are
    /// four more" and "there are four thousand more" are different situations.
    /// </remarks>
    [Notify] private int pendingCount;

    [Notify] private bool nothingToShow = true;

    /// <summary>
    /// Why the grid is empty, when it is.
    /// </summary>
    /// <remarks>
    /// The same reasoning the automation surface already carries: an empty grid cannot be told apart from
    /// "nothing happened", "nothing is configured" and "this is broken", and that ambiguity is the whole
    /// thing the inspector exists to remove. Saying so costs a line of text.
    /// </remarks>
    [Notify] private string emptyReason = "";

    /// <summary>What the capture has had to discard, per the filtered connection.</summary>
    [Notify] private string losses = "";

    /// <summary>Re-reads the capture. The only thing that changes what the grid shows.</summary>
    public System.Windows.Input.ICommand RefreshCommand { get; }

    /// <summary>Writes the whole conversation out as raw JSON-RPC plus a manifest.</summary>
    public System.Windows.Input.ICommand ExportCommand { get; }

    public ProtocolInspectorToolViewModel(
        ConversationLog capture,
        ILocalizationService localization,
        Pseudonymiser pseudonyms,
        IWindowManager windows)
    {
        _capture = capture;
        _localization = localization;
        _pseudonyms = pseudonyms;
        _windows = windows;

        localization.BindTitle(this, "Str.Tool.ProtocolInspector.Title");
        CanClose = true;
        CanFloat = false;

        RefreshCommand = new HexIDE.Utils.DelegateCommand(Refresh);

        // The tab's title rebinds itself, but everything this view model COMPOSES was composed once. A
        // window switched to German while open kept saying "9 shown" beside a fully translated header —
        // found by switching the language against the running IDE, which is the only way it shows.
        localization.LanguageChanged += Retranslate;
        ExportCommand = new HexIDE.Utils.DelegateCommand(() => _ = ExportAsync(), () => !IsExporting);

        Refresh();
    }

    /// <summary>
    /// Narrows to one server, or widens back when it is not one this capture knows.
    /// </summary>
    /// <remarks>
    /// <b>Falls back to every server rather than showing an empty grid.</b> An id can arrive for a
    /// connection that has since gone, and a window opened from a link that then showed nothing would read
    /// as the capture being broken — which is the one reading this whole feature exists to prevent. The
    /// filter list carries every connection the capture knows, so membership is the honest test.
    /// </remarks>
    public void ShowOnly(string connectionId)
    {
        RefreshConnections();

        SelectedConnection = Connections.Contains(connectionId) ? connectionId : AllConnections;
    }

    /// <summary>Re-reads the capture. The only thing that changes what the grid shows.</summary>
    public void Refresh()
    {
        // Drained first, exactly as every automation reader does. The tap hands frames to a channel and
        // never waits, so a snapshot taken without draining is short by the frames the reader has just
        // provoked — which are the only ones they are looking for.
        var page = CaptureQueries.ListAsync(_capture, Filter()).GetAwaiter().GetResult();

        Rows.Clear();
        foreach (var envelope in page.Entries)
        {
            Rows.Add(new ProtocolInspectorRowViewModel(
                envelope, _capture.Body(envelope.ConnectionId, envelope.Sequence) is not null));
        }

        ShownCount = Rows.Count;
        FailureCount = Rows.Count(r => r.IsFailure);
        PendingCount = 0;
        NothingToShow = Rows.Count == 0;
        EmptyReason = NothingToShow ? page.Note ?? "" : "";

        RefreshCounters();

        RefreshConnections();
        RefreshLosses();
    }

    /// <summary>How many entries have arrived since the grid was last filled.</summary>
    /// <remarks>
    /// Cheap and deliberately approximate: it counts the whole record rather than the filtered view, so it
    /// can say "there is more" without doing the work of deciding whether the reader would care. Claiming
    /// precision here would mean running the filter on every frame.
    /// </remarks>
    public void NoteTraffic()
    {
        var total = _capture.Snapshot().Count;
        var shown = Rows.Count;
        if (total > shown) PendingCount = total - shown;

        RefreshCounters();
    }

    private void RefreshCounters()
    {
        ShownSummary = Text("Str.Tool.ProtocolInspector.Shown", ShownCount);
        FailureSummary = Text("Str.Tool.ProtocolInspector.Failed", FailureCount);
        PendingSummary = Text("Str.Tool.ProtocolInspector.Pending", PendingCount);
        HasFailures = FailureCount > 0;
        HasPending = PendingCount > 0;
    }

    private string Text(string key, int count) =>
        _localization.GetString(key) is { Length: > 0 } format
            ? string.Format(format, count)
            : count.ToString();

    private EnvelopeFilter Filter() => new(
        ConnectionId: SelectedConnection == AllConnections ? null : SelectedConnection,
        FailuresOnly: FailuresOnly,
        // Every entry the ring holds. The ring is already the limit — capped in entries, per connection,
        // with the handshake pinned — so a second cap here would hide material the record deliberately
        // kept and would have to be explained twice.
        Limit: int.MaxValue);

    private void RefreshConnections()
    {
        var known = _capture.ConnectionIds.Order(StringComparer.Ordinal).ToList();

        // Rebuilt only when it has actually changed. Replacing the collection wholesale would reset the
        // combo's selection on every refresh, which is the sort of thing that makes a filter feel broken.
        if (Connections.Count == known.Count + 1
            && known.Select((id, i) => Connections[i + 1] == id).All(same => same))
        {
            return;
        }

        var selected = SelectedConnection;

        Connections.Clear();
        Connections.Add(AllConnections);
        foreach (var id in known) Connections.Add(id);

        SelectedConnection = Connections.Contains(selected) ? selected : AllConnections;
    }

    private void RefreshLosses()
    {
        var ids = SelectedConnection == AllConnections
            ? _capture.ConnectionIds
            : [SelectedConnection];

        long envelopes = 0, bodies = 0, refused = 0;
        foreach (var id in ids)
        {
            var (e, b, r) = _capture.Losses(id);
            envelopes += e;
            bodies += b;
            refused += r;
        }

        var dropped = _capture.QueueDropped;

        // Said rather than left to be inferred from a short list. A record that truncated silently reads
        // exactly like a complete one, which is the failure this whole design refuses.
        Losses = envelopes + bodies + refused + dropped == 0
            ? ""
            : _localization.GetString("Str.Tool.ProtocolInspector.Losses") is { Length: > 0 } format
                ? string.Format(format, envelopes, bodies, refused, dropped)
                : "";
    }

    /// <summary>
    /// Loads the selected row's body, or says why there is none.
    /// </summary>
    /// <remarks>
    /// Synchronous for the same reason the filter handlers are: a selection change arrives from the grid,
    /// which is already on the UI thread, and a caller reading the pane straight after selecting expects
    /// to see that row.
    /// </remarks>
    private void OnSelectedRowChanged()
    {
        if (SelectedRow is not { } row)
        {
            IsDetailOpen = false;
            HasSelectedBody = false;
            CanCopy = false;
            SelectedBody = "";
            SelectedTrace = "";
            SelectedBodyUnavailable = "";
            SelectedBodyHeader = "";
            BodyProblem = "";
            HasBodyProblem = false;
            DetailHeight = new Avalonia.Controls.GridLength(0);
            RefreshPane();
            return;
        }

        IsDetailOpen = true;
        DetailHeight = new Avalonia.Controls.GridLength(1, Avalonia.Controls.GridUnitType.Star);
        SelectedBodyHeader = Header(row);

        var view = CaptureQueries
            .FetchAsync(_capture, row.ConnectionId, row.Sequence)
            .GetAwaiter().GetResult();

        if (view is null)
        {
            HasSelectedBody = false;
            SelectedBody = "";
            SelectedBodyUnavailable = row.IsLocal
                // A note about the process is not a message and never had a body. Saying "nothing was
                // retained" here would imply something could have been.
                ? _localization.GetString("Str.Tool.ProtocolInspector.NoteHasNoBody")
                : CaptureQueries
                    .ExplainMissingBodyAsync(_capture, row.ConnectionId, row.Sequence)
                    .GetAwaiter().GetResult();

            // Still copyable. An envelope with no body is often exactly the finding — this was sent and
            // never answered — and a copy action that refused it would withhold the most quotable row
            // there is.
            SelectedTrace = Trace(row, null, SelectedBodyUnavailable);
            CanCopy = true;
            BodyProblem = "";
            HasBodyProblem = false;
            RefreshPane();
            return;
        }

        HasSelectedBody = true;
        SelectedBodyUnavailable = "";
        SelectedBody = Compose(view);
        SelectedTrace = Trace(row, Redactor().Body(SelectedBody), null);
        CanCopy = true;

        // Skipped for a shortened frame: it is not meant to parse, it already says so, and calling it
        // malformed would be the inspector misreading its own marker as the server's output.
        BodyProblem = view.Tail is null ? Malformed(SelectedBody) : "";
        HasBodyProblem = BodyProblem.Length > 0;

        RefreshPane();
    }

    /// <summary>Why a body does not parse, or empty when it does.</summary>
    /// <remarks>
    /// The message carries the position, because "invalid JSON" in a forty-kilobyte frame is a statement
    /// nobody can act on and the line and column are what make it one.
    /// </remarks>
    private string Malformed(string body)
    {
        if (body.Length == 0) return "";

        try
        {
            using var _ = System.Text.Json.JsonDocument.Parse(body);
            return "";
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Format("Str.Tool.ProtocolInspector.NotJson", ex.Message);
        }
    }

    /// <summary>
    /// One message in the borrowed trace shape, which is the shape a bug report wants.
    /// </summary>
    /// <remarks>
    /// The envelope is looked up rather than rebuilt from the row: a row is a projection for display —
    /// arrows, formatted sizes, a method column that falls back to a note — and pasting any of that into a
    /// report addressed to a server author would be quoting HexIDE's rendering back at them as though it
    /// were the protocol.
    /// </remarks>
    private string Trace(ProtocolInspectorRowViewModel row, string? body, string? unavailable)
    {
        foreach (var envelope in _capture.Snapshot(row.ConnectionId))
        {
            if (envelope.Sequence == row.Sequence)
                return ConversationTrace.ToTraceText(envelope, body, unavailable);
        }

        return "";
    }

    /// <summary>
    /// A redactor over the session's own pseudonym table.
    /// </summary>
    /// <remarks>
    /// <b>The table is shared with the automation surface's exports, and is session-scoped on purpose.</b>
    /// One path must get the same pseudonym everywhere it appears, or a reader holding two artefacts cannot
    /// tell that they describe the same file; and it must NOT outlive the session, or a pseudonym becomes a
    /// stable identifier for a real path, which is the thing being avoided.
    /// </remarks>
    private ConversationRedactor Redactor() => new(_pseudonyms);

    /// <summary>
    /// Writes the conversation out: one JSON-RPC message per line, and a manifest beside it.
    /// </summary>
    /// <remarks>
    /// <b>Always redacted, and there is deliberately no tick box here.</b> The design records that a
    /// non-pseudonymising mode should exist and that where it lands is an open question needing an
    /// unmissable warning; a quiet checkbox beside a Save button would settle that question by accident.
    /// The raw bytes are already one click away in the pane above, so nothing is unreachable — only
    /// unshareable by mistake.
    /// </remarks>
    public async Task ExportAsync()
    {
        if (IsExporting) return;
        IsExporting = true;

        // Cleared BEFORE the picker, not after a success. Otherwise a reader who exports, exports again
        // and cancels is left looking at the first export's line, which is true and reads as though the
        // second one had also gone out. Measured against the running window.
        ExportStatus = "";

        try
        {
            var connection = SelectedConnection == AllConnections ? null : SelectedConnection;

            // BUILT BEFORE ANYTHING IS ASKED, because the preview has to show the bytes rather than a
            // description of them. Nothing is written until the reader has seen them and chosen a path.
            var export = await ConversationExporter.ExportAsync(_capture, Redactor(), connection);
            var disclosure = await ConversationDisclosure.OfAsync(_capture, connection);

            var preview = new ExportPreviewDialogViewModel(disclosure, export, _localization);
            if (!await _windows.ShowDialog(preview)) return;

            var chosen = await _windows.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _localization.GetString("Str.Tool.ProtocolInspector.Export"),
                SuggestedFileName = "hexide-lsp-conversation.jsonl",
                DefaultExtension = "jsonl",
            });

            if (chosen is not { Length: > 0 }) return;

            // The manifest's name is DERIVED rather than asked for a second time. Two pickers for one
            // action is how the halves end up in different folders under different stems, and the manifest
            // cites the messages file by line number, so a separated pair is a broken one.
            var manifest = System.IO.Path.ChangeExtension(chosen, null) + ".manifest.json";

            await System.IO.File.WriteAllTextAsync(chosen, export.Messages);
            await System.IO.File.WriteAllTextAsync(manifest, export.Manifest);

            ExportStatus = Format("Str.Tool.ProtocolInspector.Exported", export.Lines, chosen, manifest);
        }
        catch (Exception ex)
        {
            // Said in the window rather than only in the log. An export that silently did nothing is
            // indistinguishable from one the reader cancelled, and they will act on the wrong one.
            ExportStatus = Format("Str.Tool.ProtocolInspector.ExportFailed", ex.Message);
        }
        finally
        {
            IsExporting = false;
        }
    }

    private string Format(string key, params object?[] arguments) =>
        _localization.GetString(key) is { Length: > 0 } format
            ? string.Format(format, arguments)
            : string.Join(" ", arguments);

    /// <summary>
    /// The body, with the gap stated where one was cut out.
    /// </summary>
    /// <remarks>
    /// A truncated frame is head and tail with the true length recorded, and it is NOT valid JSON. Joining
    /// the two halves would produce something that parses and lies about what was sent, so the gap is
    /// marked and measured instead — the same choice the export makes, for the same reason.
    /// </remarks>
    private string Compose(PayloadView view)
    {
        // INDENTED WHEN IT PARSES, and left exactly as it arrived when it does not.
        //
        // A frame is one line of JSON — 569 bytes of handshake wrapped across four lines is the thing
        // people copy out to a formatter, which is the outcome this pane exists to prevent. Re-indenting
        // changes only insignificant whitespace: nothing is added, removed or reordered, and the true byte
        // count is in the grid beside it. It is the same trade the exporter already makes in reverse, and
        // records for the same reason.
        //
        // A body that does NOT parse is shown byte for byte, because that body is the finding.
        if (view.Tail is not { } tail) return Readable(view.Head);

        // Bytes, not characters. TrueLength is what crossed the wire and a body is UTF-8; subtracting a
        // string length would under-report the gap on any non-ASCII payload, which is exactly the payload
        // a reader is least able to check by eye.
        var kept = System.Text.Encoding.UTF8.GetByteCount(view.Head)
                 + System.Text.Encoding.UTF8.GetByteCount(tail);
        var missing = view.TrueLength - kept;
        var note = _localization.GetString("Str.Tool.ProtocolInspector.Truncated") is { Length: > 0 } format
            ? string.Format(format, missing, view.TrueLength)
            : $"--- {missing} bytes not kept, of {view.TrueLength} on the wire ---";

        return view.Head + "\n\n" + note + "\n\n" + tail;
    }

    /// <summary>The body indented, or the body, if it will not parse.</summary>
    private static string Readable(string json)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var buffer = new MemoryStream();
            using (var writer = new System.Text.Json.Utf8JsonWriter(
                       buffer, new System.Text.Json.JsonWriterOptions { Indented = true }))
            {
                document.WriteTo(writer);
            }

            return System.Text.Encoding.UTF8.GetString(buffer.ToArray());
        }
        catch (System.Text.Json.JsonException)
        {
            return json;
        }
    }

    private string Header(ProtocolInspectorRowViewModel row)
    {
        var method = string.IsNullOrEmpty(row.Method) ? row.Kind : row.Method;
        return _localization.GetString("Str.Tool.ProtocolInspector.DetailHeader") is { Length: > 0 } format
            ? string.Format(format, row.Sequence, row.ConnectionId, method)
            : $"#{row.Sequence}  {row.ConnectionId}  {method}";
    }

    /// <summary>
    /// Re-reads when the filter changes. Synchronously, and that is deliberate.
    /// </summary>
    /// <remarks>
    /// <b>Not posted to the dispatcher, unlike the connection list's registry handler.</b> That one is
    /// posted because the registry raises from transport and RPC callbacks, which are not the UI thread.
    /// A filter change arrives from a combo box or a checkbox, which already is — so posting would buy
    /// nothing and cost the one thing a caller expects: that reading the rows straight after setting the
    /// filter shows the filtered rows.
    ///
    /// <para>
    /// Written the other way first, and two tests caught it immediately by observing an unfiltered grid.
    /// The same shape failed on CI earlier the same day in the connection list, where a test raised an
    /// event and asserted synchronously on work the view model had posted — worth recording, because the
    /// posting looks like the safe default and is only correct when the caller might be off-thread.
    /// </para>
    /// </remarks>
    /// <summary>
    /// Rebuilds every string this view model composed, in the language now in force.
    /// </summary>
    /// <remarks>
    /// <b>Not the export status.</b> That describes something that already happened, names two paths, and
    /// re-rendering last hour's outcome in a new language would claim it happened in it. Everything else
    /// here describes the present.
    /// </remarks>
    private void Retranslate()
    {
        RefreshCounters();
        RefreshLosses();

        // The detail pane's header, its no-body explanation and its trace all carry translated text, and
        // the cheapest way to rebuild all three correctly is the path that built them.
        if (SelectedRow is not null) OnSelectedRowChanged();
    }

    private void OnShowsWhatWouldBeSharedChanged() => RefreshPane();

    private void RefreshPane() => PaneText = ShowsWhatWouldBeShared ? SelectedTrace : SelectedBody;

    private void OnSelectedConnectionChanged() => Refresh();

    private void OnFailuresOnlyChanged() => Refresh();
}
