using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class FileLockGrammarTests
{
    // Compiles on vb6.exe SP6 as native and p-code: #2553.
    [Theory]
    [InlineData("Lock")]
    [InlineData("Unlock")]
    public void Omitted_range_start_parses(string statement)
    {
        VbDiagnosticsProvider.GetDiagnostics($"Sub Main()\n{statement} #1, To 4\nEnd Sub\n")
            .Should().BeEmpty();
    }
}
