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

        // initialize — the framework aggregates capabilities from handlers and fills in ServerInfo.
        var initParams = new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } };
        var init = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize", initParams)
                            .WaitAsync(Timeout);

        init.TryGetProperty("capabilities", out _).Should().BeTrue("initialize must return a capabilities object");
        init.GetProperty("serverInfo").GetProperty("name").GetString()
            .Should().Be("HexIDE VB6 Language Server");

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
