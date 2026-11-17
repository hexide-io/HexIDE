using Antlr4.Runtime.Tree;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// VBA7 acceptance rows K/D/A15 recorded in #4365's supervisor comment (measured for #4302).
public class Vba7SyntaxTests
{
    public static TheoryData<string, string> AcceptedRows => new()
    {
        { "K1", "Sub Main()\nx = 5^\nEnd Sub" },
        { "K2", "Sub Main()\nx = &HFF^\nEnd Sub" },
        { "K3", "Sub Main()\nx = &H7FFFFFFFFFFFFFFF^\nEnd Sub" },
        { "K4", "Sub Main()\nx = &HFFFFFFFFFFFFFFFF^\nEnd Sub" },
        { "K5", "Sub Main()\nx = &H8000000000000000^\nEnd Sub" },
        { "K6", "Sub Main()\nx = &HFFFFFFFF^\nEnd Sub" },
        { "K8", "Dim x^" },
        { "D1", "DefLngLng A-Z" },
        { "D2", "DefLngPtr A-Z" },
        { "A15", "Sub Main()\nx = CLngLng(5) ^ 2\nEnd Sub" },
        { "PtrSafe", "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As LongPtr" },
        { "DimLongLong", "Dim x As LongLong" },
        { "DimLongPtr", "Dim x As LongPtr" },
        { "ParameterLongLong", "Sub Foo(x As LongLong)\nEnd Sub" },
        { "ParameterLongPtr", "Sub Foo(x As LongPtr)\nEnd Sub" },
        { "ResultLongLong", "Function Foo() As LongLong\nEnd Function" },
        { "ResultLongPtr", "Function Foo() As LongPtr\nEnd Function" },
        { "ConstLongLong", "Const x As LongLong = 5" },
        { "ConstLongPtr", "Const x As LongPtr = 5" },
        { "FieldLongLong", "Type Record\nx As LongLong\nEnd Type" },
        { "FieldLongPtr", "Type Record\nx As LongPtr\nEnd Type" }
    };

    [Theory]
    [MemberData(nameof(AcceptedRows))]
    public void AcceptedVba7RowHasNoDiagnostics(string row, string source)
    {
        VbDiagnosticsProvider.GetDiagnostics(source + "\n").Should().BeEmpty(row);
    }

    [Theory]
    [InlineData("DefLngLng A-Z")]
    [InlineData("DefLngPtr A-Z")]
    public void DefTypeProducesADeclarationRatherThanAProcedureCall(string source)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(source + "\n");
        diagnostics.Should().BeEmpty();
        Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_deftypeStmt).Should().ContainSingle();
    }

    [Fact]
    public void CompleteVba7ModuleHasNoDiagnostics()
    {
        const string source = """
            DefLngLng A-Z
            DefLngPtr A-Z
            Private Declare PtrSafe Function GetTickCount Lib "kernel32" () As LongPtr
            Dim ll As LongLong
            Dim lp As LongPtr
            Const cll As LongLong = 5^
            Const clp As LongPtr = 5
            Type Record
                ll As LongLong
                lp As LongPtr
            End Type
            Function Foo(ll As LongLong, lp As LongPtr) As LongLong
            End Function
            Function Bar(ll As LongLong, lp As LongPtr) As LongPtr
            End Function
            Sub Main()
                Dim x^
                x = 5^
                x = &HFF^
                x = &H7FFFFFFFFFFFFFFF^
                x = &HFFFFFFFFFFFFFFFF^
                x = &H8000000000000000^
                x = &HFFFFFFFF^
                x = CLngLng(5) ^ 2
            End Sub
            """;
        VbDiagnosticsProvider.GetDiagnostics(source + "\n").Should().BeEmpty();
    }

    [Theory]
    [InlineData("2 ^ 3")]
    [InlineData("x ^ 2")]
    [InlineData("(a) ^ 2")]
    [InlineData("2^3")]
    [InlineData("x^2")]
    public void ExistingExponentiationHasNoDiagnostics(string expression)
    {
        var (diagnostics, tree) = VbDiagnosticsProvider.GetDiagnosticsAndTree(
            $"Sub Main()\nx = {expression}\nEnd Sub\n");
        diagnostics.Should().BeEmpty();
        Trees.FindAllRuleNodes(tree!, VisualBasic6Parser.RULE_valueStmt)
            .OfType<VisualBasic6Parser.VsPowContext>().Should().ContainSingle();
    }

    [Theory]
    [InlineData("DefLngLng")]
    [InlineData("DefLngPtr")]
    public void ExistingIdentifierSpellingStillParses(string name)
    {
        VbDiagnosticsProvider.GetDiagnostics($"Dim {name} As Long\n")
            .Should().BeEmpty();
    }

    [Fact]
    public void DefLngLngWithoutRangeIsStillASyntaxError()
    {
        VbDiagnosticsProvider.GetDiagnostics("DefLngLng\n").Should().NotBeEmpty();
    }
}
