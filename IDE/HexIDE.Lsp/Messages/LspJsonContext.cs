using System.Text.Json.Serialization;
using HexIDE.Lsp.Messages;

namespace HexIDE.Lsp;

[JsonSerializable(typeof(InitializeParams))]
[JsonSerializable(typeof(InitializeResult))]
[JsonSerializable(typeof(ServerCapabilities))]
[JsonSerializable(typeof(System.Text.Json.JsonElement))]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(LogMessageParams))]
[JsonSerializable(typeof(ShowMessageParams))]
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
[JsonSerializable(typeof(CodeLensParams))]
[JsonSerializable(typeof(CodeLens))]
[JsonSerializable(typeof(CodeLens[]))]
[JsonSerializable(typeof(Command))]
[JsonSerializable(typeof(ExecuteCommandParams))]
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
[JsonSerializable(typeof(WorkspaceSymbolParams))]
[JsonSerializable(typeof(SymbolInformation[]))]
[JsonSerializable(typeof(SetTraceParams))]
[JsonSerializable(typeof(LogTraceParams))]
// NOT an LSP type, and load-bearing for exactly that reason. StreamJsonRpc deserializes the `data` member
// of any JSON-RPC ERROR response into this, using the formatter's options — which are these. Unregistered,
// the generated resolver returns null, the error reply cannot be read, and the failure is not scoped to
// that one request: the whole connection dies with a ParseError and reconnects.
//
// So a server answering -32601 to one unsupported method took the entire language client down with it,
// rather than that request returning nothing. Found by pointing the client at a server that throws
// (`RequestFailureLoggingTests`), which is a thing no server we had ever driven happened to do.
[JsonSerializable(typeof(StreamJsonRpc.Protocol.CommonErrorData))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class LspJsonContext : JsonSerializerContext { }
