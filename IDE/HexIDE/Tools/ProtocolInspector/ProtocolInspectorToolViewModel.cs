using System.Collections.ObjectModel;
using Dock.Model.Mvvm.Controls;
using HexIDE.Conversations;
using HexIDE.Localization;
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

    /// <summary>What the grid is showing, in the order it happened.</summary>
    public ObservableCollection<ProtocolInspectorRowViewModel> Rows { get; } = [];

    /// <summary>Every connection the capture knows, plus the all-connections entry, for the filter.</summary>
    public ObservableCollection<string> Connections { get; } = [AllConnections];

    [Notify] private string selectedConnection = AllConnections;
    [Notify] private bool failuresOnly;
    [Notify] private ProtocolInspectorRowViewModel? selectedRow;

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

    public ProtocolInspectorToolViewModel(ConversationLog capture, ILocalizationService localization)
    {
        _capture = capture;
        _localization = localization;

        localization.BindTitle(this, "Str.Tool.ProtocolInspector.Title");
        CanClose = true;
        CanFloat = false;

        RefreshCommand = new HexIDE.Utils.DelegateCommand(Refresh);

        Refresh();
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
    private void OnSelectedConnectionChanged() => Refresh();

    private void OnFailuresOnlyChanged() => Refresh();
}
