// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Serilog;

namespace HexIDE.VbLspServer;

/// <summary>
/// Hand-rolled LSP stdio server.
/// Reads Content-Length-framed JSON-RPC messages from stdin, dispatches them,
/// and writes responses/notifications to stdout.
/// </summary>
public sealed class LspServer
{
    private readonly Stream _input;
    private readonly Stream _output;
    private readonly object _writeLock = new();
    private bool _shutdownRequested;
    private readonly Dictionary<string, List<LspDiagnostic>>            _documentDiagnostics  = new();
    private readonly Dictionary<string, List<LspSymbol>>                _documentSymbols      = new();
    private readonly Dictionary<string, List<LspFoldingRange>>          _documentFoldings     = new();
    private readonly Dictionary<string, string>                          _documentSource       = new();
    private readonly Dictionary<string, IReadOnlyDictionary<string, string?>> _documentDeclaredTypes = new();

    public LspServer(Stream input, Stream output)
    {
        _input = input;
        _output = output;
    }

    public void Run()
    {
        while (true)
        {
            var message = ReadMessage();
            if (message is null) break;

            if (!message.TryGetPropertyValue("method", out var methodNode)) continue;
            var method = methodNode?.GetValue<string>() ?? string.Empty;

            message.TryGetPropertyValue("id", out var idNode);
            message.TryGetPropertyValue("params", out var paramsNode);

            switch (method)
            {
                case "initialize":
                    Log.Information("Client initialize request received");
                    SendResponse(idNode, BuildInitializeResult());
                    break;

                case "initialized":
                    Log.Information("Client initialized notification received");
                    break;

                case "textDocument/didOpen":
                    HandleDidOpen(paramsNode);
                    break;

                case "textDocument/didChange":
                    HandleDidChange(paramsNode);
                    break;

                case "textDocument/didClose":
                    HandleDidClose(paramsNode);
                    break;

                case "textDocument/hover":
                    HandleHover(idNode, paramsNode);
                    break;

                case "textDocument/documentSymbol":
                    HandleDocumentSymbol(idNode, paramsNode);
                    break;

                case "textDocument/foldingRange":
                    HandleFoldingRange(idNode, paramsNode);
                    break;

                case "textDocument/completion":
                    HandleCompletion(idNode, paramsNode);
                    break;

                case "textDocument/signatureHelp":
                    HandleSignatureHelp(idNode, paramsNode);
                    break;

                case "textDocument/definition":
                    HandleDefinition(idNode, paramsNode);
                    break;

                case "textDocument/documentHighlight":
                    HandleDocumentHighlight(idNode, paramsNode);
                    break;

                case "textDocument/rename":
                    HandleRename(idNode, paramsNode);
                    break;

                case "textDocument/formatting":
                    HandleFormatting(idNode, paramsNode);
                    break;

                case "vb/builtinSymbols":
                    HandleBuiltinSymbols(idNode);
                    break;

                case "shutdown":
                    Log.Information("Shutdown requested");
                    _shutdownRequested = true;
                    SendResponse(idNode, null);
                    break;

                case "exit":
                    Log.Information("Exit notification received (clean={CleanShutdown})", _shutdownRequested);
                    Environment.Exit(_shutdownRequested ? 0 : 1);
                    return;
            }
        }
    }

    // ── LSP message handlers ──────────────────────────────────────────────────

    private void HandleDidOpen(JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var text = paramsNode?["textDocument"]?["text"]?.GetValue<string>();
        if (uri is null || text is null) return;
        Log.Debug("Document opened: {Uri}", uri);
        PublishDiagnostics(uri, text);
    }

    private void HandleDidChange(JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var changes = paramsNode?["contentChanges"]?.AsArray();
        var text = changes?.LastOrDefault()?["text"]?.GetValue<string>();
        if (uri is null || text is null) return;
        PublishDiagnostics(uri, text);
    }

