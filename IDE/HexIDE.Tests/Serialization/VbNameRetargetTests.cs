using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Finding and rewriting the <c>Attribute VB_Name</c> line at the head of a code section
/// (hexide-io/HexIDE#273 task 3.5, hexide-io/HexIDE#473).
/// </summary>
/// <remarks>
/// Every expectation here is an exact string, never a <c>Contain</c>. The span this finds is handed straight
/// to a document replace, so a locator that is one character out per line still produces text that CONTAINS
/// the right attribute — and cuts a <c>\r</c> off, or glues two lines together, which is what #465 records a
/// neighbouring walker doing while its tests stayed green on exactly that kind of assertion.
/// </remarks>
public class VbNameRetargetTests
{
    private const string FiveAttributesCrlf =
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Attribute VB_Exposed = False\r\n" +
        "Option Explicit\r\n";

    [Fact]
    public void ARenameChangesTheNameAndNotOneOtherCharacter()
    {
        FormCodeText.RetargetVbName(FiveAttributesCrlf, "frmRenamed").Should().Be(
            "Attribute VB_Name = \"frmRenamed\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_PredeclaredId = True\r\n" +
            "Attribute VB_Exposed = False\r\n" +
            "Option Explicit\r\n");
    }

    [Fact]
    public void AndTheSameForLfText()
    {
        // An editor buffer carries whatever was typed into it, and on Linux the .frm reader rebuilds a code
        // section with the host's line ending, so both have to come out byte for byte.
        FormCodeText.RetargetVbName("Attribute VB_Name = \"Form1\"\nOption Explicit\n", "frmRenamed")
            .Should().Be("Attribute VB_Name = \"frmRenamed\"\nOption Explicit\n");
    }

    [Fact]
    public void ALineBelowOtherCrlfLinesIsFoundAtItsRealOffset()
    {
        // The case that separates a walker over real offsets from one over a normalised copy: every CRLF line
        // above the target costs a normalised count one character, so this is where it would cut in.
        const string code =
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_Name = \"Form1\"\r\n" +
            "Option Explicit\r\n";

        FormCodeText.VbNameLine(code).Should().Be((code.IndexOf("Attribute VB_Name", StringComparison.Ordinal),
            "Attribute VB_Name = \"Form1\"".Length));
        FormCodeText.RetargetVbName(code, "frmRenamed").Should().Be(
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = False\r\n" +
            "Attribute VB_Name = \"frmRenamed\"\r\n" +
            "Option Explicit\r\n");
    }

    [Fact]
    public void TheSpanExcludesTheLineEnding()
    {
        // A replace over a span that included the \r would join the attribute to the line below it.
        var (start, length) = FormCodeText.VbNameLine("Attribute VB_Name = \"Form1\"\r\nOption Explicit")!.Value;

        start.Should().Be(0);
        length.Should().Be("Attribute VB_Name = \"Form1\"".Length);
    }

    [Fact]
    public void LeadingBlankLinesAreSteppedOver()
    {
        FormCodeText.RetargetVbName("\r\n\r\nAttribute VB_Name = \"Form1\"\r\n", "frmRenamed")
            .Should().Be("\r\n\r\nAttribute VB_Name = \"frmRenamed\"\r\n");
    }

    [Fact]
    public void ALastLineWithNoTerminatorIsStillFound()
    {
        FormCodeText.RetargetVbName("Attribute VB_Name = \"Form1\"", "frmRenamed")
            .Should().Be("Attribute VB_Name = \"frmRenamed\"");
    }

    [Fact]
    public void OnlyTheLeadingRunCounts()
    {
        // An Attribute line further down belongs to a procedure, and the file's name is not decided there.
        const string code = "Option Explicit\r\nAttribute VB_Name = \"Form1\"\r\n";

        FormCodeText.VbNameLine(code).Should().BeNull();
        FormCodeText.RetargetVbName(code, "frmRenamed").Should().BeSameAs(code);
    }

    [Fact]
    public void AnAttributeWhoseNameMerelyStartsWithVbNameIsNotIt()
    {
        // A prefix test for "Attribute VB_Name" would claim this line and rewrite the wrong attribute.
        const string code = "Attribute VB_NameSpace = \"x\"\r\nAttribute VB_Name = \"Form1\"\r\n";

        FormCodeText.RetargetVbName(code, "frmRenamed")
            .Should().Be("Attribute VB_NameSpace = \"x\"\r\nAttribute VB_Name = \"frmRenamed\"\r\n");
    }

    [Fact]
    public void ALineThatAlreadySaysTheNameIsLeftAsItIsHoweverItIsSpaced()
    {
        // This runs on every committed designer change. Compared by text, a line VB6 did not space the way
        // HexIDE writes it would be rewritten by the first nudge of a control -- a change nobody made.
        const string code = "Attribute VB_Name=\"Form1\"\r\nOption Explicit\r\n";

        FormCodeText.RetargetVbName(code, "Form1").Should().BeSameAs(code);
    }

    [Fact]
    public void ACaseChangeIsARename()
    {
        // VB6 compares names without regard to case, but the file records the spelling, and a developer who
        // renames Form1 to FORM1 has asked for the spelling to change.
        FormCodeText.RetargetVbName("Attribute VB_Name = \"Form1\"\r\n", "FORM1")
            .Should().Be("Attribute VB_Name = \"FORM1\"\r\n");
    }

    [Fact]
    public void CodeWithNoAttributeBlockGainsNone()
    {
        // What a form HexIDE created holds. Inventing a block here would be a decision about what the file
        // says, taken by a routine that only exists to keep a name in step.
        const string code = "Private Sub Form_Load()\n\nEnd Sub";

        FormCodeText.RetargetVbName(code, "frmRenamed").Should().BeSameAs(code);
    }
}
