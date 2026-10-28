// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The 17-method wire contract. Each handler reads params straight from the request JSON (opaque URIs
// preserved exactly) and emits the exact response shape the IDE client expects. Ported from the original
// dispatcher; result:null over JSON-RPC errors for "nothing found".

using System.Text.Json;

namespace HexIDE.VbLspServer;

internal static class LspRequestHandlers
{
    // ── textDocument/hover → MarkupContent (plaintext) or null ──────────────────────────────────
    public static JsonDocument Hover(DocumentStore store, JsonElement p)
    {
        if (!TryUriPos(p, out var uri, out var line, out var ch)) return LspJson.Null();

        store.Diagnostics.TryGetValue(uri, out var diags);
        store.DeclaredTypes.TryGetValue(uri, out var declaredTypes);
        store.Source.TryGetValue(uri, out var source);

        var parts = new List<string>();

        if (source is not null && declaredTypes is not null)
        {
            var word = VbTextHelpers.GetWordAtPosition(source, line, ch);
            if (word is not null && declaredTypes.TryGetValue(word, out var declaration))
                parts.Add(declaration.TypeText is { } typeAnnotation ? $"{word} As {typeAnnotation}" : word);
        }

        if (diags is not null)
            foreach (var d in diags)
                if (VbTextHelpers.ContainsPosition(d.Range, line, ch))
                    parts.Add(d.Message);

        if (parts.Count == 0) return LspJson.Null();

        var value = string.Join("\n\n", parts);
        return LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("contents");
            w.WriteStartObject();
            w.WriteString("kind", "plaintext");
            w.WriteString("value", value);
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    // ── textDocument/documentSymbol → flat array ────────────────────────────────────────────────
    public static JsonDocument DocumentSymbol(DocumentStore store, JsonElement p)
    {
        if (!TryUri(p, out var uri) || !store.Symbols.TryGetValue(uri, out var symbols))
            return LspJson.EmptyArray();

        return LspJson.Doc(w =>
        {
            w.WriteStartArray();
            foreach (var s in symbols)
            {
                w.WriteStartObject();
                w.WriteString("name", s.Name);
                w.WriteNumber("kind", (int)s.Kind);
                LspJson.WriteRange(w, "range", s.Range);
                // selectionRange: name span synthesized as start .. start + name length
                var sel = new LspRange(s.Range.Start,
                    new LspPosition(s.Range.Start.Line, s.Range.Start.Character + s.Name.Length));
                LspJson.WriteRange(w, "selectionRange", sel);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ── textDocument/foldingRange → array (empty for unknown URI) ────────────────────────────────
    public static JsonDocument FoldingRange(DocumentStore store, JsonElement p)
    {
        if (!TryUri(p, out var uri) || !store.Foldings.TryGetValue(uri, out var foldings))
            return LspJson.EmptyArray();

        return LspJson.Doc(w =>
        {
            w.WriteStartArray();
            foreach (var f in foldings)
            {
                w.WriteStartObject();
                w.WriteNumber("startLine", f.StartLine);
                w.WriteNumber("endLine", f.EndLine);
                if (f.Kind is not null) w.WriteString("kind", f.Kind);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ── textDocument/completion → CompletionList envelope ───────────────────────────────────────
    public static JsonDocument Completion(DocumentStore store, JsonElement p)
    {
        List<LspCompletionItem> items;
        if (TryUri(p, out var uri) && store.DeclaredTypes.TryGetValue(uri, out var declaredTypes))
        {
            items = new List<LspCompletionItem>(VbKeywords.All.Length + declaredTypes.Count);
            var labels = new HashSet<string>(declaredTypes.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var item in VbKeywords.All)
                if (labels.Add(item.Label))
                    items.Add(item);
            foreach (var (name, declaration) in declaredTypes)     // cached — no reparse (parse-once)
                items.Add(new LspCompletionItem(name, declaration.Kind));
        }
        else
        {
            items = [];
        }

        return LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WriteBoolean("isIncomplete", false);
            w.WritePropertyName("items");
            w.WriteStartArray();
            foreach (var item in items)
            {
                w.WriteStartObject();
                w.WriteString("label", item.Label);
                w.WriteNumber("kind", (int)item.Kind);
                if (item.Detail is not null) w.WriteString("detail", item.Detail);
                if (item.InsertText is not null) w.WriteString("insertText", item.InsertText);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    // ── textDocument/signatureHelp → SignatureHelp or null ──────────────────────────────────────
    public static JsonDocument SignatureHelp(DocumentStore store, JsonElement p)
    {
        if (!TryUriPos(p, out var uri, out var line, out var ch) ||
            !store.Source.TryGetValue(uri, out var source))
            return LspJson.Null();

        var (functionName, activeParameter) = VbTextHelpers.FindCallContext(source, line, ch);
        if (functionName is null) return LspJson.Null();

        var entries = VbSignatures.Get(functionName);
        if (entries.Length == 0) return LspJson.Null();

        return LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("signatures");
            w.WriteStartArray();
            foreach (var entry in entries)
            {
                w.WriteStartObject();
                w.WriteString("label", entry.Label);
                w.WritePropertyName("parameters");
                w.WriteStartArray();
                foreach (var param in entry.Parameters)
                {
                    w.WriteStartObject();
                    w.WriteString("label", param);
                    w.WriteEndObject();
                }
                w.WriteEndArray();
                if (entry.Documentation is not null) w.WriteString("documentation", entry.Documentation);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("activeSignature", 0);
            // Past a list's end the list is still active; past any other signature's end the index is sent as it is, so
            // no parameter is marked for an argument VB6 would refuse (#2599).
            var last = entries[0].Parameters.Length - 1;
            w.WriteNumber("activeParameter", entries[0].LastIsList && last >= 0 ? Math.Min(activeParameter, last) : activeParameter);
            w.WriteEndObject();
        });
    }

    // A symbol's bare identifier — strips the property display suffix (" (Get)"/" (Let)"/" (Set)") that
    // VbSymbolProvider appends. VB6 identifiers contain no space, so the text before the first " (" is the name.
    private static string BaseSymbolName(string name)
    {
        int sp = name.IndexOf(" (", StringComparison.Ordinal);
        return sp < 0 ? name : name[..sp];
    }

    // ── textDocument/definition → Location[] or null ────────────────────────────────────────────
    public static JsonDocument Definition(DocumentStore store, JsonElement p)
    {
        if (!TryUriPos(p, out var uri, out var line, out var ch) ||
            !store.Source.TryGetValue(uri, out var source) ||
            !store.Symbols.TryGetValue(uri, out var symbols))
            return LspJson.Null();

        var word = VbTextHelpers.GetWordAtPosition(source, line, ch);
        if (string.IsNullOrEmpty(word)) return LspJson.Null();

        // Property symbols carry a display suffix ("Total (Get)"/"(Let)"/"(Set)"); match — and size the range —
        // on the bare identifier, else Go-To-Definition never resolves a Property (nor an accessor's own name).
        LspSymbol? match = null;
        foreach (var s in symbols)
            if (string.Equals(BaseSymbolName(s.Name), word, StringComparison.OrdinalIgnoreCase)) { match = s; break; }

        if (match is null) return LspJson.Null();

        var range = new LspRange(match.Range.Start,
            new LspPosition(match.Range.Start.Line, match.Range.Start.Character + BaseSymbolName(match.Name).Length));

        return LspJson.Doc(w =>
        {
            w.WriteStartArray();
            w.WriteStartObject();
            w.WriteString("uri", uri);
            LspJson.WriteRange(w, "range", range);
            w.WriteEndObject();
            w.WriteEndArray();
        });
    }

    // ── textDocument/documentHighlight → array or null ──────────────────────────────────────────
    public static JsonDocument DocumentHighlight(DocumentStore store, JsonElement p)
    {
        if (!TryUriPos(p, out var uri, out var line, out var ch) ||
            !store.Source.TryGetValue(uri, out var source))
            return LspJson.Null();

        var word = VbTextHelpers.GetWordAtPosition(source, line, ch);
        if (string.IsNullOrEmpty(word)) return LspJson.Null();

        // Nothing in the header or a member's attribute lines is highlighted, and nothing is highlighted FROM
        // them: a caret on a designer property's name is not asking about the code's uses of that word.
        var regions = VbProtectedRegions.Of(source);
        if (regions.IsProtected(line)) return LspJson.Null();

        var occurrences = VbTextHelpers.FindAllOccurrences(source, word)
            .Where(r => !regions.Touches(r))
            .ToList();
        if (occurrences.Count == 0) return LspJson.Null();

        return LspJson.Doc(w =>
        {
            w.WriteStartArray();
            foreach (var r in occurrences)
            {
                w.WriteStartObject();
                LspJson.WriteRange(w, "range", r);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ── textDocument/rename → WorkspaceEdit{changes}, null, or a refusal ────────────────────────────────────
    public static JsonDocument Rename(DocumentStore store, JsonElement p, out string? refusal)
    {
        refusal = null;
        if (!TryUriPos(p, out var uri, out var line, out var ch) ||
            !store.Source.TryGetValue(uri, out var source))
            return LspJson.Null();

        // Keep the existing name rules; a refusal is distinct from no occurrences (#1015).
        if (!p.TryGetProperty("newName", out var nn) || nn.GetString() is not { } newName ||
            !VbTextHelpers.IsName(newName))
        {
            refusal = $"`{nn}` is not a VB6 name.";
            return LspJson.Null();
        }

        var word = VbTextHelpers.GetWordAtPosition(source, line, ch);
        if (string.IsNullOrEmpty(word)) return LspJson.Null();

        // Don't allow renaming reserved keywords.
        if (VbKeywords.ReservedWords.Contains(word))
        {
            refusal = $"`{word}` is a keyword.";
            return LspJson.Null();
        }

        // The header and members' attribute lines are the IDE's to change (hexide-io/HexIDE#273 task 3.10).
        // A rename is not started from inside them, and matches inside them are left alone: with a form's
        // designer block in view, renaming a local called Caption, Top or Text would otherwise rewrite the
        // layout. The one exception is the renamed member's own name in `Attribute Name.VB_...`, which follows
        // the rename -- and only the member's own: a run describing another member keeps what it says, since
        // the IDE refuses a rename that writes into a run it does not rename (task 4.4) and rewrites those
        // qualifiers itself at the next save.
        var regions = VbProtectedRegions.Of(source);
        if (regions.IsProtected(line))
        {
            refusal = "The caret is in the file's header or a member's attribute lines.";
            return LspJson.Null();
        }

        // Nor is a name the designer block declares -- the form's, a control's, a menu's. Its declaration is a
        // Begin line in the header, which the rename must keep off, so renaming it from the code would leave
        // the code naming a control that does not exist. A local that happens to share a control's name looks
        // exactly the same to a lexical rename, and is declined with it: refusing that rare case is the price
        // of never breaking the common one. A control is renamed in the Properties window, as in VB6.
        if (regions.Declares(word))
        {
            refusal = $"`{word}` is declared by the designer; rename it in the Properties window.";
            return LspJson.Null();
        }

        var occurrences = VbTextHelpers.FindAllOccurrences(source, word)
            .Where(r => !regions.Touches(r) || regions.IsOwnQualifier(r, word))
            .ToList();
        if (occurrences.Count == 0) return LspJson.Null();

        return LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WritePropertyName("changes");
            w.WriteStartObject();
            w.WritePropertyName(uri);
            w.WriteStartArray();
            foreach (var r in occurrences)
            {
                w.WriteStartObject();
                LspJson.WriteRange(w, "range", r);
                w.WriteString("newText", newName);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
            w.WriteEndObject();
        });
    }

    // ── textDocument/formatting → one TextEdit per run of changed lines (empty array if no change) ──
    // Never a whole-document edit: the header and members' attribute lines are left out of every range, not
    // merely left unchanged inside one (VbFormatter.Edits; hexide-io/HexIDE#273 task 3.10).
    public static JsonDocument Formatting(DocumentStore store, JsonElement p)
    {
        if (!TryUri(p, out var uri) || !store.Source.TryGetValue(uri, out var source))
            return LspJson.EmptyArray();

        var edits = VbFormatter.Edits(source);
        if (edits.Count == 0) return LspJson.EmptyArray();

        return LspJson.Doc(w =>
        {
            w.WriteStartArray();
            foreach (var edit in edits)
            {
                w.WriteStartObject();
                LspJson.WriteRange(w, "range", edit.Range);
                w.WriteString("newText", edit.NewText);
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ── vb/builtinSymbols (custom) → [{name, signature, documentation}] ─────────────────────────
    public static JsonDocument BuiltinSymbols()
    {
        return LspJson.Doc(static w =>
        {
            w.WriteStartArray();
            foreach (var (name, entry) in VbSignatures.GetAll())
            {
                w.WriteStartObject();
                w.WriteString("name", name);
                w.WriteString("signature", entry.Label);
                if (entry.Documentation is not null) w.WriteString("documentation", entry.Documentation);
                else w.WriteNull("documentation");
                w.WriteEndObject();
            }
            w.WriteEndArray();
        });
    }

    // ── publishDiagnostics params (server→client) ───────────────────────────────────────────────
    public static JsonDocument BuildPublishParams(string uri, IReadOnlyList<LspDiagnostic> diagnostics)
    {
        return LspJson.Doc(w =>
        {
            w.WriteStartObject();
            w.WriteString("uri", uri);
            w.WritePropertyName("diagnostics");
            w.WriteStartArray();
            foreach (var d in diagnostics)
            {
                w.WriteStartObject();
                LspJson.WriteRange(w, "range", d.Range);
                w.WriteString("message", d.Message);
                w.WriteNumber("severity", d.Severity);
                w.WriteString("source", "vb6-lsp");
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        });
    }

    // ── param readers (missing fields tolerated, matching the original server's null-safe navigation) ─
    private static bool TryUri(JsonElement p, out string uri)
    {
        uri = "";
        if (p.TryGetProperty("textDocument", out var td) &&
            td.TryGetProperty("uri", out var u) && u.GetString() is { } s)
        {
            uri = s;
            return true;
        }
        return false;
    }

    private static bool TryUriPos(JsonElement p, out string uri, out int line, out int ch)
    {
        line = 0; ch = 0;
        if (!TryUri(p, out uri)) return false;
        if (p.TryGetProperty("position", out var pos))
        {
            if (pos.TryGetProperty("line", out var l)) line = l.GetInt32();
            if (pos.TryGetProperty("character", out var c)) ch = c.GetInt32();
        }
        return true;
    }
}
