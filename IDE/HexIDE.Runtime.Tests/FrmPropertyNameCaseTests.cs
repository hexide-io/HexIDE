using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A <c>.frm</c> property name is read in any case, as VB6 reads it (#811). Measured, all loading in vb6.exe: a
/// Label written with <c>caption</c> and <c>backcolor</c>, a TextBox with <c>TEXT</c>, and a form whose own root is
/// written <c>caption</c>, <c>clientwidth</c>, <c>scalewidth</c> and so on, with a Font block of <c>name</c>,
/// <c>size</c> and <c>weight</c> (Arial, 12, bold).
/// </summary>
/// <remarks>
/// The lowercase root below also writes the outer rectangle, <c>scalemode</c> and <c>lockcontrols</c>, which the
/// probe did not. Those are not a claim about VB6: they pin that the reader maps each name to its own spelling before
/// any special case compares it (#819).
/// </remarks>
public class FrmPropertyNameCaseTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string error) { }
    }

    private static FormDefinition Load(string source) =>
        new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), source, new Sink())!;

    private static string Save(FormDefinition form) => new FormSerializer().Serialize(form, "Form1.frm").Item1;

    private const string Controls = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Probe"
           Begin VB.TextBox Text1
              Height          =   375
              Left            =   120
              TEXT            =   "upper key"
              Top             =   120
              Width           =   1800
           End
           Begin VB.Label Label1
              caption         =   "lower key"
              Height          =   375
              Left            =   2160
              Top             =   120
              Width           =   1200
              backcolor       =   &H000000FF&
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [Fact]
    public void A_controls_property_name_in_another_case_loads_as_the_property()
    {
        var form = Load(Controls);
        var text1 = form.Components.Single(c => c.BaseClass is TextBoxComponentClass);
        var label1 = form.Components.Single(c => c.BaseClass is LabelComponentClass);

        text1.GetPropertyOrDefault(TextProperty).Should().Be("upper key");
        label1.GetPropertyOrDefault(CaptionProperty).Should().Be("lower key");
        label1.GetPropertyOrDefault(BackColorProperty).Should().Be(VBColor.FromOle(255));
    }

    [Fact]
    public void A_property_read_in_another_case_is_saved_once_in_VB6s_own_spelling()
    {
        var saved = Save(Load(Controls));

        saved.Should().Contain("Caption         =   \"lower key\"").And.NotContain("caption ");
        saved.Split('\n').Count(l => l.TrimStart().StartsWith("Caption", StringComparison.OrdinalIgnoreCase))
            .Should().Be(2, "the form's and the Label's, each once");
    }

    // The root's two rectangles, written in lowercase. Before the names were mapped to one spelling, a lowercase
    // `width` missed the outer-rectangle branch and, once property lookup ignored case, bound as the CLIENT width,
    // so the form was sized to its outer rectangle and the outer one was lost (#819).
    private const string LowercaseRoot = """
        VERSION 5.00
        Begin VB.Form Dialog
           clientheight    =   3195
           clientleft      =   2760
           clienttop       =   3750
           clientwidth     =   6030
           height          =   3600
           left            =   2700
           top             =   3405
           width           =   6150
           scalemode       =   1  'Twip
           scaleheight     =   3195
           scalewidth      =   6030
           lockcontrols    =   -1  'True
           BeginProperty Font
              name            =   "Arial"
              size            =   12
              charset         =   0
              weight          =   700
              underline       =   0   'False
              italic          =   0   'False
              strikethrough   =   0   'False
           EndProperty
        End
        Attribute VB_Name = "Dialog"
        """;

    [Fact]
    public void A_lowercase_root_keeps_its_client_rectangle_and_records_its_outer_one()
    {
        var form = Load(LowercaseRoot);
        var root = form.Components.Single(c => c.BaseClass is FormComponentClass);

        root.GetPropertyOrDefault(WidthProperty).Should().Be(6030 / 15.0);
        root.GetPropertyOrDefault(HeightProperty).Should().Be(3195 / 15.0);
        form.OuterRect.Should().NotBeNull("the file declared an outer rectangle");
    }

    [Fact]
    public void A_lowercase_root_keeps_its_scale_and_its_lock()
    {
        var form = Load(LowercaseRoot);

        form.Scale.Should().NotBeNull();
        form.Scale!.Width.Should().Be(6030);
        form.LockControls.Should().BeTrue();
    }

    [Fact]
    public void A_Font_block_with_lowercase_fields_loads()
    {
        var root = Load(LowercaseRoot).Components.Single(c => c.BaseClass is FormComponentClass);

        var font = root.GetPropertyOrDefault(FontProperty);
        font.FontFamilyName.Should().Be("Arial");
        font.Size.Should().Be(12);
        font.Weight.Should().Be(700);
    }
}
