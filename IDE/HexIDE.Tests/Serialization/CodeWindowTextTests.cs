using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// A document's text as its code window numbers it, for a caller with no code window open
/// (hexide-io/HexIDE#273 tasks 3.12 and 3.16).
/// </summary>
public class CodeWindowTextTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Lines 1-7 the designer block, 8-9 the attribute run: the header is 9 lines, and the code starts at 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Dim Caption As String\r\n" +
        "    Caption = Command1.Caption\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private static DocumentIdentity Form() =>
        DocumentIdentity.For(new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, NullSink.Instance)!);

    private static DocumentIdentity Class()
    {
        var module = TestHelpers.CreateModule(name: "Order", kind: ModuleKind.ClassModule);
        module.UpdateCode(ClassCode);
        return DocumentIdentity.For(module);
    }

    /// <summary>The 1-based line of <paramref name="text"/> that starts with <paramref name="start"/>.</summary>
    private static int LineOf(CodeWindowText text, string start) =>
        text.Text.Replace("\r\n", "\n").Split('\n').ToList().FindIndex(l => l.StartsWith(start, StringComparison.Ordinal)) + 1;

    [Fact]
    public void AFormIsItsWholeFile()
    {
        var text = CodeWindowText.Of(Form());

        text.Text.Should().Be(Frm, "the code window holds the file, and every line number counts in it");
        text.LineCount.Should().Be(17, "sixteen lines and the empty one after the last line break");
        text.HeaderLineCount.Should().Be(9);
    }

    [Fact]
    public void AClassCountsItsGeneratedHeader()
    {
        // The code-editor delta's scenario: the first line of code after a class's standard header is numbered
        // from the top of the file. Asserted against where the header actually ends rather than a constant, so
        // the test says what the rule is, not what today's header happens to be.
        var text = CodeWindowText.Of(Class());

        var firstCode = LineOf(text, "Option Explicit");
        firstCode.Should().BeGreaterThan(1, "a class has a header");
        text.HeaderLineCount.Should().Be(firstCode - 1);
        text.Text.Should().EndWith(ClassCode);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void EveryLineOfAFormsHeaderIsReadOnly(int line) =>
        CodeWindowText.Of(Form()).IsReadOnlyLine(line).Should().BeTrue();

    [Theory]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(16)]
    [InlineData(17)]
    public void AFormsCodeIsNot(int line) =>
        CodeWindowText.Of(Form()).IsReadOnlyLine(line).Should().BeFalse();

    [Fact]
    public void AMembersAttributeLineIsReadOnlyAndTheDeclarationItDescribesIsNot()
    {
        // The trap 3.11 left: the edit test also refuses the line break the attribute hangs from, which ends the
        // declaration. Asked about a line, it would put every described member out of reach of a breakpoint.
        var text = CodeWindowText.Of(Class());
        var declaration = LineOf(text, "Public Function Total");

        text.IsReadOnlyLine(declaration).Should().BeFalse();
        text.IsReadOnlyLine(declaration + 1).Should().BeTrue();
        text.IsReadOnlyLine(declaration + 2).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(99)]
    public void ALineTheTextDoesNotHaveIsNotReadOnly(int line) =>
        CodeWindowText.Of(Form()).IsReadOnlyLine(line).Should().BeFalse();

    [Fact]
    public void LinesAreCountedTheSameWhicheverLineEndingTheTextUses()
    {
        // build-ide runs on Linux: a count that asked the host what a line ending is would differ there.
        var crlf = new CodeWindowText("a\r\nb\r\nc", 0);
        var lf = new CodeWindowText("a\nb\nc", 0);

        crlf.LineCount.Should().Be(3);
        lf.LineCount.Should().Be(3);
    }
}
