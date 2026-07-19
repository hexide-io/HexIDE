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

// textDocumentSync can be an int OR an object depending on the server — use JsonElement to accept either.
public record ServerCapabilities(
    [property: JsonPropertyName("textDocumentSync")]       System.Text.Json.JsonElement? TextDocumentSync = null,
    [property: JsonPropertyName("hoverProvider")]          bool? HoverProvider = null,
    [property: JsonPropertyName("documentSymbolProvider")] bool? DocumentSymbolProvider = null,
    [property: JsonPropertyName("foldingRangeProvider")]   bool? FoldingRangeProvider = null,
    [property: JsonPropertyName("completionProvider")]     System.Text.Json.JsonElement? CompletionProvider = null,
    [property: JsonPropertyName("signatureHelpProvider")] System.Text.Json.JsonElement? SignatureHelpProvider = null);

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
