using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

public class CollectionEnumeratorGrammarTests
{
    [Fact]
    public void The_measured_hidden_Collection_member_parses()
    {
        // VB6 SP6 native/p-code: https://github.com/hexide-io/HexIDE/issues/4679.
        const string source = "Sub Main()\nDim c As New Collection, v As Variant\nSet v = c.[_NewEnum]\nEnd Sub\n";
        VbDiagnosticsProvider.GetDiagnostics(source).Should().BeEmpty();
    }
}
