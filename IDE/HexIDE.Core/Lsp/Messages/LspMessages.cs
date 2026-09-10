using System.Text.Json.Serialization;

namespace HexIDE.Lsp.Messages;

public record Position(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);

public record Range(
    [property: JsonPropertyName("start")] Position Start,
    [property: JsonPropertyName("end")] Position End);

public enum DiagnosticSeverity
{
    Error = 1,
    Warning = 2,
    Information = 3,
    Hint = 4
}

public record Diagnostic(
    [property: JsonPropertyName("range")] Range Range,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("severity")] DiagnosticSeverity? Severity = null,
    [property: JsonPropertyName("source")] string? Source = null);

public record TextDocumentItem(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("languageId")] string LanguageId,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("text")] string Text);

public record VersionedTextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("version")] int Version);

public record TextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri);

public record TextDocumentContentChangeEvent(
    [property: JsonPropertyName("text")] string Text);

public record DidOpenTextDocumentParams(
    [property: JsonPropertyName("textDocument")] TextDocumentItem TextDocument);

public record DidChangeTextDocumentParams(
    [property: JsonPropertyName("textDocument")] VersionedTextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("contentChanges")] TextDocumentContentChangeEvent[] ContentChanges);

public record DidCloseTextDocumentParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

/// <summary>
/// A document was written to disk.
///
/// <para>
/// The identifier is the <b>unversioned</b> one: a save changes no version, because it changes no
/// content — it is an announcement about the text the server already has.
/// </para>
///
/// <para>
/// <b><c>text</c> must be ABSENT when the server did not ask for it, not null.</b> A server tests
/// whether the field is present to choose between reading the file from disk and using what it was
/// handed, so a null in place of an absent field selects the wrong branch — and silently. The ignore
/// condition is per-property rather than a serializer-wide default deliberately: a global setting would
/// change the wire shape of every outbound type, including a root URI that is legitimately nullable and
/// that no test pins.
/// </para>
/// </summary>
public record DidSaveTextDocumentParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("text")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Text = null);

public record PublishDiagnosticsParams(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("diagnostics")] Diagnostic[] Diagnostics);

/// <summary>
/// How serious a server says its own message is. The protocol's numbering, which is <b>not</b> the same as
/// <c>DiagnosticSeverity</c>'s despite looking like it. It carries a fifth level that scale has no
/// counterpart for, and no value here means "hint".
/// </summary>
/// <remarks>
/// <b>This scale gets quieter as its numbers rise</b>, which is the opposite of every other severity in the
/// protocol and the reason an unrecognised value must not be treated as ordinary. A number this client does
/// not know is one the specification added <em>after</em> the noisiest level here, so it belongs below them
/// all rather than in the middle.
/// </remarks>
public enum LspMessageType
{
    Error = 1,
    Warning = 2,
    Info = 3,
    Log = 4,

    /// <summary>Added in 3.18, and noisier than <see cref="Log"/>.</summary>
    Debug = 5,
}

/// <summary>
/// A server talking about <em>itself</em> rather than about a document — "I cannot find your toolchain",
/// "I am falling back to a degraded mode". Nothing else on the wire carries this, and until these were
/// handled HexIDE discarded them, so a misconfigured server looked identical to a broken IDE
/// (hexide-io/HexIDE#289).
/// </summary>
public record LogMessageParams(
    [property: JsonPropertyName("type")] LspMessageType Type,
    [property: JsonPropertyName("message")] string Message);

/// <summary>
/// The same, but the server is asking for the user's attention rather than the log's. Identical shape;
/// separate type because the two mean different things and a shared record would invite treating them
/// alike.
/// </summary>
public record ShowMessageParams(
    [property: JsonPropertyName("type")] LspMessageType Type,
    [property: JsonPropertyName("message")] string Message);

