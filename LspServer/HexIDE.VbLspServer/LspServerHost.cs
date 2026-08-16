// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Builds and wires the MIT VB6 language server on the EmmyLua LSP framework.

using System.Text.Json;
using EmmyLua.LanguageServer.Framework.Protocol.JsonRpc;
using EmmyLua.LanguageServer.Framework.Server;
using EmmyLua.LanguageServer.Framework.Server.Scheduler;
using Serilog;

namespace HexIDE.VbLspServer;

/// <summary>
/// The single construction seam for the language server, shared by the stdio entry point
/// (<c>Program</c>) and in-process protocol tests. Both hand it a <see cref="Stream"/> pair — the
/// console's for the real process, a pipe pair for tests.
/// </summary>
public static class LspServerHost
{
    /// <summary>
    /// Create a fully-wired but not-yet-running server over the given streams. Call
    /// <c>await server.Run()</c> to start the read loop; <c>server.Exit()</c> stops it.
    /// </summary>
    public static LanguageServer Create(Stream input, Stream output)
    {
        var ls = LanguageServer.From(input, output);

        // Sequential dispatch is the framework default, pinned explicitly: the IDE client relies on
        // didChange-before-next-request ordering (FlushDocumentAsync + format-on-save).
        ls.SetScheduler(new SingleThreadScheduler());

        var store = new DocumentStore();

        // ── Lifecycle ───────────────────────────────────────────────────────────────────────────
        // NOTE: server capabilities are not advertised (HexIDE's client ignores them and calls every
        // method unconditionally — see the parity matrix). Advertising them for other clients is a
        // deferred nicety (needs the framework's ServerCapabilities surface).
        ls.OnInitialize((_, serverInfo) =>
        {
            serverInfo.Name = "HexIDE VB6 Language Server";
            serverInfo.Version = "1.0.0";
            Log.Information("initialize received");
            return Task.CompletedTask;
        });
        ls.OnInitialized(_ => { Log.Information("client initialized"); return Task.CompletedTask; });
        ls.OnShutdown(() => { Log.Information("shutdown received"); return Task.CompletedTask; });

        // ── Document sync ───────────────────────────────────────────────────────────────────────
        // Publish diagnostics on EVERY didOpen/didChange (no debounce — the IDE's procedure-dropdown
        // refresh piggybacks on the publish arrival). didClose evicts caches and publishes an EMPTY
        // diagnostics array (three cache-eviction consumers depend on it).
        ls.AddNotificationHandler("textDocument/didOpen", async (NotificationMessage m, CancellationToken _) =>
        {
            var p = m.Params!.RootElement;
            if (TryReadDoc(p, out var uri, out var text) && text is not null)
                await PublishAsync(ls, store, uri, text);
        });

        ls.AddNotificationHandler("textDocument/didChange", async (NotificationMessage m, CancellationToken _) =>
        {
            var p = m.Params!.RootElement;
            var uri = ReadUri(p);
            var text = ReadLastChangeText(p); // full-text sync: last contentChange wins
            if (uri is not null && text is not null)
                await PublishAsync(ls, store, uri, text);
        });

        ls.AddNotificationHandler("textDocument/didClose", async (NotificationMessage m, CancellationToken _) =>
        {
            var uri = ReadUri(m.Params!.RootElement);
            if (uri is null) return;
            store.RemoveDocument(uri);
            await ls.SendNotification(new NotificationMessage("textDocument/publishDiagnostics",
                LspRequestHandlers.BuildPublishParams(uri, [])));
        });

        // ── Request handlers (all 17-method contract; result:null over errors for "nothing found") ─
        Request(ls, "textDocument/hover",             (s, p) => LspRequestHandlers.Hover(s, p));
        Request(ls, "textDocument/documentSymbol",    (s, p) => LspRequestHandlers.DocumentSymbol(s, p));
        Request(ls, "textDocument/foldingRange",      (s, p) => LspRequestHandlers.FoldingRange(s, p));
        Request(ls, "textDocument/completion",        (s, p) => LspRequestHandlers.Completion(s, p));
        Request(ls, "textDocument/signatureHelp",     (s, p) => LspRequestHandlers.SignatureHelp(s, p));
        Request(ls, "textDocument/definition",        (s, p) => LspRequestHandlers.Definition(s, p));
        Request(ls, "textDocument/documentHighlight", (s, p) => LspRequestHandlers.DocumentHighlight(s, p));
        Request(ls, "textDocument/rename",            (s, p) => LspRequestHandlers.Rename(s, p));
        Request(ls, "textDocument/formatting",        (s, p) => LspRequestHandlers.Formatting(s, p));
        Request(ls, "vb/builtinSymbols",              (_, _) => LspRequestHandlers.BuiltinSymbols());

        return ls;

        void Request(LanguageServer server, string method, Func<DocumentStore, JsonElement, JsonDocument> handler)
        {
            server.AddRequestHandler(method, (RequestMessage m, CancellationToken _) =>
            {
                var result = m.Params is { } pd ? handler(store, pd.RootElement) : LspJson.Null();
                return Task.FromResult<JsonDocument?>(result);
            });
        }
    }

    private static async Task PublishAsync(LanguageServer ls, DocumentStore store, string uri, string text)
    {
        var diagnostics = await store.UpdateDocumentAsync(uri, text);
        await ls.SendNotification(new NotificationMessage("textDocument/publishDiagnostics",
            LspRequestHandlers.BuildPublishParams(uri, diagnostics)));
    }

    private static string? ReadUri(JsonElement p) =>
        p.TryGetProperty("textDocument", out var td) && td.TryGetProperty("uri", out var u)
            ? u.GetString()
            : null;

    private static bool TryReadDoc(JsonElement p, out string uri, out string? text)
    {
        uri = ""; text = null;
        if (!p.TryGetProperty("textDocument", out var td)) return false;
        if (td.TryGetProperty("uri", out var u) && u.GetString() is { } s) uri = s; else return false;
        if (td.TryGetProperty("text", out var t)) text = t.GetString();
        return true;
    }

    private static string? ReadLastChangeText(JsonElement p)
    {
        if (!p.TryGetProperty("contentChanges", out var changes) || changes.ValueKind != JsonValueKind.Array)
            return null;
        string? text = null;
        foreach (var change in changes.EnumerateArray())
            if (change.TryGetProperty("text", out var t)) text = t.GetString();
        return text;
    }
}
