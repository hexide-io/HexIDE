using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A quote inside a <c>.frm</c> string property is written doubled, and a doubled quote is read as one, as VB6 does
/// (#218). The reader kept both quotes and the writer doubled none, so HexIDE round-tripped its own files while
/// reading VB6's wrongly and writing files VB6 refuses.
/// </summary>
/// <remarks>
/// Measured on the oracle (the oracle's "A quote inside a .frm string"). The form below, built with <c>vb6.exe /make</c>
/// as p-code, read back every value in the table below; the VB6 IDE re-saved it with every string line unchanged. The
/// same form written with the quotes single, as HexIDE wrote it, does not load: <c>Line 13: Syntax error: property
/// hi"" in lbl1 was missing an '='</c>, and the build fails.
/// </remarks>
public class FrmStringQuoteTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").Item1;

    // The probe's form, less its code.
    private const string Probe = """""""
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "F""orm"
           ClientHeight    =   3600
           ClientLeft      =   60
           ClientTop       =   450
           ClientWidth     =   6000
           LinkTopic       =   "Link""Topic"
           ScaleHeight     =   3600
           ScaleWidth      =   6000
           Begin VB.Label lbl1
              Caption         =   "He said ""hi"""
              Height          =   255
              Left            =   120
              Tag             =   "t""g"
              Top             =   120
              Width           =   3000
           End
           Begin VB.Label lbl2
              Caption         =   """"
              Height          =   255
              Left            =   120
              Top             =   420
              Width           =   3000
           End
           Begin VB.Label lbl3
              Caption         =   """Start"
              Height          =   255
              Left            =   120
              Top             =   720
              Width           =   3000
           End
           Begin VB.Label lbl4
              Caption         =   "End"""
              Height          =   255
              Left            =   120
              Top             =   1020
              Width           =   3000
           End
           Begin VB.Label lbl5
              Caption         =   """"""
              Height          =   255
              Left            =   120
              Top             =   1320
              Width           =   3000
           End
           Begin VB.CommandButton cmd1
              Caption         =   "Go"
              Height          =   375
              Left            =   120
              TabIndex        =   0
              ToolTipText     =   "tip""s"
              Top             =   1680
              Width           =   1215
           End
           Begin VB.TextBox txt1
              Height          =   375
              Left            =   120
              TabIndex        =   1
              Text            =   "a""b"
              Top             =   2160
              Width           =   2000
           End
        End
        Attribute VB_Name = "Form1"
        """"""";

    private static ComponentInstance Named(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(NameProperty) == name);

    [Theory]
    [InlineData("Form1", "F\"orm")]
    [InlineData("lbl1", "He said \"hi\"")]
    [InlineData("lbl2", "\"")]
    [InlineData("lbl3", "\"Start")]
    [InlineData("lbl4", "End\"")]
    [InlineData("lbl5", "\"\"")]
    public void A_doubled_quote_in_a_caption_reads_as_one(string control, string expected)
    {
        Named(Load(Probe), control).GetPropertyOrDefault(CaptionProperty).Should().Be(expected);
    }

    [Fact]
    public void A_doubled_quote_reads_as_one_in_every_string_property()
    {
        var form = Load(Probe);

        Named(form, "lbl1").GetPropertyOrDefault(TagProperty).Should().Be("t\"g");
        Named(form, "txt1").GetPropertyOrDefault(TextProperty).Should().Be("a\"b");
        // vb6.exe read cmd1's back as tip"s (#218), and HexIDE models it on a CommandButton since #1403.
        Named(form, "cmd1").GetPropertyOrDefault(ToolTipTextProperty).Should().Be("tip\"s");
        Named(form, "cmd1").UnknownRawProperties.Should().NotContain(p => p.Name == "ToolTipText");
    }

    [Fact]
    public void A_quote_is_written_doubled()
    {
        var form = Load(Probe);
        Named(form, "lbl1").SetProperty(CaptionProperty, "Say \"when\"");

        Save(form).Should().Contain("Caption         =   \"Say \"\"when\"\"\"\r\n");
    }

    [Fact]
    public void Each_string_line_is_saved_as_VB6_saved_it()
    {
        var saved = Save(Load(Probe));

        // The lines the VB6 IDE wrote when it re-saved the probe's form. LinkTopic is not modelled: it is kept as the
        // file had it, and must not be doubled a second time.
        foreach (var line in new[]
                 {
                     "Caption         =   \"F\"\"orm\"",
                     "LinkTopic       =   \"Link\"\"Topic\"",
                     "Caption         =   \"He said \"\"hi\"\"\"",
                     "Tag             =   \"t\"\"g\"",
                     "Caption         =   \"\"\"\"",
                     "Caption         =   \"\"\"Start\"",
                     "Caption         =   \"End\"\"\"",
                     "Caption         =   \"\"\"\"\"\"",
                     "ToolTipText     =   \"tip\"\"s\"",
                     "Text            =   \"a\"\"b\"",
                 })
            saved.Should().Contain(line + "\r\n");
    }

    [Fact]
    public void A_font_name_takes_the_same_rule()
    {
        // Not measured: a font name with a quote names no real face. It reaches the same reader and writer as every
        // other string, so it must come back as it went in.
        const string withFont = """""""
            VERSION 5.00
            Begin VB.Form Form1
               Caption         =   "Form1"
               BeginProperty Font
                  Name            =   "My ""Face"""
                  Size            =   8.25
                  Charset         =   0
                  Weight          =   400
                  Underline       =   0   'False
                  Italic          =   0   'False
                  Strikethrough   =   0   'False
               EndProperty
            End
            Attribute VB_Name = "Form1"
            """"""";
        var form = Load(withFont);

        form.Components[0].GetPropertyOrDefault(FontProperty).FontFamilyName.Should().Be("My \"Face\"");
        Save(form).Should().Contain("Name            =   \"My \"\"Face\"\"\"\r\n");
    }
}