/// <param name="RootUri">
/// The single root, <b>deprecated by the protocol</b> in favour of <paramref name="WorkspaceFolders"/> and
/// still sent, because "deprecated" is not "ignored". Measured across the four servers this suite drives,
/// there are three different behaviours and no field is safe to omit:
/// <list type="bullet">
/// <item>texlab reads <c>workspaceFolders</c> ONLY, and has no root at all without it;</item>
/// <item>clangd never parses <c>workspaceFolders</c> — <c>rootUri</c> is its sole root channel;</item>
/// <item>rumdl prefers the folders and falls back to <c>rootUri</c>;</item>
/// <item>the reference JSON server reads neither, resolving relative paths against the document instead.</item>
/// </list>
/// So clangd is the reason this field stays rather than a compatibility shrug, and sending both is the only
/// payload none of them is harmed by.
/// </param>
/// <param name="WorkspaceFolders">
/// Every folder in the workspace, or <see langword="null"/> when there is no project open — one per loaded
/// project, so a <c>.vbg</c> group's members are each described rather than all being assumed to live
/// wherever the startup project does.
/// </param>
public record InitializeParams(
    [property: JsonPropertyName("processId")] int? ProcessId,
    [property: JsonPropertyName("rootUri")] string? RootUri,
    [property: JsonPropertyName("capabilities")] ClientCapabilities Capabilities,
    [property: JsonPropertyName("workspaceFolders")] WorkspaceFolder[]? WorkspaceFolders = null,
    // Appended, never inserted. This is a positional record with several construction sites and tests
    // asserting on it; an insert whose types line up compiles cleanly while landing a value in the wrong slot.
    [property: JsonPropertyName("trace")] string? Trace = null);

/// <summary>
/// How much a server should say about its own work: the three values the protocol defines, and nothing else.
/// </summary>
/// <remarks>
/// <b><c>compact</c> is not here on purpose.</b> It appears in one editor's client-side rendering enum and
/// is easy to mistake for a fourth level, but the specification's own model carries exactly these three, so
/// offering it would mean sending a value no server is obliged to understand.
///
/// <para>
/// Strings rather than an enum because that is what crosses the wire, and because the value arrives from a
/// hand-edited configuration file where anything at all might be written. <see cref="Normalise"/> is the
/// single place that decides what counts.
/// </para>
/// </remarks>
public static class LspTraceValue
{
    public const string Off = "off";
    public const string Messages = "messages";
    public const string Verbose = "verbose";

    /// <summary>The canonical spelling of <paramref name="value"/>, or null when it names no level.</summary>
    /// <remarks>
    /// Null rather than a fallback to <see cref="Off"/>, so a caller can tell "the user asked for nothing"
    /// from "the user asked for something that does not exist". Only the second is worth reporting.
    /// </remarks>
    public static string? Normalise(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Off => Off,
        Messages => Messages,
        Verbose => Verbose,
        _ => null,
    };
}

/// <summary>Changes the trace level on a connection that is already up.</summary>
/// <remarks>
/// A notification, so there is no reply and no error. Nothing in the protocol reports whether a server
/// honours it, and measurement says most do not: five servers asked for verbose tracing, four then said
/// nothing at all. So this is only ever "asked", never "accepted".
/// </remarks>
public record SetTraceParams(
    [property: JsonPropertyName("value")] string Value);

/// <summary>
/// A server's own account of what it is doing, as opposed to what it is saying about a document.
/// </summary>
/// <remarks>
/// The distinction is the point. A wire capture shows what was said; this shows why. A server is the only
/// thing that knows it fell back to a slower parse or abandoned one on a deadline, and that is the answer
/// to "why did this take two seconds" that no amount of watching the wire can reconstruct.
///
/// <para>
/// <c>verbose</c> carries the detail and is sent only when the level is verbose, which is the whole of the
/// difference between the two non-off levels.
/// </para>
/// </remarks>
public record LogTraceParams(
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("verbose")] string? Verbose = null);

public record ClientCapabilities(
    [property: JsonPropertyName("textDocument")] TextDocumentClientCapabilities? TextDocument = null,
    [property: JsonPropertyName("workspace")] WorkspaceClientCapabilities? Workspace = null);

public record TextDocumentClientCapabilities(
    [property: JsonPropertyName("publishDiagnostics")] PublishDiagnosticsClientCapabilities? PublishDiagnostics = null,
    [property: JsonPropertyName("hover")] HoverClientCapabilities? Hover = null,
    [property: JsonPropertyName("synchronization")] TextDocumentSyncClientCapabilities? Synchronization = null,
    [property: JsonPropertyName("codeLens")] CodeLensClientCapabilities? CodeLens = null);

/// <summary>
/// What the client can do at workspace scope.
///
/// <para>
/// Its own node because <c>executeCommand</c> is not a document capability, and the protocol puts it here
/// for the reason that matters to us: a command is invoked against the <em>workspace</em>, not against a
/// file. That distinction survives into how we route one — see <c>LspClientRegistry</c>.
/// </para>
/// </summary>
public record WorkspaceClientCapabilities(
    [property: JsonPropertyName("executeCommand")] ExecuteCommandClientCapabilities? ExecuteCommand = null,
    /// <summary>
    /// That this client sends <c>workspaceFolders</c> in <c>initialize</c> and understands the concept.
    /// </summary>
    /// <remarks>
    /// Declared as well as sent, and both halves are needed. A server that composes its behaviour from what
    /// the client claimed will ignore the folders it was handed if nothing declared support for them — the
    /// same trap as <c>save</c> and <c>codeLens</c> above, where declaring and using are two halves of one
    /// negotiation and shipping either alone produces silence rather than an error.
    /// </remarks>
    [property: JsonPropertyName("workspaceFolders")] bool? WorkspaceFolders = null);

