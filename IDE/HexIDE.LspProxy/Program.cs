/**
 * LSP Debug Proxy
 *
 * Sits between an LSP client and server, logging all messages to stderr.
 *
 * Usage:
 *   LspProxy <command> [args...]
 *
 * Example:
 *   LspProxy python -m vb6_lsp
 *
 * The proxy reads LSP frames from stdin (from the client), logs them, and
 * forwards them to the real server's stdin. It does the same in reverse for
 * messages from the server back to the client. All logging goes to stderr so
 * it does not corrupt the LSP protocol on stdout.
 *
 * Enable via LspServerLocator by setting env var:  VB6_LSP_DEBUG_PROXY=1
 */

using System.Diagnostics;
using System.Net;
using System.Net.WebSockets;
using System.Text;

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: LspProxy <command> [args...]");
    Console.Error.WriteLine("       LspProxy --ws-server <port> [serverExePath]");
    return 1;
}

// WebSocket bridge mode: expose the stdio-only VB LSP server over a WebSocket endpoint.
if (args[0] == "--ws-server")
    return await RunWsServerAsync(args);

var exe = args[0];
var exeArgs = string.Join(" ", args.Skip(1).Select(a => a.Contains(' ') ? $"\"{a}\"" : a));

Log($"[proxy] Starting: {exe} {exeArgs}");

var server = new Process
{
    StartInfo = new ProcessStartInfo
    {
        FileName = exe,
        Arguments = exeArgs,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
    }
};

server.ErrorDataReceived += (_, e) =>
{
    if (e.Data is { Length: > 0 })
        Log($"[server stderr] {e.Data}");
};

server.Start();
server.BeginErrorReadLine();

Log("[proxy] Server started, bridging stdio...");

// stdin (client → proxy) → server stdin
var clientToServer = Task.Run(async () =>
{
    try
    {
        await PumpAsync(
            Console.OpenStandardInput(),
            server.StandardInput.BaseStream,
            "[client→server]");
    }
    catch (Exception ex)
    {
        Log($"[proxy] client→server pump error: {ex.Message}");
    }
    finally
    {
        try { server.StandardInput.Close(); } catch { }
    }
});

// server stdout → proxy stdout (client)
var serverToClient = Task.Run(async () =>
{
    try
    {
        await PumpAsync(
            server.StandardOutput.BaseStream,
            Console.OpenStandardOutput(),
            "[server→client]");
    }
    catch (Exception ex)
    {
        Log($"[proxy] server→client pump error: {ex.Message}");
    }
});

await Task.WhenAny(clientToServer, serverToClient);

try { server.Kill(); } catch { }
await server.WaitForExitAsync();
Log($"[proxy] Server exited with code {server.ExitCode}");

return 0;

// ── helpers ──────────────────────────────────────────────────────────────────

static async Task PumpAsync(Stream source, Stream dest, string direction)
{
    // LSP framing: "Content-Length: N\r\n\r\n" followed by N UTF-8 bytes.
    // We parse the frame so we can log the complete JSON body, then forward it.
    while (true)
    {
        // Read headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(source);
            if (line is null) return;          // EOF
            if (line.Length == 0) break;        // blank line = end of headers
            var sep = line.IndexOf(':');
            if (sep > 0)
                headers[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }

        if (!headers.TryGetValue("Content-Length", out var lenStr) ||
            !int.TryParse(lenStr, out var length) || length <= 0)
        {
            Log($"{direction} [bad frame — no Content-Length]");
            continue;
        }

        // Read body
        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await source.ReadAsync(body.AsMemory(read, length - read));
            if (n == 0) return;
            read += n;
        }

        var json = Encoding.UTF8.GetString(body);
        Log($"{direction} {json}");

        // Forward the complete frame unchanged
        var header = $"Content-Length: {length}\r\n\r\n";
        var headerBytes = Encoding.ASCII.GetBytes(header);
        await dest.WriteAsync(headerBytes);
        await dest.WriteAsync(body);
        await dest.FlushAsync();
    }
}

static async Task<string?> ReadLineAsync(Stream stream)
{
    var sb = new StringBuilder();
    while (true)
    {
        var b = stream.ReadByte();
        if (b == -1) return sb.Length > 0 ? sb.ToString() : null;
        if (b == '\r') continue;
        if (b == '\n') return sb.ToString();
        sb.Append((char)b);
    }
}

static void Log(string message)
{
    var ts = DateTime.Now.ToString("HH:mm:ss.fff");
    Console.Error.WriteLine($"[{ts}] {message}");
}

// ── WebSocket bridge mode ────────────────────────────────────────────────────
// `LspProxy --ws-server <port> [serverExePath]` listens for a WebSocket connection and bridges it to
// a freshly-spawned stdio HexIDE.VbLspServer, translating between WebSocket message framing (one
// JSON-RPC message per WS message) and LSP Content-Length framing on the child's stdio. This lets a
// WebSocket LSP client reach the stdio-only server end-to-end. It moves opaque bytes only (no JSON
// parsing), so it stays dependency-free and MIT — the GPLv3 boundary remains the spawned server.

