using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A <c>.bas</c> or <c>.cls</c> whose header the reader does not find where it expects it is saved back as it was,
/// with one header, not two (#472). The reader tested line 0 alone, so a blank first line left the whole file in the
/// body with an empty header, and Save Project, which writes every module, put the canonical header in front of it.
/// For a class that second header states <c>VB_Exposed = False</c> and <c>MultiUse = -1</c> whatever the file's own
/// says.
/// </summary>
public class LeadingBlankHeaderTests
{
    private const string ModuleHeader = "Attribute VB_Name = \"M\"\r\n";

    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = 0   'False\r\n" +
        "  Persistable = 0  'NotPersistable\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"M\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n" +
        "Attribute VB_PredeclaredId = False\r\n" +
        "Attribute VB_Exposed = True\r\n";

    private const string Body = "Option Explicit\r\n\r\nPublic X As Long\r\n";

    private static string Saved(string file, ModuleKind kind)
    {
        var (header, body) = ModuleFileFormat.SplitHeader(file, kind);
        return ModuleFileFormat.ToFileContent(body, "M", kind, header);
    }

    private static int VbNames(string text) => text.Split("Attribute VB_Name").Length - 1;

    [Theory]
    [InlineData(ModuleKind.StandardModule, ModuleHeader)]
    [InlineData(ModuleKind.ClassModule, ClassHeader)]
    public void A_blank_first_line_is_saved_back_with_one_header(ModuleKind kind, string header)
    {
        var file = "\r\n" + header + Body;

        var saved = Saved(file, kind);

        saved.Should().Be(file, "an unedited module is written back byte for byte");
        VbNames(saved).Should().Be(1);
    }

    [Theory]
    [InlineData(ModuleKind.StandardModule, ModuleHeader)]
    [InlineData(ModuleKind.ClassModule, ClassHeader)]
    public void A_blank_first_line_leaves_the_header_out_of_the_code(ModuleKind kind, string header)
    {
        var (found, body) = ModuleFileFormat.SplitHeader("\r\n\r\n" + header + Body, kind);

        found.Should().Be("\r\n\r\n" + header, "the blank lines above it are written back with it");
        body.Should().Be(Body);
    }

    [Fact]
    public void A_BEGIN_with_no_END_leaves_the_code_in_the_body()
    {
        var file = ClassHeader.Replace("END\r\n", "") + "Public Sub Go()\r\nEnd Sub\r\n";

        var (_, body) = ModuleFileFormat.SplitHeader(file, ModuleKind.ClassModule);

        body.Should().Be("Public Sub Go()\r\nEnd Sub\r\n");
    }

    [Fact]
    public void A_BEGIN_with_no_END_is_saved_back_byte_for_byte()
    {
        var file = ClassHeader.Replace("END\r\n", "") + "Public Sub Go()\r\nEnd Sub\r\n";

        Saved(file, ModuleKind.ClassModule).Should().Be(file);
    }

    [Fact]
    public void A_header_that_runs_to_the_end_of_the_file_gains_no_line()
    {
        // Nothing after the attributes, so the header is the whole file.
        Saved(ClassHeader, ModuleKind.ClassModule).Should().Be(ClassHeader);
        Saved(ModuleHeader, ModuleKind.StandardModule).Should().Be(ModuleHeader);

        // The last line is the header's and has no line break of its own: none is added.
        Saved("Attribute VB_Name = \"M\"", ModuleKind.StandardModule).Should().Be("Attribute VB_Name = \"M\"");

        // A BEGIN with neither END nor attributes runs to the end of the file. A line break added after it grew the file
        // by one blank line on every save, which the issue measured.
        const string unterminated = "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\n";
        Saved(unterminated, ModuleKind.ClassModule).Should().Be(unterminated);
    }

    [Fact]
    public void A_header_the_reader_still_does_not_recognise_is_not_given_a_second()
    {
        // A comment above the attributes is not a shape VB6 writes, and the reader leaves it in the body. The body then
        // carries its own VB_Name, so nothing goes in front of it.
        var file = "' generated\r\n" + ModuleHeader + Body;

        ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule).Header.Should().BeEmpty();
        Saved(file, ModuleKind.StandardModule).Should().Be(file);
    }

    [Fact]
    public void A_module_with_no_header_at_all_is_still_given_one()
    {
        // VB6 writes VB_Name into a .bas that had none, and HexIDE did and still does.
        Saved(Body, ModuleKind.StandardModule).Should().Be(ModuleHeader + Body);
    }

    [Fact]
    public void Blank_lines_above_code_with_no_header_stay_in_the_code()
    {
        var file = "\r\n\r\nPublic X As Long\r\n";

        ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule).Should().Be(("", file));
    }
}
