using System.IO.Pipelines;
using System.Text;
using System.Text.Json.Nodes;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Protocol-level tests for LspServer. Drives the server via in-process pipes —
/// no subprocess needed.
/// </summary>
public class LspServerProtocolTests : IDisposable
{
    private readonly Pipe _clientToServer = new();
    private readonly Pipe _serverToClient = new();
    private readonly Task _serverTask;
    private readonly CancellationTokenSource _cts = new();
    private int _nextId;

    public LspServerProtocolTests()
    {
        var server = new LspServer(
            _clientToServer.Reader.AsStream(),
            _serverToClient.Writer.AsStream());

        _serverTask = Task.Run(server.Run, _cts.Token);
    }

    public void Dispose()
    {
        _cts.Cancel();
        _clientToServer.Writer.Complete();
        try { _serverTask.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task SendAsync(JsonObject message)
    {
        var json = message.ToJsonString();
        var body = Encoding.UTF8.GetBytes(json);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        await _clientToServer.Writer.WriteAsync(header);
        await _clientToServer.Writer.WriteAsync(body);
        await _clientToServer.Writer.FlushAsync();
    }

    private async Task<JsonObject?> ReceiveAsync(CancellationToken ct = default)
    {
        var stream = _serverToClient.Reader.AsStream();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(5));

        // Read headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (true)
        {
            var line = await ReadLineAsync(stream, cts.Token);
            if (line is null) return null;
            if (line.Length == 0) break;
            var sep = line.IndexOf(':');
            if (sep > 0)
                headers[line[..sep].Trim()] = line[(sep + 1)..].Trim();
        }

        if (!headers.TryGetValue("Content-Length", out var lenStr) ||
            !int.TryParse(lenStr, out var length))
            return null;

        var body = new byte[length];
        var read = 0;
        while (read < length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read, length - read), cts.Token);
            if (n == 0) return null;
            read += n;
        }

