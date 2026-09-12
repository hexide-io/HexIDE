using System.Text;
using System.Text.Json;

namespace HexIDE.Conversations;

/// <summary>How many copies of one document a conversation carries, and how many bytes that came to.</summary>
/// <param name="Document">
/// The document as the wire named it — the last segment of its URI, which for a VB6 form or module is the
/// component name the reader already knows it by.
/// </param>
public sealed record DisclosedDocument(string Document, int Copies, long Bytes);

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
/// shown to the person who owns the file, and it has to be the name they recognise. For a VB6 form or
/// module the two are the same anyway — that traffic rides an opaque scheme carrying only a component
/// name, so there is no path in it to replace.
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

        var documents = perDocument
            .Select(pair => new DisclosedDocument(pair.Key, pair.Value.Copies, pair.Value.Bytes))
            .OrderByDescending(d => d.Bytes)
            .ThenBy(d => d.Document, StringComparer.Ordinal)
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
                        return Leaf(reader.GetString());
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
    /// The last segment of a URI, whatever its scheme.
    /// </summary>
    /// <remarks>
    /// A VB6 document's URI is opaque and carries only the component name; a carried file's is a real path
    /// whose leaf is the filename. Both are what the reader calls the thing, which is the only name worth
    /// showing to the person who owns it.
    /// </remarks>
    private static string? Leaf(string? uri)
    {
        if (uri is not { Length: > 0 }) return null;

        var cut = uri.LastIndexOfAny(['/', '\\']);
        var leaf = cut >= 0 && cut < uri.Length - 1 ? uri[(cut + 1)..] : uri;

        try { return Uri.UnescapeDataString(leaf); }
        catch (UriFormatException) { return leaf; }
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
