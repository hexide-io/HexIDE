using HexIDE.IDE;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// A lone CR ends a line for every reader of a module's or a form's lines, as it does for the code window, the
/// <c>.frm</c> designer reader and VB6 (#1020). These readers split on LF alone, so a <c>.bas</c> whose lines end with
/// a lone CR was one line to them: the header split took it all for the header, and Save Project wrote the file back as
/// its <c>VB_Name</c> line alone.
/// </summary>
/// <remarks>
/// VB6's answers are from <c>vb6.exe /make</c> on byte-exact files (docs/vb6-fidelity-oracle/a-lone-cr-or-lf-in-vb6-source.md, "A lone CR or LF in
/// VB6 source"). A <c>.bas</c> whose lines all end with a lone CR builds and runs as its CRLF twin. A lone CR between two
/// statements ends the first. A lone CR between a procedure and its <c>Attribute</c> line leaves the line describing it,
/// and a CR before a CRLF there is a blank line, which leaves it describing nothing. A line ending in <c> _</c> and then
/// a lone CR continues onto the next.
/// </remarks>
public class LoneCrLineEndTests
{
    private const string CrModule =
        "Attribute VB_Name = \"M\"\r" +
        "Public Sub Foo()\r" +
        "Attribute Foo.VB_Description = \"d\"\r" +
        "End Sub\r";

    private const string CrFormCode =
        "Attribute VB_Name = \"Form1\"\r" +
        "Attribute VB_PredeclaredId = True\r" +
        "Private Sub Form_Load()\r" +
        "x = 1\r" +
        "End Sub\r";

    private const string CrDesigner =
        "VERSION 5.00\r" +
        "Begin VB.Form Form1 \r" +
        "   Caption         =   \"Form1\"\r" +
        "End\r";

    private static string Crlf(string text) => text.Replace("\r", "\r\n");

    // -- The rule ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void CRLF_a_lone_CR_and_a_lone_LF_each_end_one_line()
    {
        const string text = "a\rb\r\nc\nd";

        SourceLines.Split(text).Should().Equal("a", "b", "c", "d");
        SourceLines.Count(text).Should().Be(3);
        SourceLines.LineAt(text, 0).Should().Be((1, 2));
        SourceLines.LineAt(text, 2).Should().Be((3, 5));
        SourceLines.LineAt(text, 7).Should().Be((8, 8), "a last line with no terminator ends at the end of the text");
    }

    // -- The header split, and what Save Project writes -------------------------------------------------------------------

    [Fact]
    public void A_CR_only_module_keeps_its_code_through_a_save()
    {
        var (header, body) = ModuleFileFormat.SplitHeader(CrModule, ModuleKind.StandardModule);

        header.Should().Be("Attribute VB_Name = \"M\"\r\n");
        body.Should().Be("Public Sub Foo()\r\nAttribute Foo.VB_Description = \"d\"\r\nEnd Sub\r\n");
        ModuleFileFormat.ToFileContent(body, "M", ModuleKind.StandardModule, header).Should().Be(Crlf(CrModule),
            "every line is written back, each ending CRLF as the header split joins them");
    }

    [Fact]
    public void A_stray_CR_does_not_lose_the_line_it_joins()
    {
        const string file = "Attribute VB_Name = \"M\"\rOption Explicit\r\nPublic Sub Foo()\r\nEnd Sub\r\n";

        var (header, body) = ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule);