/// <summary>One folder of a multi-root workspace, as the protocol spells it.</summary>
public record WorkspaceFolder(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("name")] string Name);

/// <summary>
/// Declares that the client will ask for code lenses.
/// </summary>
/// <remarks>
/// <b>Deliberately empty, and <c>dynamicRegistration</c> is deliberately absent rather than <c>false</c>.</b>
/// `DynamicRegistrationProbe` asserts that the string never appears anywhere in what we send, on the
/// reasoning that a conformant server may only register dynamically for something the client asked for — so
/// asking for nothing is what obliges every server to declare its capabilities statically at initialize.
/// Writing an explicit <c>false</c> means the same thing to a correct reader and weakens a rule that is
/// currently absolute, which is worth more than the byte it saves.
/// </remarks>
public record CodeLensClientCapabilities();

/// <summary>Declares that the client will send <c>workspace/executeCommand</c>. Empty for the reason in
/// <see cref="CodeLensClientCapabilities"/>.</summary>
public record ExecuteCommandClientCapabilities();

/// <summary>
/// What the client can do about document synchronization.
///
/// <para>
/// <b>Declaring this is half of a negotiation, and the halves must ship together.</b> A client that gates
/// on the server's <c>save</c> without first claiming <c>didSave</c> creates a state in which both parties
/// are correct and nothing happens: the server withholds the capability because the client never asked for
/// it, and the client declines to send because the server did not offer. Our own server hides that — it
/// answers the same to everyone regardless of what the client claims — so the deadlock would appear only
/// against a server we did not write, which is exactly where it is hardest to diagnose.
/// </para>
///
/// <para>
/// <c>willSave</c> and <c>willSaveWaitUntil</c> are deliberately absent. Claiming a capability nothing
/// implements is the same defect as failing to claim one, pointed the other way.
/// </para>
/// </summary>
public record TextDocumentSyncClientCapabilities(
    [property: JsonPropertyName("didSave")] bool DidSave = false);

public record PublishDiagnosticsClientCapabilities(
    [property: JsonPropertyName("relatedInformation")] bool RelatedInformation = false);

public record InitializeResult(
    [property: JsonPropertyName("capabilities")] ServerCapabilities Capabilities);

/// <summary>
/// Whether a server wants to be told about saves, and whether it wants the text.
///
/// <para>
/// Three states rather than two booleans because they are not independent: there is no such thing as
/// wanting the text but not the notification, and a pair of flags invites a caller to consult one.
/// </para>
/// </summary>
public enum SaveNotification
{
    /// <summary>The server did not ask. Send nothing.</summary>
    None,

    /// <summary>Tell it a save happened; it will read the file itself if it wants the content.</summary>
    WithoutText,

    /// <summary>Tell it, and include what was saved.</summary>
    WithText,
}

