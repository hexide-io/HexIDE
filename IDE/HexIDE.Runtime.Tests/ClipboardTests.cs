using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ClipboardTests : BaseVBTestFixture
{
    private sealed class MemoryClipboard : ITextClipboard
    {
        public string? Text;
        public TaskCompletionSource? Gate;
        public Task Clear() { Text = null; return Task.CompletedTask; }
        public async Task<string?> GetText() { if (Gate is { } gate) await gate.Task; return Text; }
        public Task SetText(string text) { Text = text; return Task.CompletedTask; }
    }

    private sealed class Host(List<Vb6Value> output, ITextClipboard clipboard) : IBasicStandardLibrary
    {
        public ITextClipboard Clipboard => clipboard;
        public void DebugPrint(Vb6Value value) => output.Add(value);
        public Task<MessageBoxResult> MsgBox(string t, string? c, MessageBoxButtons b, MessageBoxIcon i) => throw new NotSupportedException();
        public Task<string?> InputBox(string p, string? t, string d) => throw new NotSupportedException();
    }

    private readonly MemoryClipboard clipboard = new();
    private BasicInterpreter Interpreter(string code) => new(new Host(debug, clipboard), context, rootEnv, code);

    [Fact]
    public async Task Text_round_trip_distinguishes_empty_text_from_no_text()
    {
        await Interpreter("""
            Option Explicit
            Debug.Print TypeName(Clipboard)
            Call Clipboard.Clear
            Debug.Print Clipboard.GetFormat(vbCFText)
            Debug.Print Clipboard.GetText
            With Clipboard
                Call Clipboard.SetText("hello")
                Debug.Print .GetText(vbCFText)
                Debug.Print .GetFormat(vbCFText)
            End With
            Clipboard.SetText ""
            Debug.Print Clipboard.GetFormat(vbCFText)
            Debug.Print Clipboard.GetText()
            Clipboard.Clear
            Debug.Print Clipboard.GetFormat(vbCFText)
            """).Execute();
        AssertDebugLog([new("Clipboard"), new(false), new(""), new("hello"), new(true), new(true), new(""), new(false)]);
    }

    [Theory]
    [InlineData("42", "42")]
    [InlineData("True", "True")]
    [InlineData("Empty", "")]
    // VB6 probe constructs these with ChrW; its source files cannot encode the literal in ANSI.
    [InlineData("\"ĀΩЖ€中\"", "AO?€?")]
    [InlineData("\"a\" & Chr$(0) & \"b\"", "a")]
    public async Task Text_is_coerced_and_truncated(string expression, string expected)
    {
        await Interpreter($"Clipboard.SetText {expression}\nDebug.Print Clipboard.GetText\n").Execute();
        clipboard.Text.Should().Be(expected);
        AssertDebugLog([new(expected)]);
    }

    [Theory]
    [InlineData("0", 460)]
    [InlineData("2", 460)]
    [InlineData("13", 460)]
    [InlineData("1.5", 460)]
    [InlineData("32768", 6)]
    [InlineData("\"bad\"", 13)]
    [InlineData("Null", 13)]
    [InlineData("Empty", 460)]
    public async Task Invalid_text_formats_raise_measured_errors_and_preserve_text(string format, int error)
    {
        clipboard.Text = "old";
        await Interpreter($"On Error Resume Next\nClipboard.SetText \"new\", {format}\nDebug.Print Err.Number\nErr.Clear\nDebug.Print Clipboard.GetText({format})\nDebug.Print Err.Number\n").Execute();
        clipboard.Text.Should().Be("old");
        AssertDebugLog([new((long)error), new((long)error)]);
    }

    [Theory]
    [InlineData("1.4")]
    [InlineData("\"1\"")]
    public async Task Format_uses_integer_coercion(string format)
    {
        await Interpreter($"Clipboard.SetText \"new\", {format}\nDebug.Print Clipboard.GetText({format})\nDebug.Print Clipboard.GetFormat({format})\n").Execute();
        AssertDebugLog([new("new"), new(true)]);
    }

    [Theory]
    [InlineData("Clipboard.SetText Null", 94)]
    [InlineData("Debug.Print Clipboard.GetFormat(Null)", 94)]
    [InlineData("Debug.Print Clipboard.GetFormat(32768)", 6)]
    [InlineData("Debug.Print Clipboard.GetFormat(\"bad\")", 13)]
    [InlineData("Clipboard.Clear 1", 450)]
    [InlineData("Clipboard.SetText", 450)]
    [InlineData("Clipboard.GetFormat", 450)]
    public async Task Invalid_arguments_are_trappable(string statement, int error)
    {
        await Interpreter($"On Error Resume Next\n{statement}\nDebug.Print Err.Number\n").Execute();
        AssertDebugLog([new((long)error)]);
        clipboard.Text.Should().BeNull();
    }

    [Theory]
    [InlineData("Clipboard.GetData(2)")]
    [InlineData("Clipboard.GetFormat(2)")]
    [InlineData("Clipboard.GetText(vbCFRTF)")]
    [InlineData("Clipboard.GetText(vbCFLink)")]
    public async Task Unsupported_formats_are_not_silently_skipped(string expression)
    {
        var run = () => Interpreter($"On Error Resume Next\nDebug.Print {expression}\nDebug.Print \"continued\"\n").Execute();
        await run.Should().ThrowAsync<NotImplementedException>();
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task A_variant_alias_reaches_the_same_clipboard()
    {
        await Interpreter("Dim c\nSet c = Clipboard\nc.SetText \"alias\"\nDebug.Print c.GetText\nDebug.Print Clipboard.GetText\n").Execute();
        AssertDebugLog([new("alias"), new("alias")]);
    }

    [Fact]
    public async Task Declared_names_shadow_the_global_and_its_spelling_is_case_insensitive()
    {
        await Interpreter("""
            Sub LocalName()
                Dim Clipboard As String
                Clipboard = "local"
                Debug.Print Clipboard
            End Sub
            LocalName
            cLiPbOaRd.SeTtExT "global"
            Debug.Print cLiPbOaRd.GeTtExT
            """).Execute();
        AssertDebugLog([new("local"), new("global")]);
    }

    [Fact]
    public async Task A_user_function_can_supply_the_object_for_a_member_read()
    {
        await RunClasses("""
            Function Clipboard() As Box
                Set Clipboard = New Box
            End Function
            Debug.Print Clipboard.GetText
            """, ("Box", "Public Function GetText() As String\nGetText = \"shadow\"\nEnd Function\n"));
        AssertDebugLog([new("shadow")]);
    }

    [Fact]
    public async Task A_clipboard_wait_queues_other_events_until_the_handler_finishes()
    {
        clipboard.Text = "ready";
        clipboard.Gate = new TaskCompletionSource();
        var pump = new EventPump();
        var vb = Interpreter("Sub ReadIt()\nDebug.Print Clipboard.GetText\nDebug.Print \"after\"\nEnd Sub\n");
        vb.Events = pump;
        await vb.Execute();
        var done = new TaskCompletionSource();
        pump.Raise("ReadIt", () => vb.ExecuteSub("ReadIt"));
        pump.Raise("Other", () => { debug.Add(new("other")); done.SetResult(); return Task.CompletedTask; });
        debug.Should().BeEmpty();
        clipboard.Gate.SetResult();
        await done.Task;
        AssertDebugLog([new("ready"), new("after"), new("other")]);
    }

    [Fact]
    public async Task An_external_clipboard_change_is_read_fresh()
    {
        var vb = Interpreter("Sub ReadIt()\nDebug.Print Clipboard.GetText\nEnd Sub\n");
        await vb.Execute();
        clipboard.Text = "external";
        await vb.ExecuteSub("ReadIt");
        clipboard.Text = "changed";
        await vb.ExecuteSub("ReadIt");
        AssertDebugLog([new("external"), new("changed")]);
    }

    [Fact]
    public async Task Awaiting_the_host_does_not_run_the_next_statement_early()
    {
        clipboard.Text = "ready";
        clipboard.Gate = new TaskCompletionSource();
        var run = Interpreter("Debug.Print Clipboard.GetText\nDebug.Print \"after\"\n").Execute();
        run.IsCompleted.Should().BeFalse();
        debug.Should().BeEmpty();
        clipboard.Gate.SetResult();
        await run;
        AssertDebugLog([new("ready"), new("after")]);
    }
}
