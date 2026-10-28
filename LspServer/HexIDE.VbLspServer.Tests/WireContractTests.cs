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

[Collection("Server timing")]
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
    [Theory]
    [InlineData("\r")]
    [InlineData("\r\n\r\n\r")]
    public async Task Diagnostics_below_a_lone_CR_use_editor_lines(string breaks)
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()" + breaks + "    @@\rEnd Sub\r");

        var pub = await h.NextDiagnosticsAsync();
        pub.GetProperty("diagnostics").EnumerateArray().Should().Contain(d =>
            d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32() ==
            breaks.ReplaceLineEndings("\n").Length);
    }

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
    public async Task A_refused_ranged_change_cannot_produce_a_formatting_edit()
    {
        // This is why refusal evicts rather than logs-and-ignores. Formatting answers with edits whose ranges
        // and text come from the stored buffer, so a stale buffer left in the store after a mis-applied ranged
        // change would have a client apply the fragment's formatting to the user's real file. (It used to be
        // one edit spanning the whole buffer, which replaced the file with the fragment outright.) With no
        // source entry the path is closed structurally rather than by remembering not to take it.
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

        edits.GetArrayLength().Should().Be(0, "no source, so no edit can be emitted");
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
        await h.NextDiagnosticsAsync();

        var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 3, 0));
        list.GetProperty("isIncomplete").GetBoolean().Should().BeFalse();
        var labels = list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString()).ToList();
        labels.Should().Contain("Dim").And.Contain("Counter"); // keyword + declared name
        labels.Distinct(StringComparer.OrdinalIgnoreCase).Count().Should().Be(labels.Count);
        var items = list.GetProperty("items").EnumerateArray().ToList();
        items.Single(i => i.GetProperty("label").GetString() == "DoWork").GetProperty("kind").GetInt32().Should().Be(2);
        items.Single(i => i.GetProperty("label").GetString() == "Counter").GetProperty("kind").GetInt32().Should().Be(6);
    }

    [Theory]
    [InlineData("Sub Worker()\nEnd Sub", "Worker", 2)]
    [InlineData("Function Calculate() As Long\nEnd Function", "Calculate", 3)]
    [InlineData("Const Limit = 5", "Limit", 21)]
    [InlineData("Property Get Total() As Long\nEnd Property", "Total", 10)]
    [InlineData("Property Let Total(ByVal Value As Long)\nEnd Property", "Total", 10)]
    [InlineData("Property Set Total(ByVal Value As Object)\nEnd Property", "Total", 10)]
    [InlineData("Enum Choices\nFirstChoice\nEnd Enum", "Choices", 13)]
    [InlineData("Enum Choices\nFirstChoice\nEnd Enum", "FirstChoice", 20)]
    [InlineData("Type RecordData\nValue As Long\nEnd Type", "RecordData", 22)]
    public async Task Completion_preserves_declaration_kinds(string source, string label, int kind)
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, source + "\n");
        await h.NextDiagnosticsAsync();

        var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
        var item = list.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("label").GetString() == label);
        item.GetProperty("kind").GetInt32().Should().Be(kind);
    }

    [Fact]
    public async Task Completion_keeps_Date_as_a_single_function()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);
        await h.NextDiagnosticsAsync();

        var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
        var dates = list.GetProperty("items").EnumerateArray()
            .Where(i => string.Equals(i.GetProperty("label").GetString(), "Date", StringComparison.OrdinalIgnoreCase))
            .ToList();
        dates.Should().ContainSingle();
        dates[0].GetProperty("kind").GetInt32().Should().Be(3);
    }

    [Theory]
    [InlineData("Dim tIME As Long", "tIME", 6)]
    [InlineData("Function fORMAT() As Long\nEnd Function", "fORMAT", 3)]
    public async Task Completion_prefers_declared_names_over_static_entries(string source, string label, int kind)
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, source + "\n");
        await h.NextDiagnosticsAsync();

        var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
        var matches = list.GetProperty("items").EnumerateArray()
            .Where(i => string.Equals(i.GetProperty("label").GetString(), label, StringComparison.OrdinalIgnoreCase))
            .ToList();
        matches.Should().ContainSingle();
        matches[0].GetProperty("label").GetString().Should().Be(label);
        matches[0].GetProperty("kind").GetInt32().Should().Be(kind);
        matches[0].TryGetProperty("insertText", out _).Should().BeFalse();
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
    public async Task Formatting_returns_one_TextEdit_per_run_of_changed_lines()
    {
        await using var h = await StartAsync();
        // Lines 0-2 change, line 3 is already formatted, line 4 changes again.
        await OpenAsync(h, Uri, "sub foo()\ndim x\nend sub\n' note\nsub bar()\nEnd Sub\n");

        var edits = await RequestAsync(h, "textDocument/formatting", new
        {
            textDocument = new { uri = Uri },
            options = new { tabSize = 4, insertSpaces = true }
        });
        edits.ValueKind.Should().Be(JsonValueKind.Array);
        edits.EnumerateArray().Select(RangeOf).Select(r => (r.Start.Line, r.Start.Character, r.End.Line, r.End.Character))
            .Should().Equal((0, 0, 2, 7), (4, 0, 4, 9));
        edits[0].GetProperty("newText").GetString().Should().Be("Sub foo()\n    Dim x\nEnd Sub");
        edits[1].GetProperty("newText").GetString().Should().Be("Sub bar()");
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

    // A list's last parameter takes every argument past it, so it stays active. Any other signature has no parameter
    // for an extra argument, which VB6 refuses, so the argument's own index goes out and nothing is marked (#2599).
    [Theory]
    [InlineData("Array(1, 2, ", 0)]
    [InlineData("Choose(1, 2, 3, 4, ", 2)]
    [InlineData("Switch(True, 1, False, 2, ", 2)]
    [InlineData("Mid(a, 1, 2, ", 3)]
    [InlineData("Mid(a, 1, 2, 3, ", 4)]
    [InlineData("Len(a, b, ", 2)]
    public async Task SignatureHelp_past_the_last_parameter_keeps_only_a_list_active(
        string call, int expectedParameter)
    {
        await using var h = await StartAsync();
        var line = "x = " + call;
        await OpenAsync(h, Uri, "Sub T()\n" + line + "\nEnd Sub\n");

        var sig = await RequestAsync(h, "textDocument/signatureHelp", Pos(Uri, 1, line.Length));
        sig.GetProperty("activeParameter").GetInt32().Should().Be(expectedParameter);
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
    public async Task Rename_of_a_reserved_keyword_returns_RequestFailed()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Sub T()\n    Dim x\nEnd Sub\n");

        var rename = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 4 }, // the "Dim" keyword
            newName = "MyVar"
        });
        rename.ErrorCode.Should().Be(-32803);
        rename.Message.Should().Contain("keyword");
    }

    [Fact]
    public async Task Rename_refusal_reads_the_preceding_document_change()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, "Dim Value As Long\n");
        await h.NextDiagnosticsAsync();
        await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
        {
            textDocument = new { uri = Uri, version = 2 },
            contentChanges = new[] { new { text = "Dim Long As Long\n" } },
        });

        var refusal = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line = 0, character = 5 },
            newName = "Renamed",
        });
        refusal.ErrorCode.Should().Be(-32803);
        refusal.Message.Should().Contain("Long").And.Contain("keyword");
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

    // ── a whole file: the header is the IDE's (#273 task 3.10) ─────────────────────────────────
    // The language-server delta's requirement, on the wire: no answer changes or points inside a header or a
    // member's attribute lines. WholeFileAnswersTests holds the formatter and the diagnostics to it directly;
    // these hold the handlers, which are where rename and highlight make the decision.

    [Fact]
    public async Task Formatting_a_whole_class_never_edits_its_header_or_its_members_attribute_lines()
    {
        // The delta's "Formatting a class".
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.Class);

        var edits = await RequestAsync(h, "textDocument/formatting", new
        {
            textDocument = new { uri = Uri },
            options = new { tabSize = 4, insertSpaces = true },
        });

        var regions = VbProtectedRegions.Of(WholeFileFixtures.Class);
        edits.GetArrayLength().Should().BeGreaterThan(0, "the code under the header is deliberately unformatted");
        edits.EnumerateArray().Should().OnlyContain(e => !regions.Touches(RangeOf(e)));
        Apply(WholeFileFixtures.Class, edits).Should().Be(VbFormatter.Format(WholeFileFixtures.Class));
    }

    [Fact]
    public async Task Renaming_a_local_named_like_a_control_is_refused()
    {
        // The delta's "Renaming an identifier that also appears in the layout", literally: the local has the
        // control's own name, which the designer block declares on its Begin line. Lexically that local is
        // indistinguishable from a reference to the control, and renaming the control from its code, keeping
        // off the header, would leave the code naming a control that does not exist. So both are declined,
        // and nothing at all falls inside the designer block.
        var form = string.Concat(WholeFileFixtures.Lines(WholeFileFixtures.Form).Take(WholeFileFixtures.FormHeaderLines)
                       .Select(l => l + "\r\n")) +
                   "Private Sub Form_Load()\r\n" +      // 17
                   "    Dim cmdOK As String\r\n" +       // 18
                   "    cmdOK = \"x\"\r\n" +             // 19
                   "End Sub\r\n";
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, form);

        var edit = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line = 18, character = 9 },
            newName = "okText",
        });

        edit.ErrorCode.Should().Be(-32803);
        edit.Message.Should().Contain("Properties window");
    }

    [Fact]
    public async Task Renaming_a_control_from_its_code_is_refused()
    {
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.Form);

        var edit = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line = 21, character = 12 }, // "Caption = cmdOK.Caption"
            newName = "cmdAccept",
        });

        edit.ErrorCode.Should().Be(-32803);
        edit.Message.Should().Contain("Properties window");
    }

    [Fact]
    public async Task Renaming_an_identifier_that_also_appears_in_the_layout_edits_nothing_in_the_designer_block()
    {
        // The language-server delta's scenario. Caption is a property three times over in the fixture's designer
        // block, and the local renamed here is called Caption too.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.Form);

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 20, character = 5 }, // "dim Caption as string"
            newName = "title",
        });

        var starts = StartsOf(edit.GetProperty("changes").GetProperty(Uri));
        starts.Should().Contain((20, 4)).And.OnlyContain(s => s.Line >= WholeFileFixtures.FormHeaderLines);
    }

    [Fact]
    public async Task Renaming_a_member_that_has_a_description_takes_its_qualifier_along_and_nothing_else_on_that_line()
    {
        // The one exception to the rule, and the design record's: an attribute line names the member it
        // describes, so a rename that left it behind would describe a member that no longer exists. Only the
        // qualifier itself moves -- the attribute's name and value on the same line are not touched.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.Class);

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 15, character = 21 }, // "public property get Total()"
            newName = "Amount",
        });

        var changes = edit.GetProperty("changes").GetProperty(Uri);
        StartsOf(changes).Should().Equal((15, 20), (16, 10), (17, 10), (18, 0));
        changes.EnumerateArray().Should().OnlyContain(e =>
            e.GetProperty("range").GetProperty("end").GetProperty("character").GetInt32()
            - e.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32() == "Total".Length);
    }

    [Fact]
    public async Task Renaming_a_member_leaves_alone_a_run_that_describes_another_member()
    {
        // Halt's run says Total, and is Halt's: VB6 gives a procedure attribute to the procedure above it whatever
        // its qualifier names. The IDE refuses a rename that writes into a run it does not rename (#273 task 4.4),
        // so renaming Total sends no edit to it -- and the orphan two lines under total's run, which is free text,
        // is renamed with the code.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.ClassWithOrphans);

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 28, character = 1 }, // "total = a + b"
            newName = "Sum",
        });

        StartsOf(edit.GetProperty("changes").GetProperty(Uri))
            .Should().Equal((25, 16), (27, 10), (28, 0), (29, 10));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1Total")]
    [InlineData("Grand Total")]
    [InlineData("Grand.Total")]
    [InlineData("Total$")]
    [InlineData("_Total")]
    [InlineData("_")]
    [InlineData("[Total]")]
    [InlineData("Grand-Total")]
    [InlineData("Rem")]
    [InlineData("empty")]
    [InlineData("New")]
    [InlineData("Nothing")]
    [InlineData("Me")]
    [InlineData("attribute")]
    public async Task Renaming_to_a_name_VB6_does_not_accept_is_refused(string newName)
    {
        // The IDE refuses these before it asks (#273 task 4.4); other clients receive a refusal,
        // rather than with edits writing a name that would not compile. A leading or lone underscore and a
        // bracketed name were measured illegal against vb6.exe, and so were the six reserved words, each with a
        // name's shape: Rem wherever it was tried, Empty as a variable, New and Nothing as a Sub, Me as an array,
        // Attribute as a parameter.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var rename = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 6 },
            newName,
        });

        rename.ErrorCode.Should().Be(-32803);
        rename.Message.Should().Contain("not a VB6 name");
    }

    [Theory]
    [InlineData("Grand_Total")]
    [InlineData("Total_")]
    [InlineData("Total2")]
    [InlineData("Größe")]
    [InlineData("RemX")]
    [InlineData("Rem1")]
    public async Task Rename_to_a_name_VB6_may_accept_is_answered(string newName)
    {
        // The positive control: an underscore inside or at the end of a name, a digit after its first letter, and
        // RemX and Rem1 were measured legal; a letter outside ASCII was not measured, and VB6 may accept one in its
        // code page, so it is not refused.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, Doc);

        var edit = await RequestAsync(h, "textDocument/rename", new
        {
            textDocument = new { uri = Uri },
            position = new { line = 1, character = 6 },
            newName,
        });

        edit.GetProperty("changes").GetProperty(Uri).GetArrayLength().Should().Be(2);
    }

    [Theory]
    [InlineData("form", 6, 8)]   // "      Caption         =   \"OK\"" in the designer block
    [InlineData("form", 12, 12)] // "Attribute VB_Name = \"frmOrders\"" -- the attribute's own name
    [InlineData("class", 16, 12)] // "Attribute Total.VB_Description" -- the qualifier itself
    public async Task Rename_in_a_protected_line_is_refused_but_highlight_answers_null(string file, int line, int character)
    {
        // Nothing there is the developer's to rename, including a member's name in its own attribute line:
        // the rename starts from the member, and the qualifier follows it.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, file == "form" ? WholeFileFixtures.Form : WholeFileFixtures.Class);

        var rename = await RefusedRenameAsync(h, new
        {
            textDocument = new { uri = Uri },
            position = new { line, character },
            newName = "Renamed",
        });
        var highlight = await RequestAsync(h, "textDocument/documentHighlight", Pos(Uri, line, character));

        rename.ErrorCode.Should().Be(-32803);
        rename.Message.Should().Contain("header");
        highlight.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Highlight_leaves_out_occurrences_in_a_header_and_in_members_attribute_lines()
    {
        // Stricter than rename: highlighting the qualifier would suggest it can be edited there, and it
        // cannot.
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, WholeFileFixtures.Class);

        var hl = await RequestAsync(h, "textDocument/documentHighlight", Pos(Uri, 15, 21));

        StartsOf(hl).Should().Equal((15, 20), (18, 0));
    }

    [Fact]
    public async Task Opening_a_form_reports_nothing_in_its_header()
    {
        // The language-server delta's scenario: a form's whole file, designer block and all, as VB6 writes it,
        // including the comment VB6 puts after an enumerated value, which once failed both grammars on most forms
        // (task 0.2). A header like this raises nothing in either layer that keeps it out (the grammar reads it,
        // and the server withholds anything on a protected line), so the body is damaged: a publish with no
        // diagnostics in it cannot pass for one that left the header out.
        var form = WholeFileFixtures.Form
            .Replace("   ClientHeight    =   3000\r\n",
                "   BorderStyle     =   1  'Fixed Single\r\n   ClientHeight    =   3000\r\n", StringComparison.Ordinal)
            .Replace("dim Caption as string", "dim Caption as", StringComparison.Ordinal);
        const int headerLines = WholeFileFixtures.FormHeaderLines + 1;
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, form);

        var lines = (await h.NextDiagnosticsAsync()).GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()).ToList();

        lines.Should().Contain(21).And.OnlyContain(l => l >= headerLines);
    }

    [Fact]
    public async Task Opening_a_form_whose_designer_block_cannot_be_parsed_publishes_the_codes_diagnostics_and_none_in_the_header()
    {
        // The language-server delta's scenario. The body is damaged too, so an empty publish cannot pass for a
        // filtered one.
        var form = WholeFileFixtures.Form
            .Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal)
            .Replace("dim Caption as string", "dim Caption as", StringComparison.Ordinal);
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, form);

        var lines = (await h.NextDiagnosticsAsync()).GetProperty("diagnostics").EnumerateArray()
            .Select(d => d.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32()).ToList();

        lines.Should().Contain(20).And.OnlyContain(l => l >= WholeFileFixtures.FormHeaderLines);
    }

    [Fact]
    public async Task A_damaged_header_leaves_the_codes_procedures_in_the_outline()
    {
        // The grammar's recovery from this damage consumes the rest of the file, which took the procedure out
        // of the outline as well as the code's errors out of the list.
        var form = WholeFileFixtures.Form
            .Replace("ClientHeight    =   3000", "ClientHeight    =   = 3000", StringComparison.Ordinal);
        await using var h = await StartAsync();
        await OpenAsync(h, Uri, form);
        await h.NextDiagnosticsAsync();

        var symbols = await RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));

        symbols.EnumerateArray().Select(s => s.GetProperty("name").GetString()).Should().Contain("cmdOK_Click");
    }

    [Theory]
    [InlineData("Sub Work()\nEnd Sub\n", false)]
    [InlineData("Sub Work()\n    @@\nEnd Sub\n", true)]
    public async Task Debug_log_reports_queue_parse_diagnostics_and_completion_times(string source, bool fallback)
    {
        var previous = Serilog.Log.Logger;
        var sink = new TimingSink();
        using var logger = new Serilog.LoggerConfiguration().MinimumLevel.Debug().Enrich.FromLogContext()
            .WriteTo.Sink(sink).CreateLogger();
        Serilog.Log.Logger = logger;
        try
        {
            await using var h = await StartAsync();
            await OpenAsync(h, Uri, Doc);
            await h.NextDiagnosticsAsync();
            sink.Events.Clear();
            await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
            {
                textDocument = new { uri = Uri, version = 2 },
                contentChanges = new[] { new { text = source } },
            });
            await h.NextDiagnosticsAsync();
            var completion = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
            completion.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString())
                .Should().Contain("Work");
            await RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));
            var events = sink.Events.ToArray();
            foreach (var method in new[] { "textDocument/didChange", "textDocument/completion" })
            {
                events.Should().Contain(e => e.MessageTemplate.Text == "{Method} queue wait {QueueMs} ms"
                    && e.Properties["Method"].ToString() == $"\"{method}\"" && e.Properties.ContainsKey("QueueMs"));
                events.Should().Contain(e => e.MessageTemplate.Text == "{Method} elapsed {ElapsedMs} ms"
                    && e.Properties["Method"].ToString() == $"\"{method}\"" && e.Properties.ContainsKey("ElapsedMs"));
            }
            var completionTiming = events.Single(e => e.MessageTemplate.Text == "{Method} elapsed {ElapsedMs} ms"
                && e.Properties["Method"].ToString() == "\"textDocument/completion\"");
            completionTiming.Properties["RequestId"].Should().BeOfType<Serilog.Events.ScalarValue>()
                .Which.Value.Should().BeOfType<int>();
            var stages = events.Where(e => e.MessageTemplate.Text == "Parse {Stage} elapsed {ElapsedMs} ms").ToList();
            stages.Select(e => e.Properties["Stage"].ToString())
                .Should().Equal(fallback ? new[] { "\"SLL\"", "\"LL\"" } : new[] { "\"SLL\"" });
            stages.Should().OnlyContain(e => e.Properties.ContainsKey("ElapsedMs")
                && e.Properties["Uri"].ToString() == $"\"{Uri}\""
                && e.Properties["Method"].ToString() == "\"textDocument/didChange\""
                && e.Properties["DocumentVersion"].ToString() == "2");
            events.Should().Contain(e => e.MessageTemplate.Text == "Parse worker queue wait {QueueMs} ms");
            events.Should().Contain(e => e.MessageTemplate.Text == "Diagnostics {Uri} elapsed {ElapsedMs} ms");
        }
        finally { Serilog.Log.Logger = previous; }
    }

    private sealed class TimingSink : Serilog.Core.ILogEventSink
    {
        public System.Collections.Concurrent.ConcurrentQueue<Serilog.Events.LogEvent> Events { get; } = new();
        public void Emit(Serilog.Events.LogEvent logEvent) => Events.Enqueue(logEvent);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Completion_answers_during_analysis_from_the_last_snapshot(bool firstOpen)
    {
        var store = new SlowDocumentStore();
        await using var h = await StartAsync(store);
        if (!firstOpen)
        {
            await OpenAsync(h, Uri, Doc);
            await h.NextDiagnosticsAsync();
        }

        store.Hold = true;
        if (firstOpen)
            await OpenAsync(h, Uri, "Dim NewCounter As Long\n");
        else
            await h.Rpc.NotifyWithParameterObjectAsync("textDocument/didChange", new
            {
                textDocument = new { uri = Uri, version = 2 },
                contentChanges = new[] { new { text = "Dim NewCounter As Long\n" } },
            });
        await store.Entered.Task.WaitAsync(Timeout, TestContext.Current.CancellationToken);
        var symbols = RequestAsync(h, "textDocument/documentSymbol", DocParams(Uri));
        try
        {
            var list = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
            var labels = list.GetProperty("items").EnumerateArray()
                .Select(i => i.GetProperty("label").GetString()).ToList();
            if (firstOpen)
                labels.Should().Contain("Dim").And.NotContain("NewCounter");
            else
                labels.Should().Contain("Counter").And.NotContain("NewCounter");
            symbols.IsCompleted.Should().BeFalse("other requests still wait for the current analysis");
        }
        finally { store.Release.TrySetResult(); }

        await h.NextDiagnosticsAsync();
        await symbols;
        var refreshed = await RequestAsync(h, "textDocument/completion", Pos(Uri, 0, 0));
        refreshed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("label").GetString())
            .Should().Contain("NewCounter").And.NotContain("Counter");
    }

    private sealed class SlowDocumentStore : DocumentStore
    {
        public bool Hold { get; set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<(List<LspDiagnostic> Diagnostics, VisualBasic6Parser.StartRuleContext? Tree)?>
            AnalyzeAsync(string source, VbCompilationContext? compilation)
        {
            if (Hold)
            {
                Entered.TrySetResult();
                await Release.Task;
            }
            return await base.AnalyzeAsync(source, compilation);
        }
    }

    // ── harness ─────────────────────────────────────────────────────────────────────────────────
    private static object DocParams(string uri) => new { textDocument = new { uri } };

    private static LspRange RangeOf(JsonElement edit)
    {
        var range = edit.GetProperty("range");
        return new LspRange(PositionOf(range.GetProperty("start")), PositionOf(range.GetProperty("end")));

        static LspPosition PositionOf(JsonElement p) =>
            new(p.GetProperty("line").GetInt32(), p.GetProperty("character").GetInt32());
    }

    /// <summary>Where each answer in an array of edits or highlights starts, in the order given.</summary>
    private static List<(int Line, int Character)> StartsOf(JsonElement answers) =>
        answers.EnumerateArray().Select(RangeOf).Select(r => (r.Start.Line, r.Start.Character)).ToList();

    /// <summary>Applies a formatting answer the way a client does: from the last edit to the first.</summary>
    private static string Apply(string text, JsonElement edits)
    {
        var lineStarts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
                lineStarts.Add(i + 1);
        }

        var result = new System.Text.StringBuilder(text);
        foreach (var edit in edits.EnumerateArray().Select(e => (Range: RangeOf(e), Text: e.GetProperty("newText").GetString()!))
                     .OrderByDescending(e => e.Range.Start.Line).ThenByDescending(e => e.Range.Start.Character))
        {
            var start = lineStarts[edit.Range.Start.Line] + edit.Range.Start.Character;
            var end = lineStarts[edit.Range.End.Line] + edit.Range.End.Character;
            result.Remove(start, end - start).Insert(start, edit.Text);
        }
        return result.ToString();
    }
    private static object Pos(string uri, int line, int character) =>
        new { textDocument = new { uri }, position = new { line, character } };

    private static async Task<Harness> StartAsync(DocumentStore? store = null)
    {
        var h = new Harness(store);
        await h.Rpc.InvokeWithParameterObjectAsync<JsonElement>("initialize",
            new { processId = (int?)null, rootUri = (string?)null, capabilities = new { } }).WaitAsync(Timeout);
        await h.Rpc.NotifyWithParameterObjectAsync("initialized", new { });
        return h;
    }

    private static Task OpenAsync(Harness h, string uri, string text) =>
        h.Rpc.NotifyWithParameterObjectAsync("textDocument/didOpen",
            new { textDocument = new { uri, languageId = "vb6", version = 1, text } });

    private static async Task<RemoteInvocationException> RefusedRenameAsync(Harness h, object parameters)
    {
        var call = () => RequestAsync(h, "textDocument/rename", parameters);
        return (await call.Should().ThrowAsync<RemoteInvocationException>()).Which;
    }

    private static Task<JsonElement> RequestAsync(Harness h, string method, object @params) =>
        h.Rpc.InvokeWithParameterObjectAsync<JsonElement>(method, @params).WaitAsync(Timeout);

    [Fact]
    public async Task Harness_can_be_disposed_immediately_after_construction()
    {
        await using var harness = new Harness();
    }

    private sealed class Harness : IAsyncDisposable
    {
        public JsonRpc Rpc { get; }
        private readonly DiagTarget _diag = new();
        private readonly LanguageServer _server;
        private readonly Task _loop;

        public Harness(DocumentStore? store = null)
        {
            var c2s = new Pipe();
            var s2c = new Pipe();
            _server = LspServerHost.Create(c2s.Reader.AsStream(), s2c.Writer.AsStream(), store ?? new DocumentStore());
            _loop = _server.Run();

            var handler = new HeaderDelimitedMessageHandler(
                c2s.Writer.AsStream(), s2c.Reader.AsStream(), new SystemTextJsonFormatter());
            Rpc = new JsonRpc(handler);
            Rpc.AddLocalRpcTarget(_diag);
            Rpc.StartListening();
        }

        public Task<JsonElement> NextDiagnosticsAsync() =>
            _diag.Channel.Reader.ReadAsync().AsTask().WaitAsync(Timeout);

        public ValueTask DisposeAsync() => ServerHarnessTeardown.StopAsync(_server, _loop, Rpc, Timeout);

        private sealed class DiagTarget
        {
            public Channel<JsonElement> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();

            [JsonRpcMethod("textDocument/publishDiagnostics", UseSingleObjectParameterDeserialization = true)]
            public void Publish(JsonElement diagnostics) => Channel.Writer.TryWrite(diagnostics);
        }
    }
}

[CollectionDefinition("Server timing", DisableParallelization = true)]
public class ServerTimingCollection;