        ModuleFileFormat.ToFileContent(body, "M", ModuleKind.StandardModule, header)
            .Should().Be("Attribute VB_Name = \"M\"\r\nOption Explicit\r\nPublic Sub Foo()\r\nEnd Sub\r\n");
    }

    [Fact]
    public void A_CR_only_class_has_code_outside_its_header()
    {
        const string cls =
            "VERSION 1.0 CLASS\rBEGIN\r  MultiUse = -1  'True\rEND\r" +
            "Attribute VB_Name = \"C\"\rAttribute VB_Exposed = False\r" +
            "Public Sub Go()\rEnd Sub\r";

        var (header, body) = ModuleFileFormat.SplitHeader(cls, ModuleKind.ClassModule);

        header.Should().Be(Crlf("VERSION 1.0 CLASS\rBEGIN\r  MultiUse = -1  'True\rEND\rAttribute VB_Name = \"C\"\rAttribute VB_Exposed = False\r"));
        body.Should().Be("Public Sub Go()\r\nEnd Sub\r\n");
    }

    [Fact]
    public void A_header_the_split_did_not_find_is_not_doubled_when_its_VB_Name_follows_a_lone_CR()
    {
        // A comment above the attributes leaves the whole file in the body, and the body's own VB_Name line, below a
        // lone CR, is what tells the writer to add no header of its own.
        const string file = "' generated\rAttribute VB_Name = \"M\"\rPublic X As Long\r";

        var (header, body) = ModuleFileFormat.SplitHeader(file, ModuleKind.StandardModule);

        header.Should().BeEmpty();
        ModuleFileFormat.ToFileContent(body, "M", ModuleKind.StandardModule, header).Should().Be(file);
    }

    [Fact]
    public void A_module_renamed_keeps_its_code()
    {
        ModuleFileFormat.ToFileContent("Public X As Long\r\n", "Renamed", ModuleKind.StandardModule,
                "Attribute VB_Name = \"M\"\rAttribute VB_Exposed = False\r")
            .Should().Be("Attribute VB_Name = \"Renamed\"\r\nAttribute VB_Exposed = False\r\nPublic X As Long\r\n");
    }

    // -- A form's VB_Name line ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_CR_only_forms_VB_Name_line_is_its_first_line()
    {
        FormCodeText.VbNameLine(CrFormCode).Should().Be((0, "Attribute VB_Name = \"Form1\"".Length));
        FormCodeText.AttributeBlock(CrFormCode).Should().Be("Attribute VB_Name = \"Form1\"\rAttribute VB_PredeclaredId = True\r");
    }

    [Fact]
    public void Retargeting_a_CR_only_form_changes_its_VB_Name_line_and_nothing_else()
    {
        FormCodeText.RetargetVbName(CrFormCode, "Form1").Should().BeSameAs(CrFormCode, "the name has not changed");
        FormCodeText.RetargetVbName(CrFormCode, "frmMain")
            .Should().Be(CrFormCode.Replace("VB_Name = \"Form1\"", "VB_Name = \"frmMain\""));
    }

    [Fact]
    public void A_lone_CR_counts_as_a_line_break_for_moving_marks()
    {
        FormCodeText.LineBreaks(CrDesigner).Should().Be(4);
        FormCodeText.LineBreaks("a\r\nb\rc\n").Should().Be(3);
    }

    // -- The read-only regions ---------------------------------------------------------------------------------------------

    [Fact]
    public void A_CR_only_module_has_a_header_region_and_a_run_where_the_code_window_draws_them()
    {
        var regions = ReadOnlyRegions.Of(CrModule, 0);

        var vbName = "Attribute VB_Name = \"M\"\r".Length;
        regions[0].Should().Be(new TextRegion(0, vbName), "the header is the VB_Name line, not the whole module");
        var run = ReadOnlyRegions.MemberRuns(CrModule, 0).Should().ContainSingle().Subject;
        run.Kind.Should().Be(MemberRunKind.Procedure);
        run.Member!.Value.Name.Should().Be("Foo");
        run.Anchor.Should().Be(vbName + "Public Sub Foo()".Length);
    }

    [Fact]
    public void A_CR_only_form_buffer_holds_its_designer_block_read_only()
    {
        var buffer = CrDesigner + CrFormCode;

        var headerEnd = ReadOnlyRegions.HeaderEnd(buffer, CrDesigner.Length);

        headerEnd.Should().Be(CrDesigner.Length + FormCodeText.AttributeBlock(CrFormCode).Length,
            "the designer block and the attribute run below it are the header");
        ReadOnlyRegions.Of(buffer, CrDesigner.Length).Should().NotBeEmpty();
    }

    [Fact]
    public void A_CR_before_a_CRLF_is_a_blank_line_and_the_run_under_it_describes_nothing()
    {
        // VB6: a Property Get whose VB_UserMemId line sat under "\r\r\n" was no default member (438 at run time).
        const string text = "Public Sub Foo()\r\r\nAttribute Foo.VB_Description = \"d\"\r\nEnd Sub\r\n";

        ReadOnlyRegions.MemberRuns(text, 0).Should().ContainSingle().Which.Kind.Should().Be(MemberRunKind.Orphan);
    }

    [Fact]
    public void A_run_under_a_declaration_that_follows_a_lone_CR_describes_it()
    {
        // VB6: a lone CR ends a line, so the Sub is the line directly above the run.
        const string text = "Dim x As Long\rPublic Sub Foo()\r\nAttribute Foo.VB_Description = \"d\"\r\nEnd Sub\r\n";

        var run = ReadOnlyRegions.MemberRuns(text, 0).Should().ContainSingle().Subject;

        run.Kind.Should().Be(MemberRunKind.Procedure);
        run.Member!.Value.Name.Should().Be("Foo");
    }

    [Fact]
    public void A_continuation_ending_in_a_lone_CR_continues()
    {
        // VB6: " _" and then a lone CR continues the line, as with CRLF, so the run's line goes on to the next.
        const string text = "Public Sub Foo(a As Long, _\rb As Long)\r\nAttribute Foo.VB_Description = \"d\"\r\nEnd Sub\r\n";

        ReadOnlyRegions.MemberRuns(text, 0).Should().ContainSingle().Which.Kind.Should().Be(MemberRunKind.Procedure);
    }

    [Fact]
    public void A_variable_declared_after_a_continuation_ending_in_a_lone_CR_is_the_one_its_run_names()
    {
        // A variable's run binds by name, so the declaration has to be read past the continuation for b to be found.
        const string text = "Public a As Long, _\rb As Long\r\nAttribute b.VB_VarDescription = \"d\"\r\n";

        var run = ReadOnlyRegions.MemberRuns(text, 0).Should().ContainSingle().Subject;

        run.DescribesMember.Should().BeTrue();
        run.Member!.Value.Name.Should().Be("b");
    }

    // -- The guard, and a formatting edit --------------------------------------------------------------------------------

    [Fact]
    public void Code_sent_alone_into_a_CR_only_form_keeps_the_designer_block()
    {
        var current = CrDesigner + CrFormCode;
        const string incoming = "Private Sub Form_Load()\r\nx = 2\r\nEnd Sub\r\n";

        var result = GuardedContent.Replace(current, CrDesigner.Length, incoming);

        result.Refusal.Should().BeNull();
        result.HeaderKept.Should().BeTrue();
        result.NewText.Should().StartWith(CrDesigner + FormCodeText.AttributeBlock(CrFormCode));
        result.NewText.Should().EndWith(incoming);
    }

    [Fact]
    public void The_whole_file_sent_back_with_CRLF_into_a_CR_only_form_is_its_header_unchanged()
    {
        // The guard compares the header line by line without terminators, so a caller's CRLF is not a changed header.
        var current = CrDesigner + CrFormCode;
        var header = CrDesigner + FormCodeText.AttributeBlock(CrFormCode);
        const string code = "Private Sub Form_Load()\r\nx = 2\r\nEnd Sub\r\n";

        var result = GuardedContent.Replace(current, CrDesigner.Length, Crlf(header) + code);

        result.Refusal.Should().BeNull();
        result.NewText.Should().Be(header + code, "the document keeps its own header, byte for byte");
    }

    [Fact]
    public void A_continued_attribute_line_sent_back_with_CRLF_is_not_a_changed_run()
    {
        const string current = "Public Sub Halt()\r\nAttribute Halt.VB_Description = _\r\"Stops\"\r\nEnd Sub\r\n";
        var incoming = current.Replace("_\r\"", "_\r\n\"").Replace("End Sub", "Beep\r\nEnd Sub");

        var result = GuardedContent.Replace(current, 0, incoming);

        result.Refusal.Should().BeNull("only the line end inside the run differs, and the run is the same lines");
    }

    [Fact]
    public void A_formatting_rewrite_of_a_CR_only_form_leaves_its_header_and_keeps_its_line_ends()
    {
        var before = CrDesigner + CrFormCode;
        var after = string.Join("\n", (CrDesigner + CrFormCode).Split('\r').Select(l => l.TrimStart())).Replace("x = 1", "    x = 1");

        var reduction = LineEdits.Reduce(before, after, ReadOnlyRegions.Of(before, CrDesigner.Length));

        LineEdits.Apply(before, reduction.Changes).Should().Be(before.Replace("\rx = 1\r", "\r    x = 1\r"),
            "the code is formatted, the designer block is not, and the new line ends with a lone CR like the rest");
        reduction.DroppedLines.Should().BeGreaterThan(0, "the designer block's re-indent was dropped");
    }
}
