// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// What this server tells a client it can do.

using System.Text.Json;
using EmmyLua.LanguageServer.Framework;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Client.ClientCapabilities;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server;
using EmmyLua.LanguageServer.Framework.Protocol.Capabilities.Server.Options;
using EmmyLua.LanguageServer.Framework.Protocol.Message.TextDocument;
using EmmyLua.LanguageServer.Framework.Server.Handler;

namespace HexIDE.VbLspServer;

/// <summary>
/// Declares the server's capabilities during <c>initialize</c>.
///
/// <para>
/// Until this existed the server answered <c>"capabilities": {}</c> — literally nothing. That was harmless
/// only because the one client was ours and called every method unconditionally; a conformant client sends
/// nothing after a handshake that advertises nothing, so the server appeared mute to every other editor.
/// </para>
///
/// <para>
/// <b>Why this is a handler that handles nothing.</b> The framework collects capabilities by walking its
/// registered handlers and calling <see cref="RegisterCapability"/> on each — so a handler whose
/// <see cref="RegisterHandler"/> is a no-op still contributes them. That keeps the declaration in one
/// readable block instead of scattered across twelve classes, and leaves the request/notification dispatch
/// path untouched. The alternative — migrating the twelve string registrations to the framework's typed
/// handler bases — buys <em>no</em> capability code for free (<c>RegisterCapability</c> is abstract on
/// every base), changes the wire shapes that the client's records and the contract tests both pin, and
/// forces nine stub methods for features this server does not implement. Writing stubs would be a strange
/// way to stop overstating what the server does.
/// </para>
///
/// <para>
/// <b>Nothing here is advertised that is not implemented.</b> Overstating is the same defect as
/// understating, pointed the other way: it invites requests nothing answers. Each entry below is backed by
/// a live handler registration in <see cref="LspServerHost"/>.
/// </para>
/// </summary>
internal sealed class VbServerCapabilities : IJsonHandler
{
    // Nothing to register: this handler exists purely to declare. The dispatch path stays where it is.
    public void RegisterHandler(LSPCommunicationBase communication) { }

    public void RegisterDynamicCapability(
        EmmyLua.LanguageServer.Framework.Server.LanguageServer server, ClientCapabilities clientCapabilities)
    {
    }

    /// <param name="clientCapabilities">
    /// Deliberately unused. Advertising conditionally on what the client declared produces a server whose
    /// behaviour depends on who asked, which is far harder to reason about than a fixed answer — and our own
    /// client declares almost nothing, so conditioning on it would under-advertise to ourselves.
    /// </param>
    public void RegisterCapability(ServerCapabilities serverCapabilities, ClientCapabilities clientCapabilities)
    {
        // Full sync only, and that is a contract rather than a limitation: the client spec requires
        // documents be synchronized in full because it removes an entire class of desynchronization bug at
        // negligible cost for files of the size VB6 projects contain. Advertising Incremental would invite
        // ranged changes this server deliberately refuses (see LspServerHost.ReadContentChange).
        serverCapabilities.TextDocumentSync = new TextDocumentSyncOptions
        {
            OpenClose = true,
            Change = TextDocumentSyncKind.Full,
        };

        serverCapabilities.HoverProvider = true;
        serverCapabilities.DocumentSymbolProvider = true;
        serverCapabilities.FoldingRangeProvider = true;
        serverCapabilities.DefinitionProvider = true;
        serverCapabilities.DocumentHighlightProvider = true;
        serverCapabilities.RenameProvider = true;
        serverCapabilities.DocumentFormattingProvider = true;

        // resolveProvider: false is not a default — there is no completionItem/resolve handler, and the
        // framework's property is non-nullable so it serializes either way. Saying false is the honest half.
        serverCapabilities.CompletionProvider = new CompletionOptions { ResolveProvider = false };

        // These two characters genuinely drive it: the call-context scan walks backwards for an unclosed
        // '(' and counts ',' to find the active parameter.
        serverCapabilities.SignatureHelpProvider = new SignatureHelpOptions
        {
            TriggerCharacters = ["(", ","],
        };

        // vb/builtinSymbols has no standard slot, and `experimental` is where the protocol says to put one.
        // Without it a capability-respecting client has nothing to gate that method on.
        serverCapabilities.Experimental = JsonDocument.Parse("""{"vbBuiltinSymbols":true}""");

        // Everything else is deliberately absent. Three worth naming because they look like omissions:
        //
        //   diagnosticProvider          — that is PULL diagnostics. This server pushes on every open and
        //                                 change, which the spec pins. Advertising it would invite
        //                                 textDocument/diagnostic requests nothing answers.
        //   textDocumentSync.save       — no didSave/willSave handler exists, so omitting it stops a
        //                                 conformant client sending them.
        //   documentRangeFormatting     — only whole-document formatting is implemented.
        //
        // positionEncoding is also left at its default of utf-16, which is correct rather than lazy: the
        // text helpers index C# strings, i.e. UTF-16 code units.
    }
}
