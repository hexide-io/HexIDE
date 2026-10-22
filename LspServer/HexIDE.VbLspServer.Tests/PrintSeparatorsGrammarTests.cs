using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #1409: measured in the oracle's "Sequential files", under "Separators with no item". Print's output list may be
// separators alone, where an ordinary call's arguments may not. Mirrored on the interpreter's parser.
public class PrintSeparatorsGrammarTests
{
    [Theory]
    [InlineData("Debug.Print ,")]
    [InlineData("Debug.Print ;")]
    [InlineData("Debug.Print , ,")]
    [InlineData("Debug.Print ;;")]
    [InlineData("Debug.Print ,;")]
    [InlineData("Debug.Print ;,")]
    [InlineData("Debug.Print ; \"t\";")]
    [InlineData("Debug.Print , \"lead\"")]
    public void Print_takes_separators_alone(string statement)
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Main()\n    " + statement + "\nEnd Sub\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("Foo ,")]
    [InlineData("Foo , ,")]
    [InlineData("Foo ;")]
    public void Another_call_does_not(string statement)
    {
        // vb6.exe's "Syntax error", native and p-code, where `Foo , 2` builds.
        VbDiagnosticsProvider.GetDiagnostics("Sub Foo(Optional a, Optional b, Optional c)\nEnd Sub\nSub Main()\n    " + statement + "\nEnd Sub\n")
            .Should().NotBeEmpty();
    }

    [Fact]
    public void A_call_with_an_argument_after_a_separator_still_parses()
    {
        VbDiagnosticsProvider.GetDiagnostics("Sub Foo(Optional a, Optional b, Optional c)\nEnd Sub\nSub Main()\n    Foo , 2\nEnd Sub\n")
            .Should().BeEmpty();
    }
}