static async Task<int> RunWsServerAsync(string[] args)
{
    if (args.Length < 2 || !int.TryParse(args[1], out var port) || port is <= 0 or > 65535)
    {
        Console.Error.WriteLine("Usage: LspProxy --ws-server <port> [serverExePath]");
        return 1;
    }

    var serverExe = args.Length >= 3 ? args[2] : FindServerExe();
    if (serverExe is null || !File.Exists(serverExe))
    {
        Log($"[ws-bridge] VB LSP server exe not found ({serverExe ?? "probe failed"})");
        return 1;
    }

    using var listener = new HttpListener();
    listener.Prefixes.Add($"http://localhost:{port}/");
    try
    {
        listener.Start();
    }
    catch (Exception ex)
    {
        Log($"[ws-bridge] failed to listen on port {port}: {ex.Message}");
        return 1;
    }

    Log($"[ws-bridge] listening on ws://localhost:{port}/  ->  {serverExe}");

    while (true)
    {
        HttpListenerContext ctx;
        try
        {
            ctx = await listener.GetContextAsync();
        }
        catch (Exception ex)
        {
            Log($"[ws-bridge] listener stopped: {ex.Message}");
            break;
        }

        if (!ctx.Request.IsWebSocketRequest)
        {
            ctx.Response.StatusCode = 400;
            ctx.Response.Close();
            continue;
        }

        // Handle each connection concurrently (a fresh server per connection) so a wedged or
        // half-open connection never blocks new accepts — a reconnecting client must always be
        // able to re-dial.
        _ = HandleConnectionAsync(ctx, serverExe);
    }

    return 0;
}

static async Task HandleConnectionAsync(HttpListenerContext ctx, string serverExe)
{
    WebSocket ws;
    try
    {
        ws = (await ctx.AcceptWebSocketAsync(subProtocol: null)).WebSocket;
    }
    catch (Exception ex)
    {
        Log($"[ws-bridge] WebSocket accept failed: {ex.Message}");
        ctx.Response.Abort();
        return;
    }

    Log("[ws-bridge] client connected — spawning server");

    using var child = new Process
    {
        StartInfo = new ProcessStartInfo
        {
            FileName = serverExe,
            WorkingDirectory = Path.GetDirectoryName(serverExe) ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        }
    };
    child.ErrorDataReceived += (_, e) =>
    {
        if (e.Data is { Length: > 0 }) Log($"[server stderr] {e.Data}");
    };
    try
    {
        child.Start();
        child.BeginErrorReadLine();
    }
    catch (Exception ex)
    {
        Log($"[ws-bridge] failed to spawn server: {ex.Message}");
        try { ws.Abort(); } catch { /* best effort */ }
        ws.Dispose();
        return;
    }

    using var cts = new CancellationTokenSource();
    // The child stdout read loop uses blocking byte reads, so run both pumps off the accept thread.
    var wsToChild = Task.Run(() => WsToChildPumpAsync(ws, child.StandardInput.BaseStream, cts.Token));
    var childToWs = Task.Run(() => ChildToWsPumpAsync(child.StandardOutput.BaseStream, ws, cts.Token));

    await Task.WhenAny(wsToChild, childToWs);
    cts.Cancel();

    // Abort (don't CloseAsync) — the peer may already be gone, and we must not block teardown
    // waiting for a close handshake.
    try { ws.Abort(); } catch { /* best effort */ }
    ws.Dispose();

    try { child.Kill(); } catch { }
    try { await child.WaitForExitAsync(); } catch { }
    Log("[ws-bridge] connection closed — server stopped");
}

// One inbound WebSocket message -> one Content-Length frame on the child's stdin.
static async Task WsToChildPumpAsync(WebSocket ws, Stream childStdin, CancellationToken ct)
{
    var buffer = new byte[16 * 1024];
    while (!ct.IsCancellationRequested && ws.State == WebSocketState.Open)
    {
        using var message = new MemoryStream();
        WebSocketReceiveResult result;
        do
        {
            try
            {
                result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            }
            catch (Exception ex)
            {
                Log($"[ws-bridge] ws receive ended: {ex.Message}");
                return;
            }
            if (result.MessageType == WebSocketMessageType.Close) return;
            message.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var body = message.ToArray();
        if (body.Length == 0) continue;

        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await childStdin.WriteAsync(header.AsMemory(), ct);
        await childStdin.WriteAsync(body.AsMemory(), ct);
        await childStdin.FlushAsync(ct);
        Log($"[ws->child] {body.Length}b");
    }
}

// One Content-Length frame from the child's stdout -> one outbound WebSocket text message.
static async Task ChildToWsPumpAsync(Stream childStdout, WebSocket ws, CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        int length = -1;
        while (true)
        {
            var line = await ReadLineAsync(childStdout);
            if (line is null) return;       // EOF — server exited
            if (line.Length == 0) break;    // blank line ends headers
            var sep = line.IndexOf(':');
            if (sep > 0
                && line[..sep].Trim().Equals("Content-Length", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(line[(sep + 1)..].Trim(), out var n) && n > 0)
                length = n;
        }
        if (length <= 0) continue;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var c = await childStdout.ReadAsync(body.AsMemory(read, length - read), ct);
            if (c == 0) return;             // EOF mid-frame
            read += c;
        }

        try
        {
            await ws.SendAsync(new ArraySegment<byte>(body), WebSocketMessageType.Text, endOfMessage: true, ct);
            Log($"[child->ws] {length}b");
        }
        catch (Exception ex)
        {
            Log($"[ws-bridge] ws send ended: {ex.Message}");
            return;
        }
    }
}

static string? FindServerExe()
{
    var exeName = OperatingSystem.IsWindows() ? "HexIDE.VbLspServer.exe" : "HexIDE.VbLspServer";
    var dir = AppContext.BaseDirectory;
    foreach (var prefix in new[] { "", "..", "../..", "../../..", "../../../.." })
    {
        var path = Path.GetFullPath(Path.Combine(dir, prefix, exeName));
        if (File.Exists(path)) return path;
    }
    return null;
}
