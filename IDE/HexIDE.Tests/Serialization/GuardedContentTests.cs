using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Replacing a document's content without changing its header (hexide-io/HexIDE#273 task 3.9: the add-in
/// <c>SetContent</c> and automation <c>set_file_content</c> rows of the writer policy).
/// </summary>
public class GuardedContentTests
{
    private const string Designer =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "End\r\n";

    private const string Attributes =
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n";

    private const string Form = Designer + Attributes + "Option Explicit\r\n";

    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Tide\"\r\n";

    [Fact]
    public void TheCodeAloneKeepsTheHeader()
    {
        var result = GuardedContent.Replace(Form, Designer.Length, "Sub Main()\r\nEnd Sub\r\n");

        result.NewText.Should().Be(Designer + Attributes + "Sub Main()\r\nEnd Sub\r\n");
        result.Refusal.Should().BeNull();
        result.HeaderKept.Should().BeTrue();
    }

    [Fact]
    public void AFormsCodeSectionWithItsAttributeLinesUnchangedIsAccepted()
    {
        // What a form's code section has always begun with, and what reading it back has always returned.
        var result = GuardedContent.Replace(Form, Designer.Length, Attributes + "Sub Main()\r\nEnd Sub\r\n");

        result.NewText.Should().Be(Designer + Attributes + "Sub Main()\r\nEnd Sub\r\n");
        result.HeaderKept.Should().BeTrue("the designer block was put back in front");
    }

    [Fact]
    public void TheWholeFileWithItsHeaderUnchangedIsAccepted()
    {
        var result = GuardedContent.Replace(Form, Designer.Length, Designer + Attributes + "Sub Main()\r\n");

        result.NewText.Should().Be(Designer + Attributes + "Sub Main()\r\n");
        result.HeaderKept.Should().BeFalse("the caller supplied all of it");
    }

    [Fact]
    public void AnAddInReplacesADocumentsContentWithADifferentHeaderAndIsRefused()
    {
        // The addin-system delta's scenario: the replacement is refused and the caller is told why.
        var renamed = Attributes.Replace("frmOrders", "frmOther");

        var result = GuardedContent.Replace(Form, Designer.Length, renamed + "Sub Main()\r\n");

        result.NewText.Should().BeNull();
        result.Refusal.Should().Contain("header");
    }

    [Fact]
    public void AChangedDesignerBlockIsRefusedToo()
    {
        var moved = Designer.Replace("Orders", "Invoices") + Attributes + "Option Explicit\r\n";

        GuardedContent.Replace(Form, Designer.Length, moved).NewText.Should().BeNull();
    }

    [Fact]
    public void LineEndingsAreNotAChangeAndTheDocumentsOwnHeaderIsWhatLands()
    {
        var lf = (Attributes + "Sub Main()\n").Replace("\r\n", "\n");

        GuardedContent.Replace(Form, Designer.Length, lf).NewText
            .Should().Be(Designer + Attributes + "Sub Main()\n");
    }

    [Fact]
    public void AClassModulesAttributeLinesAloneAreAcceptedAndItsClassHeaderKept()
    {
        var current = ClassHeader + "Option Explicit\r\n";

        var result = GuardedContent.Replace(current, ClassHeader.Length, "Attribute VB_Name = \"Tide\"\r\nPublic X As Long\r\n");

        result.NewText.Should().Be(ClassHeader + "Public X As Long\r\n");
    }

    [Fact]
    public void ADocumentWithNoHeaderTakesTheContentAsItIs()
    {
        var result = GuardedContent.Replace("Sub Main()\r\n", 0, "Sub Other()\r\n");

        result.NewText.Should().Be("Sub Other()\r\n");
        result.HeaderKept.Should().BeFalse("there was no header to keep");
    }
}
