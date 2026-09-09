using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// `VB_Name` is a form's identity, it lives at the top of the code section, and neither VB6 nor HexIDE's
/// editor shows it. So "replace the code" written by someone reading the screen deletes it — which is how a
/// real form was damaged and committed before `git diff` caught it.
/// </summary>
public class FormCodeTextTests
{
    private const string Attrs =
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n";

    [Fact]
    public void An_attribute_block_is_restored_when_the_incoming_code_omits_it()
    {
        var result = FormCodeText.PreserveAttributes("Private Sub Form_Load()\r\nEnd Sub\r\n", Attrs + "Option Explicit\r\n");

        result.Should().StartWith("Attribute VB_Name = \"frmOrders\"");
        result.Should().Contain("Private Sub Form_Load()");
        result.Should().NotContain("Option Explicit", "the old BODY is replaced — only the header is kept");
    }

    [Fact]
    public void Incoming_code_that_already_has_a_block_is_left_exactly_alone()
    {
        // The get -> modify -> set round trip. Restoring here would double the block.
        var incoming = "Attribute VB_Name = \"frmRenamed\"\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n";

        FormCodeText.PreserveAttributes(incoming, Attrs).Should().Be(incoming);
    }

    [Fact]
    public void A_form_that_never_had_a_block_gains_nothing()
    {
        FormCodeText.PreserveAttributes("Private Sub Form_Load()\r\nEnd Sub\r\n", "Option Explicit\r\n")
            .Should().Be("Private Sub Form_Load()\r\nEnd Sub\r\n");
    }

    [Fact]
    public void Only_a_leading_run_counts_as_the_header()
    {
        // An Attribute line further down belongs to a procedure and is not the file's identity.
        const string code = "Private Sub Foo()\r\nAttribute Foo.VB_Description = \"x\"\r\nEnd Sub\r\n";

        FormCodeText.AttributeBlock(code).Should().BeEmpty();
    }

    [Theory]
    [InlineData("VERSION 5.00\r\nBegin VB.Form Form1 \r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n", true)]
    [InlineData("\r\n\r\nVERSION 5.00\r\nBegin VB.Form Form1 \r\nEnd\r\n", true)]
    [InlineData("Attribute VB_Name = \"Form1\"\r\nPrivate Sub Form_Load()\r\nEnd Sub\r\n", false)]
    [InlineData("Private Sub Form_Load()\r\nEnd Sub\r\n", false)]
    [InlineData("", false)]
    public void A_whole_frm_file_is_told_apart_from_a_code_section(string content, bool isFile)
    {
        FormCodeText.LooksLikeFormFile(content).Should().Be(isFile);
    }

    [Fact]
    public void A_lone_VERSION_line_is_not_enough_to_call_it_a_file()
    {
        // Both signals are required: VERSION alone could plausibly open a code section, VERSION followed by
        // a Begin block is the shape of the file and of nothing else.
        FormCodeText.LooksLikeFormFile("VERSION = 5\r\nDebug.Print VERSION\r\n").Should().BeFalse();
    }
}
