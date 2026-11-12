using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Module headers are collected as VB6 saves them, with preceding text below them (#3757, #3777).
/// This supersedes #472's unmeasured choice to preserve blank lines above the header.
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

        saved.Should().Be(header + "\r\n" + Body, "VB6 moves the blank line below the header (#3777 B2)");
        VbNames(saved).Should().Be(1);
    }

    [Theory]
    [InlineData(ModuleKind.StandardModule, ModuleHeader)]
    [InlineData(ModuleKind.ClassModule, ClassHeader)]
    public void A_blank_first_line_leaves_the_header_out_of_the_code(ModuleKind kind, string header)
    {
        var (found, body) = ModuleFileFormat.SplitHeader("\r\n\r\n" + header + Body, kind);

        found.Should().Be(header);
        body.Should().Be("\r\n\r\n" + Body);
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
    public void A_name_below_a_comment_becomes_the_only_header()
    {
        var file = "' generated\r\n" + ModuleHeader + Body;

        ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule).Header.Should().Be(ModuleHeader);
        Saved(file, ModuleKind.StandardModule).Should().Be(ModuleHeader + "' generated\r\n" + Body);
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
    // Measured by #3777: docs/vb6-fidelity-oracle/a-modules-name-line-below-the-top-of-its-file.md.
    [Theory]
    [InlineData("' c\r\n")]
    [InlineData("Option Explicit\r\n")]
    [InlineData("Public Counter As Long\r\n")]
    [InlineData("Public Function F() As Long\r\nF = 7\r\nEnd Function\r\n")]
    public void A_module_name_below_code_moves_to_the_header(string above)
    {
        var (header, body) = ModuleFileFormat.SplitHeader(above + ModuleHeader + Body, ModuleKind.StandardModule);
        header.Should().Be(ModuleHeader);
        body.Should().Be(above + Body);
        ModuleFileFormat.ToFileContent(body, "QRenamed", ModuleKind.StandardModule, header)
            .Should().Be("Attribute VB_Name = \"QRenamed\"\r\n" + above + Body);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_class_comment_moves_below_its_complete_header(bool beforeVersion)
    {
        var file = beforeVersion ? "' c\r\n" + ClassHeader + Body
            : ClassHeader.Replace("Attribute VB_Name", "' c\r\nAttribute VB_Name") + Body;
        var (header, body) = ModuleFileFormat.SplitHeader(file, ModuleKind.ClassModule);
        header.Should().Be(ClassHeader);
        body.Should().Be("' c\r\n" + Body);
    }

    [Fact]
    public void Normal_headers_keep_their_bytes_and_member_attributes_stay_in_the_body()
    {
        const string members = "Public Sub Foo()\r\nAttribute Foo.VB_Description = \"x\"\r\nEnd Sub\r\n";
        Saved(ModuleHeader + members, ModuleKind.StandardModule).Should().Be(ModuleHeader + members);
        Saved(ClassHeader + members, ModuleKind.ClassModule).Should().Be(ClassHeader + members);
        ModuleFileFormat.SplitHeader(members, ModuleKind.StandardModule).Should().Be(("", members));
    }

    [Fact]
    public void Unmeasured_names_inside_procedures_and_second_names_stay_in_place()
    {
        var body = "Public Sub Foo()\r\n" + ModuleHeader + "End Sub\r\n";
        ModuleFileFormat.SplitHeader(body, ModuleKind.StandardModule).Should().Be(("", body));
        ModuleFileFormat.SplitHeader(ModuleHeader + ModuleHeader + Body, ModuleKind.StandardModule)
            .Should().Be((ModuleHeader, ModuleHeader + Body));
    }

    [Fact]
    public void An_unmeasured_separated_class_attribute_run_keeps_the_old_split()
    {
        const string prefix = "VERSION 1.0 CLASS\r\nBEGIN\r\nEND\r\nAttribute VB_Exposed = True\r\n";
        const string body = "' between attributes\r\nAttribute VB_Name = \"M\"\r\nOption Explicit\r\n";
        ModuleFileFormat.SplitHeader(prefix + body, ModuleKind.ClassModule).Should().Be((prefix, body));
    }

    [Fact]
    public void A_standard_leading_attribute_run_keeps_its_original_order()
    {
        const string prefix = "Attribute VB_Description = \"x\"\r\n" + ModuleHeader;
        ModuleFileFormat.SplitHeader(prefix + Body, ModuleKind.StandardModule).Should().Be((prefix, Body));
    }

    [Fact]
    public void An_unterminated_final_name_keeps_the_old_split_instead_of_joining_the_body()
    {
        const string file = "' c\r\nAttribute VB_Name = \"M\"";
        ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule).Should().Be(("", file));
    }

    [Fact]
    public void A_dotted_member_attribute_is_not_collected_with_the_module_name()
    {
        const string member = "Attribute Foo.VB_Description = \"x\"\r\n";
        ModuleFileFormat.SplitHeader(member + ModuleHeader + Body, ModuleKind.StandardModule)
            .Should().Be((ModuleHeader, member + Body));
    }

}
