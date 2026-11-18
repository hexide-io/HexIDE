using System.Text.Json.Serialization;

namespace HexIDE.Lsp.Messages;

public record Position(
    [property: JsonPropertyName("line")] int Line,
    [property: JsonPropertyName("character")] int Character);

public record Range(
    [property: JsonPropertyName("start")] Position Start,
    [property: JsonPropertyName("end")] Position End);

/// <summary>A location inside a resource, such as a line inside a text file.</summary>
public record Location(
    [property: JsonPropertyName("uri")]   string Uri,
    [property: JsonPropertyName("range")] Range Range);

/// <summary>A text edit applicable to a document.</summary>
public record TextEdit(
    [property: JsonPropertyName("range")]   Range Range,
    [property: JsonPropertyName("newText")] string NewText);

/// <summary>
/// A workspace edit represents changes to many resources managed in the workspace.
/// The <c>Changes</c> dictionary maps document URIs to arrays of TextEdits.
/// </summary>
public record WorkspaceEdit(
    [property: JsonPropertyName("changes")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Dictionary<string, TextEdit[]>? Changes = null,
    [property: JsonPropertyName("documentChanges")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] TextDocumentEdit[]? DocumentChanges = null);

/// <summary>A text document edit, or an unsupported resource operation identified by Kind.</summary>
public record TextDocumentEdit(
    [property: JsonPropertyName("textDocument")] OptionalVersionedTextDocumentIdentifier? TextDocument = null,
    [property: JsonPropertyName("edits")] TextEdit[]? Edits = null,
    [property: JsonPropertyName("kind")] string? Kind = null,
    [property: JsonPropertyName("uri")] string? Uri = null,
    [property: JsonPropertyName("oldUri")] string? OldUri = null);

public record OptionalVersionedTextDocumentIdentifier(
    [property: JsonPropertyName("uri")] string Uri,
    [property: JsonPropertyName("version")] int? Version);

public record ApplyWorkspaceEditResult(
    [property: JsonPropertyName("applied")] bool Applied,
    [property: JsonPropertyName("failureReason")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? FailureReason = null);
