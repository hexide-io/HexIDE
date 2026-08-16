using HexIDE.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Covers the live Debug.Print sink (Immediate-window bridge). The ~400 interpreter tests exercise
// Debug.Print via MockStdLib's capture list; this pins VBDebugConsole itself — the piece the live IDE uses.
public class VBDebugConsoleTests
{
    [Fact]
    public void Emit_FormatsBoxedValue_AndRaisesOutput()
    {
        var captured = new List<string>();
        void Handler(string s) => captured.Add(s);

        VBDebugConsole.Output += Handler;
        try
        {
            VBDebugConsole.Emit(new Vb6Value("hello world"));
            VBDebugConsole.Emit(new Vb6Value(42));
            VBDebugConsole.Emit(new Vb6Value(true));
        }
        finally
        {
            VBDebugConsole.Output -= Handler;
        }

        captured.Should().Equal("hello world", "42", "True");
    }

    [Fact]
    public void Emit_WithNoSubscribers_DoesNotThrow()
    {
        var act = () => VBDebugConsole.Emit(new Vb6Value("nobody listening"));
        act.Should().NotThrow();
    }
}
