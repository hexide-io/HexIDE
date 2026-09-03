// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Phase 2 acceptance: the MIT server shell boots over a stream pair and completes the LSP
// lifecycle, driven by StreamJsonRpc (the same transport the real IDE client uses).

using System.IO.Pipelines;
using System.Text.Json;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

public class ServerShellSmokeTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    [Fact]
    public async Task Server_completes_the_lifecycle_and_answers_the_custom_method()
    {
        // Two one-directional pipes form a full-duplex channel between the server and an in-process
        // StreamJsonRpc client over in-memory streams — no child process, no real stdio.
        var c2s = new Pipe(); // client -> server
        var s2c = new Pipe(); // server -> client

        var server = LspServerHost.Create(c2s.Reader.AsStream(), s2c.Writer.AsStream());
        var loop = server.Run();

        var handler = new HeaderDelimitedMessageHandler(
            c2s.Writer.AsStream(),  // client writes requests here (server reads them)
            s2c.Reader.AsStream(),  // client reads responses here (server wrote them)
            new SystemTextJsonFormatter());
        using var rpc = new JsonRpc(handler);
        rpc.StartListening();

        // initialize — VbServerCapabilities declares the payload; the framework fills in ServerInfo.
        var initParams = new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } };
        var init = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams)
                            .WaitAsync(Timeout);

        init.GetProperty("serverInfo").GetProperty("name").GetString()
            .Should().Be("HexIDE VB6 Language Server");

        // This used to assert only that a "capabilities" key existed — which an empty object satisfies,
        // under a comment claiming the framework aggregated them (it does not; RegisterCapability is
        // abstract on every handler base). A test that passes for `{}` while its comment says otherwise is
        // how the server shipped advertising nothing at all. Assert the payload, by name and by value.
        var caps = init.GetProperty("capabilities");

        caps.GetProperty("textDocumentSync").GetProperty("openClose").GetBoolean().Should().BeTrue();
        caps.GetProperty("textDocumentSync").GetProperty("change").GetInt32()
            .Should().Be(1, "Full sync — the client spec requires whole-document synchronization");
        caps.GetProperty("hoverProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("documentSymbolProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("foldingRangeProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("definitionProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("documentHighlightProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("renameProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("documentFormattingProvider").GetBoolean().Should().BeTrue();
        caps.GetProperty("completionProvider").GetProperty("resolveProvider").GetBoolean()
            .Should().BeFalse("there is no completionItem/resolve handler");
        caps.GetProperty("signatureHelpProvider").GetProperty("triggerCharacters")
            .EnumerateArray().Select(t => t.GetString()).Should().BeEquivalentTo(["(", ","]);
        caps.GetProperty("experimental").GetProperty("vbBuiltinSymbols").GetBoolean().Should().BeTrue();

        // The negative half, which is the half that guards against advertising what we do not implement.
        // Overstating invites requests nothing answers, which is the same defect pointed the other way.
        caps.TryGetProperty("referencesProvider", out _).Should().BeFalse();
        caps.TryGetProperty("codeActionProvider", out _).Should().BeFalse();
        caps.TryGetProperty("documentRangeFormattingProvider", out _).Should().BeFalse();
        caps.TryGetProperty("semanticTokensProvider", out _).Should().BeFalse();
        caps.TryGetProperty("diagnosticProvider", out _)
            .Should().BeFalse("this server PUSHES diagnostics; advertising pull would invite "
                            + "textDocument/diagnostic requests nothing answers");

        await rpc.NotifyWithParameterObjectAsync("initialized", new { });

        // vb/builtinSymbols returns the built-in signature table: [{name, signature, documentation}, ...].
        var symbols = await rpc.InvokeWithParameterObjectAsync<JsonElement>("vb/builtinSymbols", new { })
                               .WaitAsync(Timeout);
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        symbols.GetArrayLength().Should().BeGreaterThan(0);
        symbols[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();

        // Clean lifecycle teardown.
        await rpc.InvokeAsync<object?>("shutdown").WaitAsync(Timeout);
        await rpc.NotifyAsync("exit");

        server.Exit();
        await loop.WaitAsync(Timeout);
    }
}