    private void HandleDidClose(JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri is null) return;
        Log.Debug("Document closed: {Uri}", uri);
        _documentDiagnostics.Remove(uri);
        _documentSymbols.Remove(uri);
        _documentFoldings.Remove(uri);
        _documentSource.Remove(uri);
        _documentDeclaredTypes.Remove(uri);
        SendNotification("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = uri,
            ["diagnostics"] = new JsonArray()
        });
    }

    private void HandleHover(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri  = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var line = paramsNode?["position"]?["line"]?.GetValue<int>();
        var ch   = paramsNode?["position"]?["character"]?.GetValue<int>();

        if (uri is null || line is null || ch is null)
        {
            SendResponse(idNode, null);
            return;
        }

        _documentDiagnostics.TryGetValue(uri, out var diags);
        _documentDeclaredTypes.TryGetValue(uri, out var declaredTypes);
        _documentSource.TryGetValue(uri, out var source);

        var parts = new List<string>();

        // Type info: look up the identifier under the cursor in declared types
        if (source is not null && declaredTypes is not null)
        {
            var word = GetWordAtPosition(source, line.Value, ch.Value);
            if (word is not null && declaredTypes.TryGetValue(word, out var typeAnnotation))
            {
                parts.Add(typeAnnotation is not null
                    ? $"{word} As {typeAnnotation}"
                    : word);
            }
        }

        // Diagnostic messages at the hovered position
        if (diags is not null)
        {
            foreach (var d in diags)
            {
                if (ContainsPosition(d.Range, line.Value, ch.Value))
                    parts.Add(d.Message);
            }
        }

        if (parts.Count == 0)
        {
            SendResponse(idNode, null);
            return;
        }

        SendResponse(idNode, new JsonObject
        {
            ["contents"] = new JsonObject
            {
                ["kind"]  = "plaintext",
                ["value"] = string.Join("\n\n", parts)
            }
        });
    }

    private void HandleFoldingRange(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri is null || !_documentFoldings.TryGetValue(uri, out var foldings))
        {
            SendResponse(idNode, new JsonArray());
            return;
        }

        var array = new JsonArray();
        foreach (var f in foldings)
        {
            var obj = new JsonObject
            {
                ["startLine"] = f.StartLine,
                ["endLine"]   = f.EndLine,
            };
            if (f.Kind is not null)
                obj["kind"] = f.Kind;
            array.Add(obj);
        }

        SendResponse(idNode, array);
    }

    private void HandleDocumentSymbol(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri is null || !_documentSymbols.TryGetValue(uri, out var symbols))
        {
            SendResponse(idNode, new JsonArray());
            return;
        }

        var array = new JsonArray();
        foreach (var s in symbols)
        {
            array.Add(new JsonObject
            {
                ["name"] = s.Name,
                ["kind"] = (int)s.Kind,
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = s.Range.Start.Line, ["character"] = s.Range.Start.Character },
                    ["end"]   = new JsonObject { ["line"] = s.Range.End.Line,   ["character"] = s.Range.End.Character }
                },
                ["selectionRange"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = s.Range.Start.Line, ["character"] = s.Range.Start.Character },
                    ["end"]   = new JsonObject { ["line"] = s.Range.Start.Line, ["character"] = s.Range.Start.Character + s.Name.Length }
                }
            });
        }

        SendResponse(idNode, array);
    }

    private void HandleCompletion(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        if (uri is null || !_documentSource.TryGetValue(uri, out var source))
        {
            SendCompletionList(idNode, []);
            return;
        }

        var declaredNames = VbScopeAnalyzer.GetDeclaredNames(source);
        var items = new List<LspCompletionItem>(VbKeywords.All.Length + declaredNames.Count);

        items.AddRange(VbKeywords.All);
        foreach (var name in declaredNames)
        {
            items.Add(new LspCompletionItem(name, LspCompletionItemKind.Variable));
        }

        SendCompletionList(idNode, items);
    }

    private void SendCompletionList(JsonNode? idNode, List<LspCompletionItem> items)
    {
        var array = new JsonArray();
        foreach (var item in items)
        {
            var obj = new JsonObject
            {
                ["label"] = item.Label,
                ["kind"]  = (int)item.Kind
            };
            if (item.Detail is not null)
                obj["detail"] = item.Detail;
            if (item.InsertText is not null)
                obj["insertText"] = item.InsertText;
            array.Add(obj);
        }

        SendResponse(idNode, new JsonObject
        {
            ["isIncomplete"] = false,
            ["items"]        = array
        });
    }

    private void HandleSignatureHelp(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri  = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var line = paramsNode?["position"]?["line"]?.GetValue<int>();
        var ch   = paramsNode?["position"]?["character"]?.GetValue<int>();

        if (uri is null || line is null || ch is null ||
            !_documentSource.TryGetValue(uri, out var source))
        {
            SendResponse(idNode, null);
            return;
        }

        var (functionName, activeParameter) = FindCallContext(source, line.Value, ch.Value);
        if (functionName is null)
        {
            SendResponse(idNode, null);
            return;
        }

        var entries = VbSignatures.Get(functionName);
        if (entries.Length == 0)
        {
            SendResponse(idNode, null);
            return;
        }

        var sigArray = new JsonArray();
        foreach (var entry in entries)
        {
            var paramArray = new JsonArray();
            foreach (var param in entry.Parameters)
            {
                var pObj = new JsonObject { ["label"] = param };
                paramArray.Add(pObj);
            }
            var sigObj = new JsonObject { ["label"] = entry.Label, ["parameters"] = paramArray };
            if (entry.Documentation is not null)
                sigObj["documentation"] = entry.Documentation;
            sigArray.Add(sigObj);
        }

        SendResponse(idNode, new JsonObject
        {
            ["signatures"]      = sigArray,
            ["activeSignature"] = 0,
            ["activeParameter"] = activeParameter
        });
    }

    /// <summary>
    /// Scans backwards from (line, character) to find the nearest unclosed '(' and
    /// the identifier immediately preceding it.  Returns (functionName, activeParamIndex)
    /// or (null, 0) if no call context is found.
    /// </summary>
    private static (string? FunctionName, int ActiveParameter) FindCallContext(
        string source, int line, int character)
    {
        // Convert (line, character) to a flat offset.
        // Both \n and \r\n line endings are handled: \r is treated as invisible.
        int offset = 0;
        int currentLine = 0;
        int lineStart = 0;
        for (int i = 0; i < source.Length; i++)
        {
            char c = source[i];
            if (c == '\r') continue;   // skip CR; column counts are against \n-normalised view
            if (c == '\n')
            {
                if (currentLine == line && character >= (i - lineStart))
                {
                    // cursor is past end of this line — clamp to EOL
                    offset = i;
                    goto found;
                }
                currentLine++;
                lineStart = i + 1;
                continue;
            }
            if (currentLine == line && (i - lineStart) == character)
            {
                offset = i;
                goto found;
            }
        }
        // Cursor is at end of source or past last line
        offset = source.Length;
        found:

        int depth = 0;
        int commas = 0;
        for (int i = offset - 1; i >= 0; i--)
        {
            char c = source[i];
            if (c == '\r') continue;
            if (c == ')') { depth++; continue; }
            if (c == '(')
            {
                if (depth > 0) { depth--; continue; }
                // This is our opening paren — now find the identifier before it
                int nameEnd = i - 1;
                while (nameEnd >= 0 && (source[nameEnd] == ' ' || source[nameEnd] == '\r')) nameEnd--;
                if (nameEnd < 0) return (null, 0);
                if (!char.IsLetterOrDigit(source[nameEnd]) && source[nameEnd] != '_')
                    return (null, 0);
                int nameStart = nameEnd;
                while (nameStart > 0 && (char.IsLetterOrDigit(source[nameStart - 1]) || source[nameStart - 1] == '_'))
                    nameStart--;
                var name = source.Substring(nameStart, nameEnd - nameStart + 1);
                return (name, commas);
            }
            if (c == ',' && depth == 0) commas++;
            // Stop at statement boundaries — don't cross lines
            if (c == '\n' || c == ':') return (null, 0);
        }
        return (null, 0);
    }

    private void HandleDefinition(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri  = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var line = paramsNode?["position"]?["line"]?.GetValue<int>();
        var ch   = paramsNode?["position"]?["character"]?.GetValue<int>();

        if (uri is null || line is null || ch is null ||
            !_documentSource.TryGetValue(uri, out var source) ||
            !_documentSymbols.TryGetValue(uri, out var symbols))
        {
            SendResponse(idNode, null);
            return;
        }

        var word = GetWordAtPosition(source, line.Value, ch.Value);
        if (string.IsNullOrEmpty(word))
        {
            SendResponse(idNode, null);
            return;
        }

        LspSymbol? match = null;
        foreach (var s in symbols)
        {
            if (string.Equals(s.Name, word, StringComparison.OrdinalIgnoreCase))
            {
                match = s;
                break;
            }
        }

        if (match is null)
        {
            SendResponse(idNode, null);
            return;
        }

        SendResponse(idNode, new JsonArray
        {
            new JsonObject
            {
                ["uri"] = uri,
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = match.Range.Start.Line, ["character"] = match.Range.Start.Character },
                    ["end"]   = new JsonObject { ["line"] = match.Range.Start.Line, ["character"] = match.Range.Start.Character + match.Name.Length }
                }
            }
        });
    }

    private void HandleDocumentHighlight(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri  = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var line = paramsNode?["position"]?["line"]?.GetValue<int>();
        var ch   = paramsNode?["position"]?["character"]?.GetValue<int>();

        if (uri is null || line is null || ch is null ||
            !_documentSource.TryGetValue(uri, out var source))
        {
            SendResponse(idNode, null);
            return;
        }

        var word = GetWordAtPosition(source, line.Value, ch.Value);
        if (string.IsNullOrEmpty(word))
        {
            SendResponse(idNode, null);
            return;
        }

        var results = new JsonArray();
        FindAllOccurrences(source, word, results);
        SendResponse(idNode, results.Count > 0 ? results : null);
    }

    private void HandleRename(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri     = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();
        var line    = paramsNode?["position"]?["line"]?.GetValue<int>();
        var ch      = paramsNode?["position"]?["character"]?.GetValue<int>();
        var newName = paramsNode?["newName"]?.GetValue<string>();

        if (uri is null || line is null || ch is null || string.IsNullOrWhiteSpace(newName) ||
            !_documentSource.TryGetValue(uri, out var source))
        {
            SendResponse(idNode, null);
            return;
        }

        var word = GetWordAtPosition(source, line.Value, ch.Value);
        if (string.IsNullOrEmpty(word))
        {
            SendResponse(idNode, null);
            return;
        }

        // Don't allow renaming reserved keywords
        if (VbKeywords.ReservedWords.Contains(word))
        {
            SendResponse(idNode, null);
            return;
        }

        var ranges = new JsonArray();
        FindAllOccurrences(source, word, ranges);

        if (ranges.Count == 0)
        {
            SendResponse(idNode, null);
            return;
        }

        var edits = new JsonArray();
        foreach (var rangeNode in ranges)
        {
            edits.Add(new JsonObject
            {
                ["range"]   = rangeNode!["range"]!.DeepClone(),
                ["newText"] = newName
            });
        }

        var workspaceEdit = new JsonObject
        {
            ["changes"] = new JsonObject
            {
                [uri] = edits
            }
        };

        SendResponse(idNode, workspaceEdit);
    }

    private void HandleFormatting(JsonNode? idNode, JsonNode? paramsNode)
    {
        var uri = paramsNode?["textDocument"]?["uri"]?.GetValue<string>();

        if (uri is null || !_documentSource.TryGetValue(uri, out var source))
        {
            SendResponse(idNode, new JsonArray());
            return;
        }

        var formatted = VbFormatter.Format(source);
        if (formatted is null)
        {
            // No changes needed
            SendResponse(idNode, new JsonArray());
            return;
        }

        // Return a single whole-document TextEdit
        var lines = source.Split('\n');
        int lastLine = Math.Max(0, lines.Length - 1);
        int lastChar = lines[lastLine].TrimEnd('\r').Length;

        var edits = new JsonArray
        {
            new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = 0, ["character"] = 0 },
                    ["end"]   = new JsonObject { ["line"] = lastLine, ["character"] = lastChar }
                },
                ["newText"] = formatted
            }
        };

        SendResponse(idNode, edits);
    }

    private void HandleBuiltinSymbols(JsonNode? idNode)
    {
        var result = new JsonArray();
        foreach (var (name, entry) in VbSignatures.GetAll())
        {
            result.Add(new JsonObject
            {
                ["name"]          = name,
                ["signature"]     = entry.Label,
                ["documentation"] = entry.Documentation
            });
        }
        SendResponse(idNode, result);
    }

    private static void FindAllOccurrences(string source, string word, JsonArray results)
    {
        int lineNum = 0;
        int i = 0;
        while (i < source.Length)
        {
            // Find line start/end
            int lineStart = i;
            while (i < source.Length && source[i] != '\n') i++;
            int lineEnd = i;
            if (i < source.Length) i++; // skip \n

            // Search within this line (strip trailing \r)
            int end = lineEnd;
            if (end > lineStart && source[end - 1] == '\r') end--;

            int pos = lineStart;
            while (pos < end)
            {
                // Case-insensitive search for word
                int idx = source.IndexOf(word, pos, end - pos, StringComparison.OrdinalIgnoreCase);
                if (idx < 0) break;

                // Ensure it's a whole identifier (not part of a longer name)
                bool leftOk  = idx == lineStart        || !IsIdentifierChar(source[idx - 1]);
                bool rightOk = idx + word.Length >= end || !IsIdentifierChar(source[idx + word.Length]);

                if (leftOk && rightOk)
                {
                    int charOffset = idx - lineStart;
                    results.Add(new JsonObject
                    {
                        ["range"] = new JsonObject
                        {
                            ["start"] = new JsonObject { ["line"] = lineNum, ["character"] = charOffset },
                            ["end"]   = new JsonObject { ["line"] = lineNum, ["character"] = charOffset + word.Length }
                        }
                    });
                }
                pos = idx + word.Length;
            }
            lineNum++;
        }
    }

    private static string? GetWordAtPosition(string source, int line, int character)
    {
        // Convert LSP (line, character) to flat offset in source
        int currentLine = 0;
        int lineStart = 0;
        int lineEnd = source.Length;

        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == '\r') continue;
            if (source[i] == '\n')
            {
                if (currentLine == line)
                {
                    lineEnd = i;
                    break;
                }
                currentLine++;
                lineStart = i + 1;
            }
        }

        if (currentLine != line) return null;

        // lineStart..lineEnd is now the target line's content (excluding \r\n)
        var col = character;
        var lineLen = lineEnd - lineStart;
        if (col > lineLen) col = lineLen;

        // Expand left
        var start = col;
        while (start > 0 && IsIdentifierChar(source[lineStart + start - 1]))
            start--;

        // Expand right
        var end = col;
        while (end < lineLen && IsIdentifierChar(source[lineStart + end]))
            end++;

        return end > start ? source.Substring(lineStart + start, end - start) : null;
    }

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static bool ContainsPosition(LspRange range, int line, int ch)
    {
        var (sl, sc) = (range.Start.Line, range.Start.Character);
        var (el, ec) = (range.End.Line,   range.End.Character);
        if (line < sl || line > el) return false;
        if (line == sl && ch < sc)  return false;
        if (line == el && ch >= ec) return false;
        return true;
    }

    private void PublishDiagnostics(string uri, string source)
    {
        _documentSource[uri] = source;

        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source);
        _documentDiagnostics[uri] = diagnostics;

        _documentSymbols[uri] = VbSymbolProvider.GetSymbols(source);
        _documentFoldings[uri] = VbFoldingProvider.GetFoldings(source);
        _documentDeclaredTypes[uri] = tree is not null
            ? VbScopeAnalyzer.GetDeclaredTypes(tree)
            : new Dictionary<string, string?>();

        var array = new JsonArray();
        foreach (var d in diagnostics)
        {
            array.Add(new JsonObject
            {
                ["range"] = new JsonObject
                {
                    ["start"] = new JsonObject { ["line"] = d.Range.Start.Line, ["character"] = d.Range.Start.Character },
                    ["end"]   = new JsonObject { ["line"] = d.Range.End.Line,   ["character"] = d.Range.End.Character }
                },
                ["message"]  = d.Message,
                ["severity"] = d.Severity,
                ["source"]   = "vb6-lsp"
            });
        }

        SendNotification("textDocument/publishDiagnostics", new JsonObject
        {
            ["uri"] = uri,
            ["diagnostics"] = array
        });
    }

    // ── JSON-RPC framing ──────────────────────────────────────────────────────

    private static JsonObject BuildInitializeResult() => new()
    {
        ["capabilities"] = new JsonObject
        {
            ["textDocumentSync"]       = 1,    // Full sync
            ["hoverProvider"]          = true,
            ["documentSymbolProvider"] = true,
            ["foldingRangeProvider"]   = true,
            ["completionProvider"]     = new JsonObject(),  // No special trigger characters
            ["signatureHelpProvider"]  = new JsonObject
            {
                ["triggerCharacters"] = new JsonArray { "(", "," }
            },
            ["definitionProvider"] = true,
            ["documentHighlightProvider"] = true,
            ["renameProvider"] = true,
            ["documentFormattingProvider"] = true
        }
    };

    private void SendResponse(JsonNode? id, JsonNode? result)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id?.DeepClone() };
        if (result is not null)
            msg["result"] = result;
        else
            msg["result"] = null;
        WriteMessage(msg);
    }

    private void SendNotification(string method, JsonNode @params)
    {
        var msg = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"]  = method,
            ["params"]  = @params
        };
        WriteMessage(msg);
    }

    private void WriteMessage(JsonNode message)
    {
        var json = message.ToJsonString();
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        lock (_writeLock)
        {
            _output.Write(header);
            _output.Write(body);
            _output.Flush();
        }
    }

    private JsonObject? ReadMessage()
    {
        // Read headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = ReadLine();
            if (line is null) return null;
            if (line.Length == 0) break;
            var sep = line.IndexOf(':');
            if (sep > 0)
                headers[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }

        if (!headers.TryGetValue("Content-Length", out var lenStr) ||
            !int.TryParse(lenStr, out var length) || length <= 0)
            return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = _input.Read(body, read, length - read);
            if (n == 0) return null;
            read += n;
        }

        return JsonNode.Parse(Encoding.UTF8.GetString(body))?.AsObject();
    }

    private string? ReadLine()
    {
        var sb = new StringBuilder();
        while (true)
        {
            var b = _input.ReadByte();
            if (b == -1) return sb.Length > 0 ? sb.ToString() : null;
            if (b == '\r') continue;
            if (b == '\n') return sb.ToString();
            sb.Append((char)b);
        }
    }
}
