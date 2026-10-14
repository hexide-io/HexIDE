using System.Text;
using System.Text.Json;

namespace HexIDE.Conversations;

/// <summary>How many copies of one document a conversation carries, and how many bytes that came to.</summary>
/// <param name="Document">
/// What to show the reader: the last segment of the URI, which is the filename of a saved document and the
/// document's own name for one with no file yet. Where two documents in this conversation share that, it gains
/// its parent segment and stops there, so two whose last two segments match still read alike — see
/// <c>DisplayNameFor</c>.
/// </param>
/// <param name="Uri">
/// What the counting is grouped by: the whole name as the wire carried it.
/// <b>Not the display name, and the distinction is the point.</b> Two projects may each hold a
/// <c>Module1</c>, and so may two directories; grouping by the leaf merged them into one row whose copy
/// count and byte total belonged to neither. A disclosure exists to be weighed, so a number that is the sum
/// of two unrelated things is worse than no number.
/// </param>
public sealed record DisclosedDocument(string Document, string Uri, int Copies, long Bytes);

/// <summary>
/// What an export is about to disclose, in terms a person can weigh.
/// </summary>
/// <remarks>
/// <b>A message count is not a disclosure.</b> "Ten messages" tells the owner of the files nothing about
/// what is in them; "forty-seven copies of Form1, two megabytes of your source" tells them exactly what
/// they are about to hand somebody, and it is the only form of the question they can actually answer. Full
/// document synchronisation sends the whole file on every keystroke burst, so the copy count is usually the
/// surprising number and the one worth leading with.
///
/// <para>
/// <b>Counted from the retained bodies, and honest about the ones it does not have.</b> An envelope whose
/// body was never kept cannot be attributed to a document, so it is counted apart rather than folded in or
/// silently dropped — a disclosure that under-reports is worse than none, because it is believed.
/// </para>
///
/// <para>
/// The document name is read from the message rather than from the redactor's output on purpose: this is
/// shown to the person who owns the file, and it has to be the name they recognise. A pseudonym would make
/// the one surface addressed to that person the one they cannot read.
/// </para>
///
/// <para>
/// <b>That used to come with a reassurance that is no longer true.</b> It said a VB6 document's traffic
/// rode an opaque scheme carrying only a component name, so there was no path in it to replace and the two
/// answers coincided. Since hexide-io/HexIDE#273 a document is named by its file, so they diverge for every
/// saved document and this is now a deliberate choice between them rather than a coincidence. Nothing here
/// reaches an export — the export takes the redactor's output; this is the preview shown before one is
/// written, so that the person deciding can see what they are deciding about.
/// </para>
/// </remarks>
/// <param name="Messages">Everything the export will contain, one line each.</param>
/// <param name="Documents">Per document, how many copies of it are in there and how large they were.</param>
/// <param name="DocumentBytes">The total of <paramref name="Documents"/>, which is the source-code total.</param>
/// <param name="RetainedBytes">Every retained body, document content and protocol chatter together.</param>
/// <param name="MessagesWithNoBody">
/// Envelopes going out as a placeholder line. Nothing of their content is disclosed, and saying so is what
/// stops the other numbers reading as the whole story.
/// </param>
/// <param name="UnattributedMessages">
/// Messages that carry document content but whose body was not retained, so which document they belonged
/// to is not known. Reported rather than assumed.
/// </param>
public sealed record ConversationDisclosure(
    int Messages,
    IReadOnlyList<DisclosedDocument> Documents,
    long DocumentBytes,
    long RetainedBytes,
    int MessagesWithNoBody,
    int UnattributedMessages)
{
    /// <summary>True when the export carries the contents of at least one of the reader's files.</summary>
    public bool CarriesDocuments => DocumentBytes > 0 || UnattributedMessages > 0;

    /// <summary>
    /// The methods that carry a document's text.
    /// </summary>
    /// <remarks>
    /// Enumerated rather than guessed at from the body, because "does this JSON contain source code" is not
    /// a question a content-agnostic reader can answer, and a wrong answer here understates a disclosure.
    /// These three are the ones that put a file's contents on the wire; everything else names a document
    /// and asks about it.
    /// </remarks>
    private static readonly string[] CarryText =
    [
        "textDocument/didOpen",
        "textDocument/didChange",
        "textDocument/didSave",
    ];

    /// <summary>What exporting this capture would put in somebody else's hands.</summary>
    public static async Task<ConversationDisclosure> OfAsync(ConversationLog log, string? connectionId = null)
    {
        // Drained first, exactly as every other reader of this record is. A disclosure short by the frames
        // the reader has just provoked would describe a different export from the one about to be written.
        await log.DrainAsync().ConfigureAwait(false);

        // Keyed by the whole URI, not by its last segment. Ordinal because two spellings differing only
        // in case are two strings on the wire, and merging them here would hide exactly the normalisation
        // defect the rest of this subsystem goes out of its way to preserve.
        var perDocument = new Dictionary<string, (int Copies, long Bytes)>(StringComparer.Ordinal);
        var messages = 0;
        var noBody = 0;
        var unattributed = 0;
        long retained = 0;

        foreach (var envelope in log.Snapshot(connectionId))
        {
            messages++;

            var body = log.Body(envelope.ConnectionId, envelope.Sequence);
            if (body is null)
            {
                noBody++;
                continue;
            }

            retained += body.RetainedBytes;

            if (!CarriesText(envelope.Method)) continue;

            // The TRUE length, not the retained one. A truncated body still put the whole file on the
            // wire, and the disclosure is about what was sent rather than about what this record kept.
            if (DocumentOf(body.Head) is { Length: > 0 } document)
            {
                var (copies, bytes) = perDocument.GetValueOrDefault(document);
                perDocument[document] = (copies + 1, bytes + body.TrueLength);
            }
            else
            {
                unattributed++;
            }
        }

        // The answers go out too, so they are counted here or this understates the export — and a
        // disclosure that understates is the only kind that does harm. They are counted as messages and
        // as bytes, and deliberately not attributed to a document: a reply carries a range, a hover or a
        // capability table, never the file, and guessing otherwise would inflate the one figure the reader
        // is actually deciding on.
        foreach (var envelope in log.Snapshot(connectionId))
        {
            if (envelope.AnswerSequence is not { } answer) continue;

            messages++;

            if (log.Body(envelope.ConnectionId, answer) is { } reply) retained += reply.RetainedBytes;
            else noBody++;
        }

        var uris = perDocument.Keys.ToList();
        var documents = perDocument
            .Select(pair => new DisclosedDocument(
                DisplayNameFor(pair.Key, uris), pair.Key, pair.Value.Copies, pair.Value.Bytes))
            .OrderByDescending(d => d.Bytes)
            .ThenBy(d => d.Document, StringComparer.Ordinal)
            // The URI breaks the remaining tie, so the order cannot depend on dictionary iteration: two
            // documents may now share a display name AND a byte count, which the leaf-keyed version could
            // not produce because it had already merged them.
            .ThenBy(d => d.Uri, StringComparer.Ordinal)
            .ToList();

        return new ConversationDisclosure(
            Messages: messages,
            Documents: documents,
            DocumentBytes: documents.Sum(d => d.Bytes),
            RetainedBytes: retained,
            MessagesWithNoBody: noBody,
            UnattributedMessages: unattributed);
    }

    private static bool CarriesText(string? method) =>
        method is { Length: > 0 } && Array.IndexOf(CarryText, method) >= 0;

    /// <summary>
    /// The document a message is about, named the way its reader would name it.
    /// </summary>
    /// <remarks>
    /// <b>Read from a PARTIAL document, and that is the case that matters rather than an edge.</b> Full
    /// synchronisation of a large form exceeds the frame cap, so the bodies most worth disclosing are
    /// exactly the ones this record kept only the head of — and a whole-document parse would refuse every
    /// one of them, leaving the biggest files in the export unnamed. The URI sits near the front of these
    /// messages, well inside any head, so a forward scan finds it and stops.
    ///
    /// <para>
    /// A body this client could not decode at all comes back null and is counted as unattributed rather
    /// than guessed at: a disclosure that invents a filename is worse than one that admits it does not
    /// know which file this was.
    /// </para>
    /// </remarks>
    private static string? DocumentOf(byte[] head)
    {
        try
        {
            // isFinalBlock: false — a truncated head is not malformed JSON, it is unfinished JSON, and the
            // reader is the one component that can tell the difference. It simply stops when it runs out.
            var reader = new Utf8JsonReader(head, isFinalBlock: false, state: default);
            var path = new Stack<string>();
            string? property = null;

            while (reader.Read())
            {
                switch (reader.TokenType)
                {
                    case JsonTokenType.PropertyName:
                        property = reader.GetString();
                        continue;

                    case JsonTokenType.StartObject:
                    case JsonTokenType.StartArray:
                        path.Push(property ?? "");
                        break;

                    case JsonTokenType.EndObject:
                    case JsonTokenType.EndArray:
                        if (path.Count > 0) path.Pop();
                        break;

                    case JsonTokenType.String
                        when property == "uri"
                             && path.Count > 0
                             && path.Peek() == "textDocument":
                        // The WHOLE URI. It is the grouping key, and the reader-facing name is derived from
                        // it afterwards, once every document in the conversation is known -- which is the
                        // only point at which "does this name collide" can be answered.
                        return reader.GetString();
                }

                property = null;
            }
        }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// The name to show for one document: its last segment, or its last two where that alone would collide
    /// within this conversation.
    /// </summary>
    /// <remarks>
    /// <b>The leaf is the right answer almost always, and wrong in the one case grouping by URI created.</b>
    /// A saved document's leaf is its filename and a pathless one's is its own name, which is what its owner
    /// calls it. But two projects may each hold a <c>Module1</c>, and so may two directories — and now that
    /// those count as two documents, showing both as <c>Module1.bas</c> would present the reader with two
    /// identical rows carrying different numbers and no way to tell which is which.
    ///
    /// <para>
    /// So a colliding name gains its parent segment, which is the project's name for an <c>untitled:</c>
    /// document and the containing directory for a saved one. It stops there rather than widening to the
    /// whole path: the point is to distinguish two rows for someone who already knows their own project, not
    /// to name the file absolutely, and a disclosure listing full paths would be unreadable at exactly the
    /// moment it matters.
    /// </para>
    /// </remarks>
    private static string DisplayNameFor(string uri, IReadOnlyList<string> all)
    {
        var leaf = Leaf(uri) ?? uri;

        var collides = false;
        foreach (var other in all)
        {
            if (ReferenceEquals(other, uri) || other == uri) continue;
            if (!string.Equals(Leaf(other) ?? other, leaf, StringComparison.Ordinal)) continue;
            collides = true;
            break;
        }

        return collides ? Trail(uri) : leaf;
    }

    /// <summary>The last two segments of a URI, which is as far as the display name ever widens.</summary>
    /// <remarks>
    /// The scheme is dropped first. An <c>untitled:</c> URI has no <c>//</c> after its colon, so its scheme
    /// is part of the first path segment and a naive split hands back <c>untitled:Ledger/Module1.bas</c> --
    /// the wire name, in the one place whose whole job is to show a name its reader recognises.
    /// </remarks>
    private static string Trail(string uri)
    {
        var colon = uri.IndexOf(':');
        var path = colon >= 0 && colon < uri.Length - 1 ? uri[(colon + 1)..] : uri;

        var segments = path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return Unescape(uri);

        var take = segments[^Math.Min(2, segments.Length)..];

        return string.Join('/', take.Select(Unescape));
    }

    /// <summary>
    /// The last segment of a URI, whatever its scheme.
    /// </summary>
    /// <remarks>
    /// A saved document's URI is a real path whose leaf is the filename; a document with no file yet is
    /// <c>untitled:&lt;Project&gt;/&lt;Name&gt;.&lt;ext&gt;</c>, whose leaf is the name the developer typed.
    /// Both are what the reader calls the thing, which is the only name worth showing to the person who owns
    /// it.
    /// </remarks>
    private static string? Leaf(string? uri)
    {
        if (uri is not { Length: > 0 }) return null;

        var cut = uri.LastIndexOfAny(['/', '\\']);
        var leaf = cut >= 0 && cut < uri.Length - 1 ? uri[(cut + 1)..] : uri;

        return Unescape(leaf);
    }

    /// <summary>
    /// A percent-encoded segment as a person reads it.
    /// </summary>
    /// <remarks>
    /// Undone here and nowhere else: an <c>untitled:</c> name is percent-encoded when it is minted, so a
    /// project called <c>Bill of Fare</c> reaches the wire as <c>Bill%20of%20Fare</c> and would be shown
    /// that way. The grouping key keeps the encoded form, because that is what was sent.
    /// </remarks>
    private static string Unescape(string segment)
    {
        try { return Uri.UnescapeDataString(segment); }
        catch (UriFormatException) { return segment; }
    }

    /// <summary>A byte count as a person reads one.</summary>
    /// <remarks>
    /// Here rather than in a view model because the disclosure's whole reason to exist is that it is
    /// legible, and two callers rendering "2.1 MB" differently would undermine the one property it has.
    /// </remarks>
    public static string Bytes(long bytes) => bytes < 1024
        ? $"{bytes} B"
        : bytes < 1024 * 1024
            ? $"{bytes / 1024.0:N1} KB"
            : $"{bytes / (1024.0 * 1024):N1} MB";

    /// <summary>The documents, as one phrase — the sentence the design asked for.</summary>
    /// <remarks>
    /// Machine text: filenames and counts. It is deliberately not a localisation key with a placeholder per
    /// document, because the number of documents is not known in advance and no pack can carry a template
    /// for it. The surrounding sentence IS translated; this is the list it names.
    /// </remarks>
    public string DocumentSummary()
    {
        if (Documents.Count == 0) return "";

        // With one document the surrounding sentence has already given the total, so repeating it here
        // reads as two different numbers that happen to agree. Measured against the running window.
        var single = Documents.Count == 1;

        var text = new StringBuilder();
        foreach (var document in Documents)
        {
            if (text.Length > 0) text.Append(", ");

            var size = single ? "" : $" ({Bytes(document.Bytes)})";
            text.Append(document.Copies == 1
                ? $"{document.Document}{size}"
                : $"{document.Copies} copies of {document.Document}{size}");
        }

        return text.ToString();
    }
}