/// <summary>
/// What a language server says it can do.
///
/// <para>
/// <b>Every field is a <c>JsonElement?</c>, and that is not laziness.</b> The protocol defines most of these
/// as <c>boolean | XxxOptions</c> — a server may answer <c>true</c> or an options object, and both are
/// correct. Modelling one as <c>bool?</c> accepts only half the contract, and the half it rejects does not
/// degrade gracefully: deserialization throws, the handshake is abandoned, and <em>every</em> language
/// feature including diagnostics goes dark with no error a user can see (#238).
/// </para>
///
/// <para>
/// The cost of being permissive here is one helper call at each use site. The cost of being precise is that
/// a conformant backend can silently disable all language intelligence by answering in its other legal
/// shape — which is the opposite of what a replaceable-backend seam is for.
/// </para>
/// </summary>
public record ServerCapabilities(
    [property: JsonPropertyName("textDocumentSync")]            System.Text.Json.JsonElement? TextDocumentSync = null,
    [property: JsonPropertyName("hoverProvider")]               System.Text.Json.JsonElement? HoverProvider = null,
    [property: JsonPropertyName("documentSymbolProvider")]      System.Text.Json.JsonElement? DocumentSymbolProvider = null,
    [property: JsonPropertyName("foldingRangeProvider")]        System.Text.Json.JsonElement? FoldingRangeProvider = null,
    [property: JsonPropertyName("completionProvider")]          System.Text.Json.JsonElement? CompletionProvider = null,
    [property: JsonPropertyName("signatureHelpProvider")]       System.Text.Json.JsonElement? SignatureHelpProvider = null,
    [property: JsonPropertyName("definitionProvider")]          System.Text.Json.JsonElement? DefinitionProvider = null,
    [property: JsonPropertyName("documentHighlightProvider")]   System.Text.Json.JsonElement? DocumentHighlightProvider = null,
    [property: JsonPropertyName("renameProvider")]              System.Text.Json.JsonElement? RenameProvider = null,
    [property: JsonPropertyName("documentFormattingProvider")]  System.Text.Json.JsonElement? DocumentFormattingProvider = null,
    [property: JsonPropertyName("codeLensProvider")]            System.Text.Json.JsonElement? CodeLensProvider = null,
    [property: JsonPropertyName("executeCommandProvider")]      System.Text.Json.JsonElement? ExecuteCommandProvider = null,
    [property: JsonPropertyName("experimental")]                System.Text.Json.JsonElement? Experimental = null)
{
    /// <summary>
    /// True when a capability is present and switched on, in either shape the protocol permits.
    ///
    /// <para>
    /// An options object counts as enabled: a server that returns options is describing <em>how</em> it
    /// supports the feature, which necessarily means it does. Only an absent field or an explicit
    /// <c>false</c> means unsupported.
    /// </para>
    /// </summary>
    public static bool IsEnabled(System.Text.Json.JsonElement? capability) => capability?.ValueKind
        is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.Object;

    /// <summary>
    /// True when the server accepts document open/change notifications. <c>textDocumentSync</c> is the one
    /// field whose two shapes mean different things rather than the same thing said twice — a bare number
    /// is the sync kind, an object carries it under <c>change</c> — and kind 0 means "send me nothing".
    /// </summary>
    public bool AcceptsDocumentSync() => ChangeKindIsNotNone(TextDocumentSync);

    // ── Reading a raw capabilities object ───────────────────────────────────────────────────────
    // The client keeps what the server sent verbatim rather than a typed view, so these work on that.
    // They live here, beside the record, because the two must not drift: a capability read one way for
    // display and another way for gating is how a feature ends up shown as available and refused.

    /// <summary>
    /// True when the server advertised support for a named capability, in either legal shape.
    /// </summary>
    public static bool Supports(System.Text.Json.JsonElement? capabilities, string capabilityName) =>
        capabilities is { } caps
        && caps.ValueKind == System.Text.Json.JsonValueKind.Object
        && caps.TryGetProperty(capabilityName, out var value)
        && IsEnabled(value);

    /// <summary>
    /// True when the server named this command in its <c>executeCommandProvider.commands</c>.
    /// </summary>
    /// <remarks>
    /// <b>This is the routing key for <c>workspace/executeCommand</c>, and it is why that request cannot go
    /// through the document-scoped helpers.</b> Every other request this client sends is about a file, so
    /// "which server" is answered by "which one claims this file". A command is about the workspace, and the
    /// only thing that says which server owns it is the list the server itself published. Asking any other
    /// server would run someone else's command, or nobody's.
    ///
    /// <para>
    /// A server that advertises <c>executeCommandProvider</c> with no <c>commands</c> array declares no
    /// commands, and so owns none. That is a real shape — <c>ExecuteCommandOptions.commands</c> is required
    /// by the specification, but a server that omits it should lose the request rather than receive every
    /// command by default.
    /// </para>
    /// </remarks>
    public static bool DeclaresCommand(System.Text.Json.JsonElement? capabilities, string command) =>
        capabilities is { } caps
        && caps.ValueKind == System.Text.Json.JsonValueKind.Object
        && caps.TryGetProperty("executeCommandProvider", out var provider)
        && provider.ValueKind == System.Text.Json.JsonValueKind.Object
        && provider.TryGetProperty("commands", out var commands)
        && commands.ValueKind == System.Text.Json.JsonValueKind.Array
        && commands.EnumerateArray().Any(c =>
            c.ValueKind == System.Text.Json.JsonValueKind.String
            && string.Equals(c.GetString(), command, StringComparison.Ordinal));

    /// <summary>
    /// True when the server asks to be called back for <c>codeLens/resolve</c>.
    /// </summary>
    /// <remarks>
    /// Read from <c>codeLensProvider.resolveProvider</c>. A bare <c>true</c> for <c>codeLensProvider</c> is
    /// legal and means lenses arrive complete, so the absence of this is not a defect — it is the server
    /// saying there is nothing to resolve.
    /// </remarks>
    public static bool ResolvesCodeLenses(System.Text.Json.JsonElement? capabilities) =>
        capabilities is { } caps
        && caps.ValueKind == System.Text.Json.JsonValueKind.Object
        && caps.TryGetProperty("codeLensProvider", out var provider)
        && provider.ValueKind == System.Text.Json.JsonValueKind.Object
        && provider.TryGetProperty("resolveProvider", out var resolve)
        && resolve.ValueKind == System.Text.Json.JsonValueKind.True;

    /// <summary>
    /// True for a capability under <c>experimental</c>, which is where the protocol says to put a method
    /// it does not define — and therefore the only thing a client can gate a custom method on.
    /// </summary>
    public static bool SupportsExperimental(System.Text.Json.JsonElement? capabilities, string name) =>
        capabilities is { } caps
        && caps.ValueKind == System.Text.Json.JsonValueKind.Object
        && caps.TryGetProperty("experimental", out var experimental)
        && experimental.ValueKind == System.Text.Json.JsonValueKind.Object
        && experimental.TryGetProperty(name, out var value)
        && IsEnabled(value);

    /// <summary>True when the server wants <c>didOpen</c> / <c>didClose</c>.</summary>
    public static bool AcceptsOpenClose(System.Text.Json.JsonElement? capabilities) =>
        ReadSync(capabilities) is { } sync
        && (sync.ValueKind == System.Text.Json.JsonValueKind.Number   // a bare kind implies open/close
            || !sync.TryGetProperty("openClose", out var openClose)   // absent defaults to supported
            || openClose.ValueKind != System.Text.Json.JsonValueKind.False);

    /// <summary>
    /// Whether the server wants <c>didSave</c>, and whether it wants the text with it.
    ///
    /// <para>
    /// One reader with three answers rather than two predicates a caller can check one of. The same
    /// argument as the note above: a capability read one way in one place and another way in another is
    /// how a feature ends up half-applied, and here the halves are "send it" and "send it with the text",
    /// which are not independent.
    /// </para>
    ///
    /// <para>
    /// <b>A bare non-zero number counts as asking</b>, which is a deliberate divergence from a strict
    /// reading: literally, a number carries no options object, so no <c>save</c>, so nothing should be
    /// sent. The reference implementation resolves a non-zero kind to
    /// <c>{openClose, change, save: {includeText: false}}</c>, and that is what server authors test
    /// against — so the strict reading leaves a server silent for a reason its author cannot see.
    /// <see cref="AcceptsOpenClose"/> immediately above already takes the same ecosystem reading of the
    /// same number form; taking the strict one here and the loose one there is the combination with no
    /// defence.
    /// </para>
    ///
    /// <para>
    /// <b>The object form is default-DENY, which is the opposite polarity to both its neighbours</b> — an
    /// object with no <c>openClose</c> still gets opens, and one with no <c>change</c> still gets changes,
    /// but one with no <c>save</c> gets no saves. That is deliberate rather than an oversight: open and
    /// change describe a default the server may narrow, while <c>save</c> is an opt-in it has to state.
    /// Please do not harmonise the three.
    /// </para>
    /// </summary>
    public static SaveNotification ReadSave(System.Text.Json.JsonElement? capabilities)
    {
        if (ReadSync(capabilities) is not { } sync) return SaveNotification.None;

        switch (sync.ValueKind)
        {
            // Kind 0 is "send me nothing", and a save is something.
            case System.Text.Json.JsonValueKind.Number:
                return sync.TryGetInt32(out var kind) && kind != 0
                    ? SaveNotification.WithoutText
                    : SaveNotification.None;

            case System.Text.Json.JsonValueKind.Object:
                if (!sync.TryGetProperty("save", out var save)) return SaveNotification.None;
                return save.ValueKind switch
                {
                    System.Text.Json.JsonValueKind.True => SaveNotification.WithoutText,
                    // An options object means yes, and then says how — the same rule as IsEnabled, except
                    // that here "how" is a question we actually have to answer.
                    System.Text.Json.JsonValueKind.Object =>
                        save.TryGetProperty("includeText", out var includeText)
                        && includeText.ValueKind == System.Text.Json.JsonValueKind.True
                            ? SaveNotification.WithText
                            : SaveNotification.WithoutText,
                    _ => SaveNotification.None,
                };

            default:
                return SaveNotification.None;
        }
    }

    /// <summary>True when the server wants <c>didChange</c>. Sync kind 0 means "send me nothing".</summary>
    public static bool AcceptsChanges(System.Text.Json.JsonElement? capabilities) =>
        ChangeKindIsNotNone(ReadSync(capabilities));

    private static System.Text.Json.JsonElement? ReadSync(System.Text.Json.JsonElement? capabilities) =>
        capabilities is { } caps
        && caps.ValueKind == System.Text.Json.JsonValueKind.Object
        && caps.TryGetProperty("textDocumentSync", out var sync)
            ? sync
            : null;

    // textDocumentSync is the one capability whose two shapes say DIFFERENT things rather than the same
    // thing twice — a bare number is the sync kind, an object carries it under `change` — so it cannot go
    // through IsEnabled, where an object always means yes.
    private static bool ChangeKindIsNotNone(System.Text.Json.JsonElement? sync)
    {
        if (sync is not { } value) return false;
        return value.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number => value.TryGetInt32(out var kind) && kind != 0,
            System.Text.Json.JsonValueKind.Object =>
                !value.TryGetProperty("change", out var change)
                || !change.TryGetInt32(out var objectKind)
                || objectKind != 0,
            _ => false,
        };
    }
}

