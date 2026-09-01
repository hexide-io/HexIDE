using HexIDE.IDE;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// What reaches the dialog layer when VB6 calls <c>MsgBox</c> / <c>InputBox</c> (issue #131).
///
/// <para><c>MsgBox</c> never read <c>args[2]</c> — it passed a hardcoded <c>""</c> — so every message box
/// came out captionless however it was called. It surfaced through the MCP tools, which reported an open
/// dialog as <c>"activeDialog": ""</c>; the tools were right and the caption really was empty.</para>
///
/// <para>The distinction these tests exist to protect is <b>null vs empty</b>. VB6 substitutes the
/// application name only when the Title argument is <i>omitted</i>; an explicitly empty one stays empty.
/// A <c>?? ""</c> anywhere on this path collapses the two and makes <c>MsgBox "x", 0, ""</c> sprout a
/// caption the author deliberately suppressed. The omitted-case default belongs to <c>App.Title</c>, which
/// this runtime does not have yet (#136) — so the interpreter's job is only to report which case it was.</para>
/// </summary>
public class MsgBoxTitleTests
{
    private sealed class RecordingStdLib : IBasicStandardLibrary
    {
        public string? LastCaption { get; private set; }
        public bool Called { get; private set; }

        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            LastCaption = caption;
            Called = true;
            return Task.FromResult(MessageBoxResult.Ok);
        }

        public Task<string?> InputBox(string prompt, string? title, string defaultText)
        {
            LastCaption = title;
            Called = true;
            return Task.FromResult<string?>("");
        }

        public void DebugPrint(Vb6Value value) { }
    }

    private static async Task<RecordingStdLib> Run(string code)
    {
        var stdLib = new RecordingStdLib();
        var vb = new BasicInterpreter(stdLib, new ModuleExecutionContext(), new ExecutionEnvironment(), code);
        await vb.Execute();
        stdLib.Called.Should().BeTrue("the probe must actually reach the dialog layer");
        return stdLib;
    }

    [Fact]
    public async Task MsgBox_passes_the_title_it_was_given()
    {
        var lib = await Run("""
            MsgBox "hello", vbExclamation, "Oracle Check"
            """);

        lib.LastCaption.Should().Be("Oracle Check");
    }

    [Fact]
    public async Task MsgBox_reports_an_omitted_title_as_null()
    {
        // null is the signal "the caller omitted it" — the only thing that lets the dialog layer apply
        // VB6's application-name default. Returning "" here would make the default unreachable.
        var lib = await Run("""
            MsgBox "hello"
            """);

        lib.LastCaption.Should().BeNull();
    }

    [Fact]
    public async Task MsgBox_keeps_an_explicitly_empty_title_empty()
    {
        var lib = await Run("""
            MsgBox "hello", vbOKOnly, ""
            """);

        lib.LastCaption.Should().NotBeNull("an empty title was supplied, not omitted");
        lib.LastCaption.Should().BeEmpty();
    }

    [Fact(Skip = "Blocked on #135 — an omitted positional argument is dropped, shifting the rest left.")]
    public async Task MsgBox_still_passes_the_title_when_buttons_are_omitted_by_position()
    {
        // `MsgBox "x", , "T"` — the middle argument left blank, which VB6 allows freely. The title is
        // still the third position, and reading it by index has to survive that.
        //
        // Kept rather than deleted, and asserting the RIGHT answer rather than today's: the interpreter
        // drops the blank and shifts everything after it one place left, so "Positional" arrives as the
        // Buttons argument. Proven with `MsgBox "hello", , vbCritical`, which sets the icon — a constant
        // written in the Title position being read as Buttons. That is a general argument-binding defect
        // rather than anything to do with MsgBox, so it is #135; inverting this test to match would
        // enshrine the bug, and deleting it would lose the case.
        var lib = await Run("""
            MsgBox "hello", , "Positional"
            """);

        lib.LastCaption.Should().Be("Positional");
    }

    [Fact]
    public async Task InputBox_passes_the_title_it_was_given()
    {
        var lib = await Run("""
            Dim s As String
            s = InputBox("prompt", "Ask Me")
            """);

        lib.LastCaption.Should().Be("Ask Me");
    }

    [Fact]
    public async Task InputBox_reports_an_omitted_title_as_null()
    {
        // InputBox always read its Title, but an omitted one arrived as "" and so could never take the
        // application-name default either.
        var lib = await Run("""
            Dim s As String
            s = InputBox("prompt")
            """);

        lib.LastCaption.Should().BeNull();
    }
}
