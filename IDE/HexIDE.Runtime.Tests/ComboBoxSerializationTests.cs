using System.Linq;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A ComboBox's and a TextBox's Text in the .frm (#926). The form is the one VB6's designer saved after a TextBox and three
/// ComboBoxes were drawn and two of them given Style 1 and 2 (the oracle's "Drawn in the VB6 IDE's designer").
/// </summary>
public class ComboBoxSerializationTests
{
    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private const string Head =
        "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   Caption         =   \"Form1\"\r\n   ClientHeight    =   3600\r\n"
        + "   ClientLeft      =   60\r\n   ClientTop       =   405\r\n   ClientWidth     =   4800\r\n   LinkTopic       =   \"Form1\"\r\n"
        + "   ScaleHeight     =   3600\r\n   ScaleWidth      =   4800\r\n";

    private const string Tail = "End\r\nAttribute VB_Name = \"Form1\"\r\n";

    private const string Combo3 =
        "   Begin VB.ComboBox Combo3 \r\n      Height          =   315\r\n      Left            =   1800\r\n"
        + "      Style           =   2  'Dropdown List\r\n      TabIndex        =   3\r\n      Top             =   1680\r\n"
        + "      Width           =   1215\r\n   End\r\n";

    private const string Combo2 =
        "   Begin VB.ComboBox Combo2 \r\n      Height          =   315\r\n      Left            =   1800\r\n"
        + "      Style           =   1  'Simple Combo\r\n      TabIndex        =   2\r\n      Text            =   \"Combo2\"\r\n"
        + "      Top             =   1680\r\n      Width           =   1215\r\n   End\r\n";

    private const string Combo1 =
        "   Begin VB.ComboBox Combo1 \r\n      Height          =   315\r\n      Left            =   1800\r\n"
        + "      TabIndex        =   1\r\n      Text            =   \"Combo1\"\r\n      Top             =   1680\r\n"
        + "      Width           =   1215\r\n   End\r\n";

    private const string Text1 =
        "   Begin VB.TextBox Text1 \r\n      Height          =   495\r\n      Left            =   1800\r\n"
        + "      TabIndex        =   0\r\n      Text            =   \"Text1\"\r\n      Top             =   1560\r\n"
        + "      Width           =   1215\r\n   End\r\n";

    private static (FormDefinition Form, string Saved) RoundTrip(string controls)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Head + controls + Tail, new NullSink())!;
        var (saved, _) = new FormSerializer().Serialize(form, "Form1.frm");
        return (form, saved);
    }

    private static ComponentInstance Named(FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    [Fact]
    public void The_form_VB6_s_designer_saved_is_written_back_block_for_block()
    {
        var (form, saved) = RoundTrip(Combo3 + Combo2 + Combo1 + Text1);

        saved.Should().Contain(Combo3 + Combo2 + Combo1 + Text1);
        Named(form, "Combo1").GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Combo1");
        Named(form, "Combo2").GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Combo2");
    }

    // Style is modelled (#1428): read as its value, not kept as a line, and written where VB6's designer writes it.
    [Fact]
    public void Style_is_read_as_its_value_and_written_back_as_VB6_wrote_it()
    {
        var (form, saved) = RoundTrip(Combo3 + Combo2 + Combo1);

        Named(form, "Combo3").GetPropertyOrDefault(VBProperties.ComboStyleProperty).Should().Be(VBComboStyle.DropdownList);
        Named(form, "Combo2").GetPropertyOrDefault(VBProperties.ComboStyleProperty).Should().Be(VBComboStyle.SimpleCombo);
        Named(form, "Combo1").GetPropertyOrDefault(VBProperties.ComboStyleProperty).Should().Be(VBComboStyle.DropdownCombo);
        form.Components.SelectMany(c => c.UnknownRawProperties).Should().NotContain(p => p.Name == "Style");
        saved.Should().Contain(Combo3 + Combo2 + Combo1);
    }

    // A Style set in the designer goes where VB6's designer put Combo2's, between Left and TabIndex, with its name; and
    // back to Dropdown Combo, which VB6 never writes, it goes.
    [Fact]
    public void A_Style_set_in_the_model_is_written_with_its_name_and_the_default_is_dropped()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var form = new FormDeserializer().Deserialize(project, Head + Combo1 + Tail, new NullSink())!;

        Named(form, "Combo1").SetProperty(VBProperties.ComboStyleProperty, VBComboStyle.SimpleCombo);
        var (simple, _) = new FormSerializer().Serialize(form, "Form1.frm");
        Named(form, "Combo1").SetProperty(VBProperties.ComboStyleProperty, VBComboStyle.DropdownCombo);
        var (dropdown, _) = new FormSerializer().Serialize(form, "Form1.frm");

        simple.Should().Contain("      Left            =   1800\r\n      Style           =   1  'Simple Combo\r\n      TabIndex        =   1\r\n");
        dropdown.Should().Contain(Combo1);
    }

    // VB6's designer never writes a Dropdown List's Text, and its run time ignores one, so a hand-written line goes.
    [Fact]
    public void A_Dropdown_List_s_Text_line_is_not_written_back()
    {
        var withText = Combo3.Replace("      TabIndex        =   3\r\n", "      TabIndex        =   3\r\n      Text            =   \"x\"\r\n");

        var (_, saved) = RoundTrip(withText);

        saved.Should().Contain(Combo3);
    }

    [Fact]
    public void A_combo_s_Text_outside_ASCII_is_written_back_as_read()
    {
        var accented = Combo1.Replace("\"Combo1\"", "\"Café £5\"");

        var (form, saved) = RoundTrip(accented);

        saved.Should().Contain(accented);
        Named(form, "Combo1").GetPropertyOrDefault(VBProperties.TextProperty).Should().Be("Café £5");
    }
}