/// <summary>Empty params object for notifications that take no arguments (e.g. "initialized").</summary>
public record EmptyParams
{
    public static readonly EmptyParams Instance = new();
}

public record TextDocumentPositionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);

public record CompletionParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")] Position Position);

public enum CompletionItemKind
{
    Text        = 1,
    Function    = 3,
    Variable    = 6,
    Property    = 10,
    Keyword     = 14,
    Constant    = 21,
}

public record CompletionItem(
    [property: JsonPropertyName("label")]      string Label,
    [property: JsonPropertyName("kind")]       CompletionItemKind Kind,
    [property: JsonPropertyName("detail")]     string? Detail = null,
    [property: JsonPropertyName("insertText")] string? InsertText = null);

public record CompletionList(
    [property: JsonPropertyName("isIncomplete")] bool IsIncomplete,
    [property: JsonPropertyName("items")]        CompletionItem[] Items);

public record DocumentSymbolParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

/// <summary>
/// What a symbol is, as the protocol numbers it.
/// </summary>
/// <remarks>
/// <b>All twenty-six, not the five VB6 happens to produce.</b> This is deserialized from whatever a server
/// sends, and a value with no member is not a compile error — it lands in the enum as an undefined number
/// and every <c>switch</c> falls to its default arm. So a partial enum does not fail loudly on an
/// unexpected kind, it silently relabels it: a <c>Module</c> arriving as <c>2</c> was rendered as a method.
/// The names are the protocol's, including the ones no VB6 server will ever send.
/// </remarks>
public enum SymbolKind
{
    File = 1,
    Module = 2,
    Namespace = 3,
    Package = 4,
    Class = 5,
    Method = 6,
    Property = 7,
    Field = 8,
    Constructor = 9,
    Enum = 10,
    Interface = 11,
    Function = 12,
    Variable = 13,
    Constant = 14,
    String = 15,
    Number = 16,
    Boolean = 17,
    Array = 18,
    Object = 19,
    Key = 20,
    Null = 21,
    EnumMember = 22,
    Struct = 23,
    Event = 24,
    Operator = 25,
    TypeParameter = 26,
}

