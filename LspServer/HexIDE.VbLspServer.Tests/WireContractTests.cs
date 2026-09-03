// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Phase 4 acceptance: drive all 17 wire-contract methods through StreamJsonRpc (the real IDE client's
// transport) over an in-memory pipe pair and assert the exact response shapes + the hard requirements.

using System.IO.Pipelines;
using System.Text.Json;
using System.Threading.Channels;
using EmmyLua.LanguageServer.Framework.Server;
using StreamJsonRpc;

namespace HexIDE.VbLspServer.Tests;

public class WireContractTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    private const string Uri = "vb6://module/Test";
    private const string Doc =
        "Option Explicit\n" +        // 0
        "Dim Counter As Integer\n" + // 1  Counter @ char 4
        "Sub DoWork()\n" +           // 2  DoWork  @ char 4
        "Counter = 5\n" +            // 3  Counter @ char 0
        "y = 10\n" +                 // 4  y undeclared → diagnostic (severity 2)
        "End Sub\n";                 // 5

    // ── document sync + diagnostics ─────────────────────────────────────────────────────────────
    [Fact]
    public async Task DidOpen_publishes_diagnostics_with_the_exact_uri()
    {
        await using var h = await StartAsync();
        // A syntax error is the diagnostic signal (the undeclared-var check is default-off in the live path).
        await OpenAsync(h, Uri, "Sub DoWork()\n    @@\nEnd Sub\n");

        var pub = await h.NextDiagnosticsAsync();
        pub.GetProperty("uri").GetString().Should().Be(Uri, "opaque vb6:// URIs must round-trip verbatim");

        var diags = pub.GetProperty("diagnostics");
        diags.ValueKind.Should().Be(JsonValueKind.Array);
        diags.EnumerateArray().Should()
            .Contain(d => d.GetProperty("severity").GetInt32() == 1, "a syntax error is published");
    }

    [Fact]
    public async Task DidChange_republishes_diagnostics()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);
        await h.NextDiagnosticsAsync();

        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[] { new { text = "Sub Clean()\nEnd Sub\n" } }
        });

        var pub = await h.NextDiagnosticsAsync();
        pub.GetProperty("uri").GetString().Should().Be(Uri);
        pub.GetProperty("diagnostics").GetArrayLength().Should().Be(0); // clean source → no diagnostics
    }

    [Fact]
    public async Task DidChange_carrying_a_range_is_refused_and_the_document_is_evicted()
    {
        // The server advertises Full sync, so a ranged change means the client ignored that. Applying it
        // would take the replacement text for a few characters as the WHOLE document.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);
        await h.NextDiagnosticsAsync();

        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[]
            {
                new
                {
                    range = new { start = new { line = 0, character = 4 }, end = new { line = 0, character = 6 } },
                    rangeLength = 2,
                    text = "X = 1",
                },
            },
        });

        // Evicted, not replaced: the refusal publishes an empty diagnostics array, and the document is gone
        // from the store rather than holding "X = 1" as its entire contents.
        var pub = await h.NextDiagnosticsAsync();
        pub.GetProperty("uri").GetString().Should().Be(Uri);
        pub.GetProperty("diagnostics").GetArrayLength().Should().Be(0);

        var symbols = await RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));
        symbols.GetArrayLength().Should().Be(0, "the document was evicted, not mis-applied");
    }

    [Fact]
    public async Task A_refused_ranged_change_cannot_produce_a_whole_document_formatting_edit()
    {
        // This is why refusal evicts rather than logs-and-ignores. Formatting returns a single TextEdit
        // spanning (0,0) to end-of-buffer, so a stale buffer left in the store after a mis-applied ranged
        // change would let a client replace the user's real file with the fragment. With no source entry
        // the path is closed structurally rather than by remembering not to take it.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);
        await h.NextDiagnosticsAsync();

        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[]
            {
                new
                {
                    range = new { start = new { line = 0, character = 0 }, end = new { line = 0, character = 1 } },
                    rangeLength = 1,
                    text = "x",
                },
            },
        });
        await h.NextDiagnosticsAsync();

        var edits = await RequestAsync(h, "textDocument/formatting", new
        {
            textDocument = new { uri = Uri },
            options = new { tabSize = 4, insertSpaces = true },
        });

        edits.GetArrayLength().Should().Be(0, "no source, so no whole-document edit can be emitted");
    }

    [Fact]
    public async Task DidClose_publishes_an_empty_diagnostics_array()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);
        await h.NextDiagnosticsAsync();

        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didClose",
            new { textDocument = new { uri = Uri } });

        var pub = await h.NextDiagnosticsAsync();
        pub.GetProperty("uri").GetString().Should().Be(Uri);
        pub.GetProperty("diagnostics").GetArrayLength().Should().Be(0);
    }

    // ── request methods ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public async Task Hover_returns_a_MarkupContent_object()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var hover = await RequestAsync(h, "textDocument/hover", Pos(Uri, 1, 6)); // over "Counter"
        var contents = hover.GetProperty("contents");
        contents.GetProperty("kind").GetString().Should().Be("plaintext");
        contents.GetProperty("value").GetString().Should().Contain("Counter As Integer");
    }

    [Fact]
    public async Task DocumentSymbol_returns_a_flat_array()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var symbols = await RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        symbols.EnumerateArray().Should()
            .Contain(s => s.GetProperty("name").GetString() == "DoWork" && s.GetProperty("kind").GetInt32() == 6);
        symbols.EnumerateArray().All(s => s.TryGetProperty("selectionRange", out _)).Should().BeTrue();
    }

    [Fact]
    public async Task Completion_returns_a_CompletionList_envelope()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 3, 0));
        list.GetProperty("isIncomplete").GetBoolean().Should().BeFalse();
        var labels = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToList();
        labels.Should().Contain("Dim").And.Contain("Counter"); // keyword + declared name
    }

    [Fact]
    public async Task Definition_returns_a_Location_array()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var def = await RequestAsync(h, "textDocument/definition", Pos(Uri, 2, 6)); // "DoWork"
        def.ValueKind.Should().Be(JsonValueKind.Array);
        def.GetArrayLength().Should().Be(1);
        def[0].GetProperty("uri").GetString().Should().Be(Uri);
        def[0].TryGetProperty("range", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Rename_returns_a_WorkspaceEdit_with_changes()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 6 },
            newName = "Total"
        });
        var changes = edit.GetProperty("changes").GetProperty(Uri);
        changes.ValueKind.Should().Be(JsonValueKind.Array);
        changes.GetArrayLength().Should().Be(2); // Counter appears twice
        changes.EnumerateArray().Should().OnlyContain(e => e.GetProperty("newText").GetString() == "Total");
    }

    // Bug-hunt MED: Property symbols carry a "(Get)" display suffix; Definition compared the suffixed name to the
    // bare identifier and never matched, so Go-To-Definition on a property did nothing.
    [Fact]
    public async Task Definition_resolves_a_Property_despite_its_display_suffix()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Public Property Get Total() As Long\n    Total = 5\nEnd Property\n");

        var def = await RequestAsync(h, "textDocument/definition", Pos(Uri, 0, 22)); // "Total" in the declaration
        def.ValueKind.Should().Be(JsonValueKind.Array);
        def.GetArrayLength().Should().Be(1);
    }

    // Bug-hunt MED: Rename matched the whole word inside string literals and comments too, silently corrupting them.
    [Fact]
    public async Task Rename_skips_string_literals_and_comments()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub S()\n    Text = \"Enter Text here\"  ' set the Text\nEnd Sub\n");

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 5 }, // the code "Text" (starts at char 4)
            newName = "Caption"
        });
        var changes = edit.GetProperty("changes").GetProperty(Uri);
        changes.GetArrayLength().Should().Be(1); // only the code token — not the string literal or the comment
        changes[0].GetProperty("newText").GetString().Should().Be("Caption");
    }

    [Fact]
    public async Task DocumentHighlight_returns_ranges()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var hl = await RequestAsync(h, "textDocument/documentHighlight", Pos(Uri, 1, 6)); // "Counter"
        hl.ValueKind.Should().Be(JsonValueKind.Array);
        hl.GetArrayLength().Should().Be(2);
        hl.EnumerateArray().All(x => x.TryGetProperty("range", out _)).Should().BeTrue();
    }

    [Fact]
    public async Task FoldingRange_returns_an_array()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var folds = await RequestAsync(h, "textDocument/foldingRange", DocParams(Uri));
        folds.ValueKind.Should().Be(JsonValueKind.Array);
        folds.EnumerateArray().Should().Contain(f => f.GetProperty("startLine").GetInt32() == 2); // the Sub
    }

    [Fact]
    public async Task Formatting_returns_a_single_whole_document_TextEdit()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "sub foo()\ndim x\nend sub\n"); // lowercase keywords → reformatted

        var edits = await RequestAsync(h, "textDocument/formatting", new
        {
            textDocument = new { uri = Uri },
            options = new { tabSize = 4, insertSpaces = true }
        });
        edits.ValueKind.Should().Be(JsonValueKind.Array);
        edits.GetArrayLength().Should().Be(1);
        edits[0].GetProperty("newText").GetString().Should().Contain("Sub foo()");
    }

    [Fact]
    public async Task SignatureHelp_returns_signatures_for_a_call()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\nx = Mid(a, b)\nEnd Sub\n"); // Mid( opens at char 7 on line 1

        var sig = await RequestAsync(h, "textDocument/signatureHelp", Pos(Uri, 1, 8)); // inside Mid(
        var signatures = sig.GetProperty("signatures");
        signatures.GetArrayLength().Should().BeGreaterThan(0);
        signatures[0].GetProperty("label").GetString().Should().Contain("Mid");
    }

    [Fact]
    public async Task BuiltinSymbols_returns_the_signature_table()
    {
        await using var h = await StartAsync();

        var symbols = await RequestAsync(h, "vb/builtinSymbols", new { });
        symbols.ValueKind.Should().Be(JsonValueKind.Array);
        symbols.GetArrayLength().Should().BeGreaterThan(0);
        symbols[0].GetProperty("name").GetString().Should().NotBeNullOrEmpty();
        symbols[0].TryGetProperty("signature", out _).Should().BeTrue();
    }

    // ── protocol edge cases (lifted from the original LspServerProtocolTests, via the faithful transport) ─
    [Fact]
    public async Task SignatureHelp_after_a_comma_reports_active_parameter_1()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\nx = Mid(a, b)\nEnd Sub\n"); // "x = Mid(a, b)"

        var sig = await RequestAsync(h, "textDocument/signatureHelp", Pos(Uri, 1, 11)); // after "a, "
        sig.GetProperty("activeParameter").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task SignatureHelp_for_an_unknown_function_returns_null()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\nMyCustomFunc(\nEnd Sub\n");

        var sig = await RequestAsync(h, "textDocument/signatureHelp", Pos(Uri, 1, 13)); // inside the call
        sig.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Hover_over_an_untyped_variable_shows_just_the_name()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\nDim x\nx = 1\nEnd Sub\n"); // untyped Dim

        var hover = await RequestAsync(h, "textDocument/hover", Pos(Uri, 1, 4)); // over "x"
        var value = hover.GetProperty("contents").GetProperty("value").GetString();
        value.Should().Be("x"); // no "As <type>"
    }

    [Fact]
    public async Task Hover_over_a_non_identifier_returns_null()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\nDim x As Integer\nEnd Sub\n");

        var hover = await RequestAsync(h, "textDocument/hover", Pos(Uri, 0, 0)); // the "Sub" keyword
        hover.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Rename_on_whitespace_returns_null()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\n    Dim x As Integer\nEnd Sub\n");

        var rename = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 0 }, // leading whitespace
            newName = "NewName"
        });
        rename.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Rename_of_a_reserved_keyword_returns_null()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\n    Dim x\nEnd Sub\n");

        var rename = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 4 }, // the "Dim" keyword
            newName = "MyVar"
        });
        rename.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Rename_is_case_insensitive_and_finds_all_occurrences()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\n    Dim MyVar As String\n    myvar = 1\n    MYVAR = 2\nEnd Sub\n");

        var rename = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 8 }, // "MyVar"
            newName = "Renamed"
        });
        rename.GetProperty("changes").GetProperty(Uri).GetArrayLength().Should().Be(3); // MyVar/myvar/MYVAR
    }

    [Fact]
    public async Task Formatting_of_already_formatted_source_returns_no_edits()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub Test()\n    Dim x As Integer\nEnd Sub\n"); // already canonical

        var edits = await RequestAsync(h, "textDocument/formatting", new
        {
            textDocument = new { uri = Uri },
            options = new { tabSize = 4, insertSpaces = true }
        });
        edits.ValueKind.Should().Be(JsonValueKind.Array);
        edits.GetArrayLength().Should().Be(0);
    }

    // ── ordering / cadence seams (the load-bearing single-thread guarantees) ─────────────────────
    [Fact]
    public async Task DidChange_is_applied_before_the_next_request_even_without_awaiting_its_publish()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub Old()\nEnd Sub\n");
        await h.NextDiagnosticsAsync(); // drain the didOpen publish

        // Fire a didChange and IMMEDIATELY request documentSymbol WITHOUT awaiting the publish. The
        // single-thread scheduler must run the async didChange handler to completion (updating the store)
        // before dispatching the request — the ordering FlushDocumentAsync + format-on-save rely on.
        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[] { new { text = "Sub Fresh()\nEnd Sub\n" } }
        });
        var symbols = await RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));

        var names = symbols.EnumerateArray().Select(s => s.GetProperty("name").GetString()).ToList();
        names.Should().Contain("Fresh").And.NotContain("Old");
    }

    [Fact]
    public async Task Every_didChange_publishes_exactly_once_no_coalescing()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub A()\nEnd Sub\n");
        await h.NextDiagnosticsAsync(); // drain the didOpen publish

        const int n = 4;
        for (int i = 0; i < n; i++)
            await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
            {
                textDocument = new { uri = Uri, version = i + 2 },
                contentChanges = new[] { new { text = $"Sub A{i}()\nEnd Sub\n" } }
            });

        // Exactly N publishes must arrive (no server-side debounce/coalescing — the IDE's symbol-dropdown
        // refresh piggybacks on each publish's arrival).
        for (int i = 0; i < n; i++)
            (await h.NextDiagnosticsAsync()).GetProperty("uri").GetString().Should().Be(Uri);
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────
    private static object DocParams(string uri) => new { textDocument = new { uri } };
    private static object Pos(string uri, int line, int character) =>
        new { textDocument = new { uri }, position = new { line, character } };

    private static async Task<Harness> StartAsync()
    {
        var h = new Harness();
        await h.Rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize",
            new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } }).WaitAsync(Timeout);
        await h.Rpc.NotifyWithParameterObjectAsync("initialized", new { });
        return h;
    }

    private static Task OpenAsync(Harness h, string uri, string text) =>
        h.Rpc.NotifyWithParameterObjectAsync("textDocument/didOpen",
            new { textDocument = new { uri, languageId = "vb6", version = 1, text } });

    private static Task<JsonElement> RequestAsync(Harness h, string method, object @params) =>
        h.Rpc.InvokeWithParameterObjectAsync<JsonElement>(method, @params).WaitAsync(Timeout);

    private sealed class Harness : IAsyncDisposable
    {
        public JsonRpc Rpc { get; }
        private readonly DiagTarget _diag = new();
        private readonly LanguageServer _server;
        private readonly Task _loop;

        public Harness()
        {
            var c2s = new Pipe();
            var s2c = new Pipe();
            _server = LspServerHost.Create(c2s.Reader.AsStream(), s2c.Writer.AsStream());
            _loop = _server.Run();

            var handler = new HeaderDelimitedMessageHandler(
                c2s.Writer.AsStream(), s2c.Reader.AsStream(), new SystemTextJsonFormatter());
            Rpc = new JsonRpc(handler);
            Rpc.AddLocalRpcTarget(_diag);
            Rpc.StartListening();
        }

        public Task<JsonElement> NextDiagnosticsAsync() =>
            _diag.Channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public async ValueTask DisposeAsync()
        {
            Rpc.Dispose();
            _server.Exit();
            try { await _loop.WaitAsync(Timeout); } catch { /* best effort */ }
        }

        private sealed class DiagTarget
        {
            public Channel<JsonElement> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();

            [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
            public void Publish(JsonElement diagnostics) => Channel.Writer.TryWrite(diagnostics);
        }
    }
}