        return JsonNode.Parse(Encoding.UTF8.GetString(body))?.AsObject();
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (true)
        {
            var n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) return sb.Length > 0 ? sb.ToString() : null;
            var ch = (char)buf[0];
            if (ch == '\r') continue;
            if (ch == '\n') return sb.ToString();
            sb.Append(ch);
        }
    }

    private JsonObject MakeRequest(string method, JsonNode? @params = null)
    {
        var id = Interlocked.Increment(ref _nextId);
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        return msg;
    }

    private static JsonObject MakeNotification(string method, JsonNode? @params = null)
    {
        var msg = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method };
        if (@params is not null) msg["params"] = @params;
        return msg;
    }

    private async Task InitializeAsync()
    {
        await SendAsync(MakeRequest("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["capabilities"] = new JsonObject()
        }));
        var response = await ReceiveAsync();
        response.Should().NotBeNull();
        response!["result"]!["capabilities"]!["textDocumentSync"]!.GetValue<int>().Should().Be(1);

        await SendAsync(MakeNotification("initialized"));
    }

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Initialize_ReturnsCapabilitiesWithFullTextDocumentSync()
    {
        await InitializeAsync();
        // If we got here without exception, initialize succeeded
    }

    [Fact]
    public async Task DidOpen_ValidCode_PublishesEmptyDiagnostics()
    {
        await InitializeAsync();

        await SendAsync(MakeNotification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///test.bas",
                ["languageId"] = "vb6",
                ["version"] = 1,
                ["text"] = "Sub Hello()\nEnd Sub"
            }
        }));

        var notification = await ReceiveAsync();
        notification.Should().NotBeNull();
        notification!["method"]!.GetValue<string>().Should().Be("textDocument/publishDiagnostics");
        notification["params"]!["diagnostics"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task DidOpen_InvalidCode_PublishesDiagnostics()
    {
        await InitializeAsync();

        await SendAsync(MakeNotification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///test.bas",
                ["languageId"] = "vb6",
                ["version"] = 1,
                ["text"] = "@@@"
            }
        }));

        var notification = await ReceiveAsync();
        notification.Should().NotBeNull();
        notification!["method"]!.GetValue<string>().Should().Be("textDocument/publishDiagnostics");
        notification["params"]!["diagnostics"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task DidChange_UpdatesDocument_PublishesDiagnostics()
    {
        await InitializeAsync();

        // Open with valid code
        await SendAsync(MakeNotification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///test.bas",
                ["languageId"] = "vb6",
                ["version"] = 1,
                ["text"] = "Sub Hello()\nEnd Sub"
            }
        }));
        await ReceiveAsync(); // discard first publishDiagnostics

        // Change to invalid code
        await SendAsync(MakeNotification("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///test.bas", ["version"] = 2 },
            ["contentChanges"] = new JsonArray { new JsonObject { ["text"] = "@@@" } }
        }));

        var notification = await ReceiveAsync();
        notification.Should().NotBeNull();
        notification!["method"]!.GetValue<string>().Should().Be("textDocument/publishDiagnostics");
        notification["params"]!["diagnostics"]!.AsArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task DidClose_ClearsDiagnostics()
    {
        await InitializeAsync();

        await SendAsync(MakeNotification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"] = "file:///test.bas",
                ["languageId"] = "vb6",
                ["version"] = 1,
                ["text"] = "@@@"
            }
        }));
        await ReceiveAsync(); // discard publishDiagnostics

        await SendAsync(MakeNotification("textDocument/didClose", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = "file:///test.bas" }
        }));

        var notification = await ReceiveAsync();
        notification.Should().NotBeNull();
        notification!["method"]!.GetValue<string>().Should().Be("textDocument/publishDiagnostics");
        notification["params"]!["diagnostics"]!.AsArray().Should().BeEmpty();
    }

    [Fact]
    public async Task Shutdown_RespondsWithNull()
    {
        await InitializeAsync();

        var shutdownRequest = MakeRequest("shutdown");
        await SendAsync(shutdownRequest);

        var response = await ReceiveAsync();
        response.Should().NotBeNull();
        response!["id"]!.GetValue<int>().Should().Be(shutdownRequest["id"]!.GetValue<int>());
        response["result"].Should().BeNull();
    }

    // ── signatureHelp tests ───────────────────────────────────────────────────

    private async Task OpenDocumentAsync(string uri, string text)
    {
        await SendAsync(MakeNotification("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject
            {
                ["uri"]        = uri,
                ["languageId"] = "vb6",
                ["version"]    = 1,
                ["text"]       = text
            }
        }));
        await ReceiveAsync(); // discard publishDiagnostics
    }

    private async Task<JsonObject?> RequestSignatureHelpAsync(string uri, int line, int character)
    {
        await SendAsync(MakeRequest("textDocument/signatureHelp", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"]     = new JsonObject { ["line"] = line, ["character"] = character }
        }));
        return await ReceiveAsync();
    }

    [Fact]
    public async Task SignatureHelp_KnownBuiltin_ReturnsSignature()
    {
        await InitializeAsync();
        // Source line 2: "    s = Mid(" — cursor is after '(' at column 12 (0-indexed)
        // 4 spaces + "s = Mid(" = 4+8 = 12 chars, cursor at 12
        await OpenDocumentAsync("file:///test.bas", "Sub Test()\n    Dim s As String\n    s = Mid(\nEnd Sub");

        var resp = await RequestSignatureHelpAsync("file:///test.bas", 2, 12);
        resp.Should().NotBeNull();
        var result = resp!["result"];
        result.Should().NotBeNull();
        result!["signatures"]!.AsArray().Should().NotBeEmpty();
        result["signatures"]!.AsArray()[0]!["label"]!.GetValue<string>().Should().Contain("Mid");
        result["activeParameter"]!.GetValue<int>().Should().Be(0);
    }

    [Fact]
    public async Task SignatureHelp_AfterOneComma_ReturnsActiveParameter1()
    {
        await InitializeAsync();
        // Source line 2: "    s = Mid(s, " — cursor after the space following comma = column 15
        // 4 spaces + "s = Mid(s, " = 4+11 = 15 chars, cursor at 15
        await OpenDocumentAsync("file:///test.bas", "Sub Test()\n    Dim s As String\n    s = Mid(s, \nEnd Sub");

        var resp = await RequestSignatureHelpAsync("file:///test.bas", 2, 15);
        resp.Should().NotBeNull();
        var result = resp!["result"];
        result.Should().NotBeNull();
        result!["activeParameter"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task SignatureHelp_UnknownFunction_ReturnsNull()
    {
        await InitializeAsync();
        // Source line 1: "    MyCustomFunc(" — cursor after '(' at column 17
        await OpenDocumentAsync("file:///test.bas", "Sub Test()\n    MyCustomFunc(\nEnd Sub");

        var resp = await RequestSignatureHelpAsync("file:///test.bas", 1, 17);
        resp.Should().NotBeNull();
        resp!["result"].Should().BeNull();
    }

    [Fact]
    public async Task Initialize_AdvertisesSignatureHelpProvider()
    {
        await InitializeAsync();
        // Source line 1: "    Len(" — cursor after '(' at column 8
        await OpenDocumentAsync("file:///test.bas", "Sub Test()\n    Len(\nEnd Sub");
        var resp = await RequestSignatureHelpAsync("file:///test.bas", 1, 8);
        resp.Should().NotBeNull();
        resp!["result"]!["signatures"]!.AsArray().Should().NotBeEmpty();
    }

    // ── hover type enrichment tests ───────────────────────────────────────────

    private async Task<JsonObject?> RequestHoverAsync(string uri, int line, int character)
    {
        await SendAsync(MakeRequest("textDocument/hover", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"]     = new JsonObject { ["line"] = line, ["character"] = character }
        }));
        return await ReceiveAsync();
    }

    [Fact]
    public async Task Hover_DeclaredVariable_ShowsTypeAnnotation()
    {
        await InitializeAsync();
        // Line 1: "    Dim x As Integer"
        await OpenDocumentAsync("file:///hover.bas",
            "Sub Test()\n    Dim x As Integer\n    x = 1\nEnd Sub");

        // Hover over 'x' on line 1 (0-indexed), character 8
        var resp = await RequestHoverAsync("file:///hover.bas", 1, 8);
        resp.Should().NotBeNull();
        var value = resp!["result"]?["contents"]?["value"]?.GetValue<string>();
        value.Should().Contain("x As Integer");
    }

    [Fact]
    public async Task Hover_UntypedVariable_ShowsJustName()
    {
        await InitializeAsync();
        // Line 1: "    Dim x"
        await OpenDocumentAsync("file:///hover2.bas",
            "Sub Test()\n    Dim x\n    x = 1\nEnd Sub");

        var resp = await RequestHoverAsync("file:///hover2.bas", 1, 8);
        resp.Should().NotBeNull();
        var value = resp!["result"]?["contents"]?["value"]?.GetValue<string>();
        value.Should().NotBeNullOrEmpty();
        value.Should().Contain("x");
    }

    [Fact]
    public async Task Hover_NonIdentifier_ReturnsNull()
    {
        await InitializeAsync();
        // Hover over a keyword / empty area where there's no declared name
        await OpenDocumentAsync("file:///hover3.bas",
            "Sub Test()\n    Dim x As Integer\nEnd Sub");

        // Hover on "Sub" keyword (line 0, char 0) — "Sub" is not in DeclaredTypes
        var resp = await RequestHoverAsync("file:///hover3.bas", 0, 0);
        resp.Should().NotBeNull();
        // result should be null (no type info, no diagnostic at this position)
        resp!["result"].Should().BeNull();
    }

    // ── rename tests ──────────────────────────────────────────────────────────

    private async Task<JsonObject?> RequestRenameAsync(string uri, int line, int character, string newName)
    {
        await SendAsync(MakeRequest("textDocument/rename", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["position"]     = new JsonObject { ["line"] = line, ["character"] = character },
            ["newName"]      = newName
        }));
        return await ReceiveAsync();
    }

    [Fact]
    public async Task Rename_Variable_ReturnsWorkspaceEditWithAllOccurrences()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///rename1.bas",
            "Sub Test()\n    Dim counter As Integer\n    counter = 1\n    Debug.Print counter\nEnd Sub");

        // Rename "counter" (line 1, char 8) to "total"
        var resp = await RequestRenameAsync("file:///rename1.bas", 1, 8, "total");
        resp.Should().NotBeNull();
        var result = resp!["result"];
        result.Should().NotBeNull();

        var changes = result!["changes"]!["file:///rename1.bas"]!.AsArray();
        // Should find 3 occurrences: declaration, assignment, Debug.Print usage
        changes.Should().HaveCount(3);

        foreach (var edit in changes)
        {
            edit!["newText"]!.GetValue<string>().Should().Be("total");
        }
    }

    [Fact]
    public async Task Rename_OnWhitespace_ReturnsNull()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///rename2.bas",
            "Sub Test()\n    Dim x As Integer\nEnd Sub");

        // Rename at whitespace (line 1, char 0 = leading spaces)
        var resp = await RequestRenameAsync("file:///rename2.bas", 1, 0, "NewName");
        resp.Should().NotBeNull();
        resp!["result"].Should().BeNull();
    }

    [Fact]
    public async Task Rename_CaseInsensitive_FindsAllOccurrences()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///rename3.bas",
            "Sub Test()\n    Dim MyVar As String\n    myvar = \"hello\"\n    MYVAR = \"world\"\nEnd Sub");

        // Rename "MyVar" (line 1, char 8) to "renamed"
        var resp = await RequestRenameAsync("file:///rename3.bas", 1, 8, "renamed");
        resp.Should().NotBeNull();
        var result = resp!["result"];
        result.Should().NotBeNull();

        var changes = result!["changes"]!["file:///rename3.bas"]!.AsArray();
        // All 3 case variants should be found
        changes.Should().HaveCount(3);
    }

    [Fact]
    public async Task Rename_OnReservedKeyword_ReturnsNull()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///rename4.bas",
            "Sub Test()\n    Dim x As Integer\nEnd Sub");

        // Try to rename "Sub" (line 0, char 0) — reserved keyword
        var resp = await RequestRenameAsync("file:///rename4.bas", 0, 0, "MyProc");
        resp.Should().NotBeNull();
        resp!["result"].Should().BeNull();
    }

    [Fact]
    public async Task Rename_OnReservedKeyword_CaseInsensitive()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///rename5.bas",
            "dim x as integer");

        // Try to rename "dim" (lowercase) — still a reserved keyword
        var resp = await RequestRenameAsync("file:///rename5.bas", 0, 0, "MyVar");
        resp.Should().NotBeNull();
        resp!["result"].Should().BeNull();
    }

    [Fact]
    public async Task Initialize_AdvertisesRenameProvider()
    {
        await SendAsync(MakeRequest("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["capabilities"] = new JsonObject()
        }));
        var resp = await ReceiveAsync();
        resp.Should().NotBeNull();
        var caps = resp!["result"]!["capabilities"];
        caps!["renameProvider"]!.GetValue<bool>().Should().BeTrue();
    }

    // ── formatting tests ──────────────────────────────────────────────────────

    private async Task<JsonObject?> RequestFormattingAsync(string uri)
    {
        await SendAsync(MakeRequest("textDocument/formatting", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri },
            ["options"]      = new JsonObject { ["tabSize"] = 4, ["insertSpaces"] = true }
        }));
        return await ReceiveAsync();
    }

    [Fact]
    public async Task Formatting_FixesIndentAndCasing()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///fmt1.bas",
            "sub test()\ndim x as integer\nx = 1\nend sub");

        var resp = await RequestFormattingAsync("file:///fmt1.bas");
        resp.Should().NotBeNull();
        var result = resp!["result"]!.AsArray();
        result.Should().NotBeEmpty();

        // Single whole-document edit
        var newText = result[0]!["newText"]!.GetValue<string>();
        newText.Should().Contain("Sub test()");
        newText.Should().Contain("    Dim x As Integer");
        newText.Should().Contain("End Sub");
    }

    [Fact]
    public async Task Formatting_AlreadyFormatted_ReturnsEmptyEdits()
    {
        await InitializeAsync();
        await OpenDocumentAsync("file:///fmt2.bas",
            "Sub Test()\n    Dim x As Integer\nEnd Sub");

        var resp = await RequestFormattingAsync("file:///fmt2.bas");
        resp.Should().NotBeNull();
        var result = resp!["result"]!.AsArray();
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task Initialize_AdvertisesFormattingProvider()
    {
        await SendAsync(MakeRequest("initialize", new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["capabilities"] = new JsonObject()
        }));
        var resp = await ReceiveAsync();
        resp.Should().NotBeNull();
        var caps = resp!["result"]!["capabilities"];
        caps!["documentFormattingProvider"]!.GetValue<bool>().Should().BeTrue();
    }
}