/// <summary>
/// One symbol in a document's structure, with the symbols nested inside it.
/// </summary>
/// <remarks>
/// <b><c>Children</c> is the half of this the protocol is actually built around.</b>
/// <c>textDocument/documentSymbol</c> answers with a <em>tree</em> — a class holding its methods, a module
/// holding its procedures — and a client modelling only the top level receives that tree and keeps its
/// root. Against a server that reports one module symbol containing every procedure, dropping children is
/// the difference between a full outline and a list of one.
///
/// <para>
/// <b><c>Range</c> and <c>SelectionRange</c> are non-nullable, and that is a guarantee the reader makes
/// rather than one the wire gives.</b> The protocol's other legal answer to this request,
/// <c>SymbolInformation[]</c>, carries a <c>location</c> and no ranges at all — so a client that
/// deserializes straight into this shape gets nulls in fields nothing checks.
/// <c>VBLspClient.RequestDocumentSymbolsAsync</c> normalises both answers before anything sees them, which
/// is what lets every consumer here treat a symbol as a plain value.
/// </para>
/// </remarks>
public record DocumentSymbol(
    [property: JsonPropertyName("name")]           string Name,
    [property: JsonPropertyName("kind")]           SymbolKind Kind,
    [property: JsonPropertyName("range")]          Range Range,
    [property: JsonPropertyName("selectionRange")] Range SelectionRange,
    [property: JsonPropertyName("detail")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Detail = null,
    [property: JsonPropertyName("children")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DocumentSymbol[]? Children = null)
{
    /// <summary>
    /// This symbol and every symbol beneath it, depth first.
    /// </summary>
    /// <remarks>
    /// Offered rather than flattening at the seam, because the two consumers here want a flat list and a
    /// future outline view wants the tree — and only one of those can be reconstructed from the other.
    ///
    /// <para>
    /// The depth cap guards the walk, not the wire. Measured: a reply is bounded first by
    /// <c>System.Text.Json</c>'s own <c>MaxDepth</c> of 64 while it is parsed, and a symbol level costs two
    /// JSON levels, so nothing deeper than ~31 symbols ever reaches this. What the cap is actually for is
    /// a tree built in memory — by a test, or by a future caller — where an unbounded walk is a stack
    /// overflow rather than an exception: unrecoverable, and attributable to nothing.
    /// </para>
    /// </remarks>
    public IEnumerable<DocumentSymbol> Flatten(int maxDepth = 64)
    {
        yield return this;
        if (maxDepth <= 0 || Children is null) yield break;
        foreach (var child in Children)
            foreach (var descendant in child.Flatten(maxDepth - 1))
                yield return descendant;
    }
}

/// <summary>
/// The protocol's <em>other</em> answer to <c>textDocument/documentSymbol</c> — a flat list, each entry
/// carrying a <see cref="Location"/> instead of ranges.
/// </summary>
/// <remarks>
/// Modelled so it can be recognised and converted, never handed to a consumer. The two shapes are
/// distinguishable only by inspecting an element: a <c>SymbolInformation</c> has <c>location</c>, a
/// <c>DocumentSymbol</c> has <c>range</c>. Servers pick freely between them, and the reference
/// implementation's own servers do not agree with each other.
/// </remarks>
public record SymbolInformation(
    [property: JsonPropertyName("name")]          string Name,
    [property: JsonPropertyName("kind")]          SymbolKind Kind,
    [property: JsonPropertyName("location")]      Location Location,
    [property: JsonPropertyName("containerName")] string? ContainerName = null);

/// <summary>A workspace-wide symbol search. The query is matched by the server, not by us.</summary>
/// <remarks>
/// An empty query is legal and means "everything the server knows"; servers differ on whether they answer
/// it at all, and one that does may answer with a great deal. Whether to send one is the caller's call.
/// </remarks>
public record WorkspaceSymbolParams(
    [property: JsonPropertyName("query")] string Query);

public record HoverClientCapabilities(
    [property: JsonPropertyName("contentFormat")] string[]? ContentFormat = null);

public record MarkupContent(
    [property: JsonPropertyName("kind")]  string Kind,
    [property: JsonPropertyName("value")] string Value);

/// <summary>Hover response — null when there is nothing to show.</summary>
public record HoverResult(
    [property: JsonPropertyName("contents")] MarkupContent Contents,
    [property: JsonPropertyName("range")]    Range? Range = null);

/// <summary>A location inside a resource, such as a line inside a text file.</summary>
public record Location(
    [property: JsonPropertyName("uri")]   string Uri,
    [property: JsonPropertyName("range")] Range Range);

/// <summary>A document highlight marks a range in the document for the symbol at the given position.</summary>
public record DocumentHighlight(
    [property: JsonPropertyName("range")] Range Range,
    [property: JsonPropertyName("kind")]  int? Kind = null);

/// <summary>Rename request parameters.</summary>
public record RenameParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")]     Position Position,
    [property: JsonPropertyName("newName")]       string NewName);

/// <summary>A text edit applicable to a document.</summary>
public record TextEdit(
    [property: JsonPropertyName("range")]   Range Range,
    [property: JsonPropertyName("newText")] string NewText);

/// <summary>
/// A workspace edit represents changes to many resources managed in the workspace.
/// The <c>Changes</c> dictionary maps document URIs to arrays of TextEdits.
/// </summary>
public record WorkspaceEdit(
    [property: JsonPropertyName("changes")] Dictionary<string, TextEdit[]>? Changes = null);

/// <summary>Formatting options sent by the client.</summary>
public record FormattingOptions(
    [property: JsonPropertyName("tabSize")]                int TabSize,
    [property: JsonPropertyName("insertSpaces")]           bool InsertSpaces,
    [property: JsonPropertyName("trimTrailingWhitespace")] bool? TrimTrailingWhitespace = null);

/// <summary>Document formatting request parameters.</summary>
public record DocumentFormattingParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("options")]      FormattingOptions Options);

