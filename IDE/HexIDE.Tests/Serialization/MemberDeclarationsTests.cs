using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// <see cref="ReadOnlyRegions.MemberDeclarations"/>: what a text declares that a run of attribute lines could
/// describe, described or not, with a property's accessor (hexide-io/HexIDE#273 task 4.5b).
/// </summary>
public class MemberDeclarationsTests
{
    private static IEnumerable<(string Name, DeclarationKind Kind, PropertyAccessor? Accessor)> Of(string code) =>
        ReadOnlyRegions.MemberDeclarations(code, 0).Select(d => (d.Name.Name, d.Name.Kind, d.Accessor));

    [Fact]
    public void EachAccessorOfAPropertyIsReportedWithItsName()
    {
        const string code =
            "Public Property Get Value() As Long\r\n" +
            "End Property\r\n" +
            "Public Property Let Value(ByVal v As Long)\r\n" +
            "End Property\r\n" +
            "Friend Property Set Shape(ByVal s As Object)\r\n" +
            "End Property\r\n";

        Of(code).Should().Equal(
            ("Value", DeclarationKind.Procedure, PropertyAccessor.Get),
            ("Value", DeclarationKind.Procedure, (PropertyAccessor?)PropertyAccessor.Let),
            ("Shape", DeclarationKind.Procedure, (PropertyAccessor?)PropertyAccessor.Set));
    }

    [Fact]
    public void EveryOtherProcedureHasNoAccessor()
    {
        const string code =
            "Public Event Changed()\r\n" +
            "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long\r\n" +
            "Sub Main()\r\n" +
            "End Sub\r\n" +
            "Private Function Total() As Currency\r\n" +
            "End Function\r\n";

        Of(code).Should().Equal(
            ("Changed", DeclarationKind.Procedure, (PropertyAccessor?)null),
            ("GetTickCount", DeclarationKind.Procedure, (PropertyAccessor?)null),
            ("Main", DeclarationKind.Procedure, (PropertyAccessor?)null),
            ("Total", DeclarationKind.Procedure, (PropertyAccessor?)null));
    }

    [Fact]
    public void OnlyTheDeclarationsSectionsVariablesAndConstantsAreMembers()
    {
        // A procedure's own Dim, Static and Const describe nothing a run can attach to, nor does a variable declared
        // on the line that opens the first body, as the run reader reads that line.
        const string code =
            "Public A As Long, B As Long: Private Const C = 1\r\n" +
            "Dim D As Long: Public Sub Go()\r\n" +
            "    Dim Local As Long\r\n" +
            "    Static Kept As Long\r\n" +
            "End Sub\r\n" +
            "Public Late As Long\r\n";

        Of(code).Select(d => d.Name).Should().Equal("A", "B", "C", "Go");
    }

    [Fact]
    public void AContinuedDeclarationIsReadAsOneLine()
    {
        const string code =
            "Public Property Get _\r\n" +
            "    Value() As Long\r\n" +
            "End Property\r\n";

        Of(code).Should().Equal(("Value", DeclarationKind.Procedure, (PropertyAccessor?)PropertyAccessor.Get));
    }

    [Fact]
    public void TheHeaderDeclaresNothing()
    {
        const string header = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Order\"\r\n";

        Of(header + "Public Total As Long\r\n").Select(d => d.Name).Should().Equal("Total");
        ReadOnlyRegions.MemberDeclarations(header + "Public Total As Long\r\n", header.Length).Single().Name.Offset
            .Should().Be(header.Length + "Public ".Length, "a declaration's offset is the one the run reader gives its member");
    }
}
