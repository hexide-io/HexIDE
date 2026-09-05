using System.Text.Json.Serialization;
using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(DidOpenTextDocumentParams))]
[JsonSerializable(typeof(DidChangeTextDocumentParams))]
[JsonSerializable(typeof(DidCloseTextDocumentParams))]
// Load-bearing, and not only under AOT: VBLspClient builds its serializer options FROM this context,
// whose generated resolver ends in `return null` with nothing chained behind it. An unregistered type
// therefore throws under plain JIT too — into a debug-level catch, which turns the omission into a
// server that connects, initializes and then answers nothing. That is the exact symptom #267 exists to
// fix, so leaving this line out would reproduce the bug while appearing to fix it.
[JsonSerializable(typeof(DidSaveTextDocumentParams))]
[JsonSerializable(typeof(PublishDiagnosticsParams))]
[JsonSerializable(typeof(Diagnostic[]))]
[JsonSerializable(typeof(TextDocumentPositionParams))]
[JsonSerializable(typeof(HoverResult))]
[JsonSerializable(typeof(DocumentSymbolParams))]
[JsonSerializable(typeof(DocumentSymbol[]))]
[JsonSerializable(typeof(FoldingRangeParams))]
[JsonSerializable(typeof(FoldingRange[]))]
[JsonSerializable(typeof(CompletionParams))]
[JsonSerializable(typeof(CompletionList))]
[JsonSerializable(typeof(CompletionItem[]))]
[JsonSerializable(typeof(SignatureHelpParams))]
[JsonSerializable(typeof(SignatureHelp))]
[JsonSerializable(typeof(SignatureInformation[]))]
[JsonSerializable(typeof(ParameterInformation[]))]
[JsonSerializable(typeof(Location[]))]
[JsonSerializable(typeof(DocumentHighlight[]))]
[JsonSerializable(typeof(RenameParams))]
[JsonSerializable(typeof(WorkspaceEdit))]
[JsonSerializable(typeof(TextEdit[]))]
[JsonSerializable(typeof(Dictionary<string, TextEdit[]>))]
[JsonSerializable(typeof(DocumentFormattingParams))]
[JsonSerializable(typeof(FormattingOptions))]
[JsonSerializable(typeof(VbaBuiltinSymbol[]))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class LspJsonContext : JsonSerializerContext { }