public record FoldingRangeParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

public record FoldingRange(
    [property: JsonPropertyName("startLine")]      int StartLine,
    [property: JsonPropertyName("endLine")]        int EndLine,
    [property: JsonPropertyName("startCharacter")] int? StartCharacter = null,
    [property: JsonPropertyName("endCharacter")]   int? EndCharacter = null,
    [property: JsonPropertyName("kind")]           string? Kind = null);

public record CodeLensParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument);

/// <summary>
/// A command the server offers against a range of a document — the protocol's affordance for "Run test"
/// above a procedure, and the reason this pair was implemented together.
/// </summary>
/// <remarks>
/// <b><c>Command</c> is optional, and that is the whole of <c>codeLens/resolve</c>.</b> A server may return
/// the ranges cheaply and compute each command only when asked, so a lens with no command is not a broken
/// lens — it is an unresolved one, and clicking it would do nothing until it is resolved. <c>Data</c> is
/// the server's own opaque handle for doing that, and it must be handed back untouched.
/// </remarks>
/// <remarks>
/// <b>The two optional members are omitted when absent, not written as <c>null</c>.</b> This record goes
/// <em>out</em> as well as in: <c>codeLens/resolve</c> hands the server back its own lens, so writing
/// <c>"data": null</c> where the server sent no <c>data</c> would return it an object it did not give us.
/// <c>data</c> is explicitly the server's private handle, and the one field it is entitled to expect
/// verbatim.
/// </remarks>
public record CodeLens(
    [property: JsonPropertyName("range")]   Range Range,
    [property: JsonPropertyName("command")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Command? Command = null,
    [property: JsonPropertyName("data")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] System.Text.Json.JsonElement? Data = null);

/// <summary>
/// A command the client can invoke, as named by the server.
/// </summary>
/// <remarks>
/// <c>Title</c> is what a human reads; <c>CommandName</c> is what goes on the wire. They are unrelated
/// strings and the protocol says nothing about either, so a client that displays the identifier or sends
/// the label is wrong in a way no server can correct.
/// </remarks>
public record Command(
    [property: JsonPropertyName("title")]     string Title,
    [property: JsonPropertyName("command")]   string CommandName,
    [property: JsonPropertyName("arguments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] System.Text.Json.JsonElement[]? Arguments = null);

/// <remarks>
/// <b><c>arguments</c> is omitted when there are none, never sent as <c>null</c>.</b> The protocol marks it
/// optional, so absent is unambiguously valid while <c>null</c> is only probably-tolerated — the same
/// distinction that made <c>"params": []</c> on <c>shutdown</c> a real defect against two of three foreign
/// servers (hexide-io/HexIDE#312). Where the two spellings differ only in how strict a reader is, send the
/// one nothing can object to.
/// </remarks>
public record ExecuteCommandParams(
    [property: JsonPropertyName("command")]   string Command,
    [property: JsonPropertyName("arguments")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] System.Text.Json.JsonElement[]? Arguments = null);

public record SignatureHelpParams(
    [property: JsonPropertyName("textDocument")] TextDocumentIdentifier TextDocument,
    [property: JsonPropertyName("position")]     Position Position);

public record ParameterInformation(
    [property: JsonPropertyName("label")]         string Label,
    [property: JsonPropertyName("documentation")] string? Documentation = null);

public record SignatureInformation(
    [property: JsonPropertyName("label")]         string Label,
    [property: JsonPropertyName("documentation")] string? Documentation = null,
    [property: JsonPropertyName("parameters")]    ParameterInformation[]? Parameters = null);

public record SignatureHelp(
    [property: JsonPropertyName("signatures")]       SignatureInformation[] Signatures,
    [property: JsonPropertyName("activeSignature")]  int? ActiveSignature = null,
    [property: JsonPropertyName("activeParameter")]  int? ActiveParameter = null);

/// <summary>One VBA built-in function entry returned by vb/builtinSymbols.</summary>
public record VbaBuiltinSymbol(
    [property: JsonPropertyName("name")]          string Name,
    [property: JsonPropertyName("signature")]     string Signature,
    [property: JsonPropertyName("documentation")] string? Documentation = null);
