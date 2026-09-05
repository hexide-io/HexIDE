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

public record InitializeParams(
    [property: JsonPropertyName("processId")] int? ProcessId,
    [property: JsonPropertyName("rootUri")] string? RootUri,
    [property: JsonPropertyName("capabilities")] ClientCapabilities Capabilities);

public record ClientCapabilities(
    [property: JsonPropertyName("textDocument")] TextDocumentClientCapabilities? TextDocument = null);

public record TextDocumentClientCapabilities(
    [property: JsonPropertyName("publishDiagnostics")] PublishDiagnosticsClientCapabilities? PublishDiagnostics = null,
    [property: JsonPropertyName("hover")] HoverClientCapabilities? Hover = null);

public record PublishDiagnosticsClientCapabilities(
    [property: JsonPropertyName("relatedInformation")] bool RelatedInformation = false);

public record InitializeResult(
    [property: JsonPropertyName("capabilities")] ServerCapabilities Capabilities);

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

public enum SymbolKind
{
    Method   = 6,
    Property = 7,
    Enum     = 10,
    Function = 12,
    Struct   = 23,
}

public record DocumentSymbol(
    [property: JsonPropertyName("name")]           string Name,
    [property: JsonPropertyName("kind")]           SymbolKind Kind,
    [property: JsonPropertyName("range")]          Range Range,
    [property: JsonPropertyName("selectionRange")] Range SelectionRange);

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
