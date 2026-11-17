using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A designer file damaged in ways VB6 never writes opens read-only, or saves to what it read, never a form the save
/// then refuses or that changes from one save to the next (#4096, found by #1222's <c>.frm</c> fuzz run).
/// </summary>
public class DamagedDesignerTextTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public List<string> Errors { get; } = new();
        public void LogError(string error) => Errors.Add(error);
    }

    // Built from character codes, so no escape in this file can be read as a different character on its way here.
    private static readonly string Cr = ((char)13).ToString();
    private static readonly string CrLf = Cr + (char)10;
    private static readonly string Nul = ((char)0).ToString();

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Form(params string[] body) =>
        string.Join(CrLf, ["VERSION 5.00", "Begin VB.Form Form1 ", "   Caption         =   \"Form1\"", .. body, "End",
            "Attribute VB_Name = \"Form1\"", ""]);

    [Fact]
    public void A_citation_on_a_line_a_lone_CR_ends_is_counted()
    {
        // The designer reader ends a line at a lone CR (#1020), so the count must too.
        var text = "      Tag             =   \"x\"" + CrLf + "      Ext             =   \"Form1.frx\":0004" + Cr
            + "      Width           =   100" + CrLf;

        FrxDeserializer.CitedOffsets(text).Should().Equal(4);
    }

    [Fact]
    public void A_citation_with_more_after_it_on_its_line_is_counted_where_the_writer_finds_it()
    {
        FrxDeserializer.CitedOffsets("   Begin NoSuchLib.Widget \"Form1.frx\":007Fx Widget1").Should().Equal(0x7F);
    }

    [Fact]
    public void A_carried_citation_a_lone_CR_ends_opens_read_only_with_no_companion_to_honour_it()
    {
        // With no opening quote the writer leaves the line alone, and once a save had ended it with CRLF the reload counted
        // it as a citation nothing honours. Counted at the first open, the form is read-only from the start.
        var form = Load(Form("   Begin VB.PictureBox Picture1 ", "      Ext             =   Form1.frx\":0000" + Cr
            + "      Width           =   100", "   End"));

        form.CanSaveFaithfully.Should().BeFalse("the save would write a citation its own reload refuses");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
    }

    [Fact]
    public void A_carried_Begin_line_holding_a_citation_opens_read_only_with_no_companion_to_honour_it()
    {
        // The save cites through every carried line, a preserved block's Begin line included, and refused this one only once
        // the user saved: the record it names isn't held.
        var form = Load(Form("   Begin NoSuchLib.Widget \"Form1.frx\":0000x Widget1", "   End"));

        form.CanSaveFaithfully.Should().BeFalse();
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.UnreproducibleBinaryContent);
        Action save = () => new FormSerializer().Serialize(form, "Form1.frm");
        save.Should().Throw<InvalidOperationException>("the writer's own refusal still stands behind the load's");
    }

    [Fact]
    public void A_NUL_in_the_designer_text_opens_read_only()
    {
        var form = Load(Form("   Begin NoSuchLib.Widget Widget1", "      Note            =   \"a" + Nul + "b\"", "   End"));

        form.CanSaveFaithfully.Should().BeFalse("the writer marks citations with NULs, so it can't write one back beside them");
        form.UnfaithfulSaveCauses.Should().HaveFlag(UnfaithfulSaveCause.NulInDesignerText);
    }

    [Fact]
    public void A_NUL_in_the_code_alone_leaves_the_designer_saveable()
    {
        var form = Load(Form() + "Private Sub Form_Load()" + CrLf + "    ' a" + Nul + "b" + CrLf + "End Sub" + CrLf);

        form.UnfaithfulSaveCauses.Should().NotHaveFlag(UnfaithfulSaveCause.NulInDesignerText);
        form.CanSaveFaithfully.Should().BeTrue();
    }
}
