using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// `VB_Name` is a form's identity and it lives at the top of the code section, in the leading run of `Attribute`
/// lines. These pin how that run is found. Keeping it in front of code sent alone is `GuardedContent`'s job, and
/// the method that once restored it is gone with its last caller (#686).
/// </summary>
public class FormCodeTextTests
{
    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void AttributeBlock_keeps_the_exact_header_bytes_for_both_endings(string eol)
    {
        var block = string.Join(eol,
            "Attribute VB_Name = \"Form1\"",
            "Attribute VB_GlobalNameSpace = False",
            "Attribute VB_Creatable = False",
            "Attribute VB_PredeclaredId = True",
            "Attribute VB_Exposed = False") + eol;

        FormCodeText.AttributeBlock(block + "Option Explicit" + eol).Should().Be(block);
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void AttributeBlock_includes_leading_blank_lines_in_the_header(string eol)
    {
        var block = eol + eol + "Attribute VB_Name = \"Form1\"" + eol;
        FormCodeText.AttributeBlock(block + "Option Explicit" + eol).Should().Be(block);
    }

    [Fact]
    public void Only_a_leading_run_counts_as_the_header()
    {
        // An Attribute line further down belongs to a procedure and is not the file's identity.
        const string code = "Private Sub Foo()\r\nAttribute Foo.VB_Description = \"x\"\r\nEnd Sub\r\n";

        FormCodeText.AttributeBlock(code).Should().BeEmpty();
    }
}
