using HexIDE.Forms.Views;
using HexIDE.Runtime.Debugging;

namespace HexIDE.Tests.Debugging;

/// <summary>
/// Debugger v2·P6c — the Auto Data Tip show/suppress decision. Hovering an identifier while paused shows
/// "identifier = value", but ONLY for a genuinely resolved, in-scope value; a keyword, an out-of-scope name, or an
/// evaluation error shows NO tip (rather than an error string in the editor). The hover interaction itself is UI —
/// verified by the proven typed-eval path (WatchEvalTests + the live get_watches check) plus code review, since a
/// pointer hover can't be driven headlessly at a specific glyph reliably. This pins the decision logic.
/// </summary>
public class DataTipTests
{
    // Touching CodeEditorView (even a static method) runs its static ctor, which loads Avalonia assets — needs the
    // headless app initialised.
    public DataTipTests() => AvaloniaTestSetup.EnsureInitialized();

    private static DebugEvalResult Ok(string display)
        => new(true, display, "Integer", true, new DebugNode("x", display, "Integer"));

    private static DebugEvalResult Err(string message)
        => new(false, message, string.Empty, false, new DebugNode("x", message, string.Empty));

    [Fact]
    public void ResolvedValue_ShowsIdentifierEqualsValue()
        => CodeEditorView.DataTipText("x", Ok("42")).Should().Be("x = 42");

    [Fact]
    public void EvaluationError_ShowsNoTip()
        => CodeEditorView.DataTipText("Foo", Err("Sub or Function not defined")).Should().BeNull();

    [Fact]
    public void NoResult_ShowsNoTip()   // not paused / eval unavailable → null result
        => CodeEditorView.DataTipText("x", null).Should().BeNull();

    [Fact]
    public void EmptyWord_ShowsNoTip()   // pointer not over an identifier
        => CodeEditorView.DataTipText(string.Empty, Ok("42")).Should().BeNull();
}
