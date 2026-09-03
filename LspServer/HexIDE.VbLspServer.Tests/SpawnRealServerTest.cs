// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// The seam CI never tests: spawn the REAL server process and drive it over REAL stdio (exactly as the
// IDE's StdioProcessLspTransport does), proving the exe boots, frames LSP correctly, and publishes
// diagnostics. Converts the packaging / ordering / cadence risks from silent to loud.

using System.Diagnostics;
using System.Text.Json;
using System.Threading.Channels;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

public class SpawnRealServerTest
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Spawned_server_boots_over_stdio_and_publishes_a_diagnostic()
    {
        var (fileName, arguments) = ResolveServer();

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            }
        };
        process.ErrorDataReceived += static (_, _) => { }; // drain stderr so the child never blocks
        process.Start();
        process.BeginErrorReadLine();

        var diagTarget = new DiagTarget();
        var handler = new HeaderDelimitedMessageHandler(
            process.StandardInput.BaseStream,
            process.StandardOutput.BaseStream,
            new SystemTextJsonFormatter());
        using var rpc = new JsonRpc(handler);
        rpc.AddLocalRpcTarget(diagTarget);
        rpc.StartListening();

        try
        {
            var init = await rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize",
                new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } }).WaitAsync(Timeout);

            // The only check that the SHIPPED binary advertises anything. The in-process smoke test asserts
            // the payload in detail; this one exists because the client resolves its server by probing the
            // output directory and four parents, so a stale exe left in one of them would be found and
            // would answer `capabilities: {}` — after which a capability-gating client goes silently dark.
            // A spot-check of the load-bearing entries is enough to tell a current binary from an old one.
            var caps = init.GetProperty("capabilities");
            caps.GetProperty("textDocumentSync").GetProperty("change").GetInt32().Should().Be(1);
            caps.GetProperty("hoverProvider").GetBoolean().Should().BeTrue();
            caps.GetProperty("experimental").GetProperty("vbBuiltinSymbols").GetBoolean().Should().BeTrue();

            await rpc.NotifyWithParameterObjectAsync("initialized", new { });

            // A module with a syntax error (the undeclared-var check is default-off in the live path);
            // '@@' is an invalid token, so the real server must publish a syntax diagnostic over stdio.
            await rpc.NotifyWithParameterObjectAsync("textDocument/didOpen", new
            {
                textDocument = new
                {
                    uri = "vb6://module/Spawned",
                    languageId = "vb6",
                    version = 1,
                    text = "Sub Test()\n    @@\nEnd Sub\n",
                }
            });

            var pub = await diagTarget.Channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);
            pub.GetProperty("uri").GetString().Should().Be("vb6://module/Spawned");
            pub.GetProperty("diagnostics").EnumerateArray().Should()
                .Contain(d => d.GetProperty("severity").GetInt32() == 1, "a syntax error is published");

            await rpc.InvokeAsync<object?>("shutdown").WaitAsync(Timeout);
            await rpc.NotifyAsync("exit");
        }
        finally
        {
            rpc.Dispose();
            try { if (!process.WaitForExit(2000)) process.Kill(); } catch { /* already gone */ }
        }
    }

    private sealed class DiagTarget
    {
        public Channel<JsonElement> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();

        [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
        public void Publish(JsonElement diagnostics) => Channel.Writer.TryWrite(diagnostics);
    }

    /// <summary>Prefer the built apphost exe (the real integration path); fall back to `dotnet &lt;dll&gt;`.</summary>
    private static (string FileName, string Arguments) ResolveServer()
    {
        var baseDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "HexIDE.VbLspServer.exe" : "HexIDE.VbLspServer";
        var exe = Path.Combine(baseDir, exeName);
        if (File.Exists(exe))
            return (exe, "");

        var dll = Path.Combine(baseDir, "HexIDE.VbLspServer.dll");
        File.Exists(dll).Should().BeTrue($"the server build output should be next to the test assembly ({baseDir})");
        return ("dotnet", $"\"{dll}\"");
    }
}
