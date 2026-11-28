using HexIDE.Lsp.Messages;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>Declaration-list contracts from hexide-io/HexIDE#4401, matching the bundled server.</summary>
public class VbFamilyDeclarationCollectorTests
{
    [Theory]
    [InlineData("Public Sub Bump()\nEnd Sub", "Bump", SymbolKind.Method)]
    [InlineData("Private Static Function Twice&()\nEnd Function", "Twice", SymbolKind.Function)]
    [InlineData("Friend Property Get Value() As Long\nEnd Property", "Value (Get)", SymbolKind.Property)]
    [InlineData("Public Property Let Value(ByVal n As Long)\nEnd Property", "Value (Let)", SymbolKind.Property)]
    [InlineData("Private Property Set Value(ByVal n As Object)\nEnd Property", "Value (Set)", SymbolKind.Property)]
    [InlineData("Global Enum Choices\nOne\nEnd Enum", "Choices", SymbolKind.Enum)]
    [InlineData("Private Type Record\nvalue As Long\nEnd Type", "Record", SymbolKind.Struct)]
    [InlineData("Private Function [Value]() As Long\nEnd Function", "[Value]", SymbolKind.Function)]
    [InlineData("Static Sub Bump()\nEnd Sub", "Bump", SymbolKind.Method)]
    [InlineData("Public _\nFunction _\nTwice&( _\n) As Long\nEnd Function", "Twice", SymbolKind.Function)]
    [InlineData("' Sub Fake()\nRem Function Fake()\n#If VBA7 Then\nPublic Sub Real() ' comment\nEnd Sub\n#End If", "Real", SymbolKind.Method)]
    [InlineData("Declare Function Fake Lib \"x\" () As Long\nPublic Event Fake()\nSub Real()\nExit Sub\nEnd Sub", "Real", SymbolKind.Method)]
    public void Collects_only_declarations(string text, string name, SymbolKind kind)
    {
        var symbol = VbFamilyDeclarationCollector.Collect(text).Should().ContainSingle().Which;
        symbol.Name.Should().Be(name);
        symbol.Kind.Should().Be(kind);
        symbol.Range.End.Line.Should().Be(text.ReplaceLineEndings("\n").Count(c => c == '\n') - (text.EndsWith("#End If") ? 1 : 0));
    }

    [Fact]
    public void Keeps_source_positions_and_both_conditional_branches()
    {
        var symbols = VbFamilyDeclarationCollector.Collect("#If Win64 Then\n  Sub Wide()\n  End Sub\n#Else\nSub Narrow()\nEnd Sub\n#End If");
        symbols.Select(s => s.Name).Should().Equal("Wide", "Narrow");
        symbols[0].SelectionRange.Start.Should().Be(new Position(1, 6));
        symbols[0].Range.End.Line.Should().Be(2);
    }
}
