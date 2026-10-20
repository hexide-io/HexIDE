using System.Globalization;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Components;

/// <summary>
/// A property value written as text by an automation caller is read in every spelling the Properties window shows
/// (#641). <c>set_control_property</c> refused every colour and enum, so a label could not be made opaque.
/// </summary>
public class PropertyTextTests
{
    [Theory]
    [InlineData("1")]
    [InlineData(" 1 ")]
    [InlineData("1 - Opaque")]
    [InlineData("Opaque")]
    [InlineData("opaque")]
    public void An_enum_is_read_by_number_by_name_or_as_the_dropdown_shows_it(string text)
    {
        PropertyText.TryParse(VBProperties.BackStyleProperty, text, out var value).Should().BeTrue();
        value.Should().Be(BackStyles.Opaque);
    }

    [Theory]
    [InlineData("Align Top")]
    [InlineData("aligntop")]
    [InlineData("vbAlignTop")]
    public void An_enum_is_read_by_the_name_VB6_shows_and_by_its_member_name(string text)
    {
        PropertyText.TryParse(VBProperties.AlignProperty, text, out var value).Should().BeTrue();
        value.Should().Be(VBAlign.vbAlignTop);
    }

    [Theory]
    [InlineData("7")]
    [InlineData("Translucent")]
    [InlineData("")]
    public void An_enum_value_that_is_not_a_member_is_refused(string text)
    {
        PropertyText.TryParse(VBProperties.BackStyleProperty, text, out _).Should().BeFalse();
    }

    [Fact]
    public void A_colour_is_read_as_the_properties_window_shows_it()
    {
        PropertyText.TryParse(VBProperties.BackColorProperty, "&H00C0FFC0&", out var value).Should().BeTrue();
        value.Should().BeOfType<VBColor>().Which.ToString().Should().Be("&H00C0FFC0&");
    }

    [Fact]
    public void A_colour_that_is_not_a_vb6_literal_is_refused()
    {
        PropertyText.TryParse(VBProperties.BackColorProperty, "red", out _).Should().BeFalse();
    }

    [Fact]
    public void A_number_is_read_with_a_point_whatever_the_machine_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            PropertyText.TryParse(VBProperties.LeftProperty, "1.5", out var value).Should().BeTrue();
            value.Should().Be(1.5, "a caller's string is not typed in the machine's locale, where '1.5' reads as 15");
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    [Fact]
    public void A_bool_and_an_int_still_parse()
    {
        PropertyText.TryParse(VBProperties.VisibleProperty, "False", out var visible).Should().BeTrue();
        visible.Should().Be(false);
        PropertyText.TryParse(VBProperties.TabIndexProperty, "3", out var tab).Should().BeTrue();
        tab.Should().Be(3);
    }

    [Fact]
    public void A_refusal_names_every_member_of_an_enum()
    {
        PropertyText.Accepted(VBProperties.BackStyleProperty).Should()
            .StartWith("one of 0 (Transparent), 1 (Opaque)").And.Contain("\"1 - Opaque\"");
    }

    [Fact]
    public void A_refusal_for_a_colour_shows_the_literal_form()
    {
        PropertyText.Accepted(VBProperties.BackColorProperty).Should().Contain("&H00C0FFC0&");
    }

    [Fact]
    public void A_stored_value_is_shown_as_a_reply_should_show_it()
    {
        // set_control_property reports what the property holds afterwards, which is not always what was sent (#655).
        PropertyText.Display(BackStyles.Opaque).Should().Be("1 (Opaque)");
        PropertyText.Display(VBAlign.vbAlignTop).Should().Be("1 (Align Top)");
        PropertyText.Display("Form1").Should().Be("'Form1'");
        PropertyText.Display(1.5).Should().Be("1.5");
        PropertyText.Display(true).Should().Be("True");
        PropertyText.Display(null).Should().Be("(none)");
    }

    // ── read back as the Properties window shows it, and set again from that text (#923) ────────────────────────

    [Fact]
    public void A_label_s_rows_are_every_property_the_properties_window_shows()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");

        var rows = PropertyText.Rows(label);

        rows.Select(r => r.Name).Should().Equal(LabelComponentClass.Instance.Properties.Select(p => p.Name).OrderBy(n => n));
        // What get_form_controls left out, and the gap had to infer from a caption.
        rows.Select(r => r.Name).Should().Contain(["Alignment", "AutoSize", "WordWrap", "BorderStyle", "UseMnemonic", "Font"]);
    }

    [Fact]
    public void A_value_is_shown_as_its_row_shows_it()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetUntypedProperty(VBProperties.BackStyleProperty, BackStyles.Opaque);
        label.SetUntypedProperty(VBProperties.BackColorProperty, VBColor.FromColor(0xC0, 0xFF, 0xC0));
        label.SetUntypedProperty(VBProperties.FontProperty, new VBFont("Tahoma", 9.75, bold: true, italic: true));

        var rows = PropertyText.Rows(label).ToDictionary(r => r.Name);

        rows["BackStyle"].Should().Be(new PropertyRow("BackStyle", "1 - Opaque", Settable: true));
        rows["BackColor"].Should().Be(new PropertyRow("BackColor", "&H00C0FFC0&", Settable: true));
        rows["(Name)"].Should().Be(new PropertyRow("(Name)", "Label1", Settable: true));
        rows["Font"].Should().Be(new PropertyRow("Font", "Tahoma, 9.75 pt, bold, italic", Settable: false),
            "a font is set through its dialog, so its text is to read, not to send back");
    }

    [Fact]
    public void A_picture_is_shown_as_its_size_or_none()
    {
        PropertyText.Shown(VBProperties.PictureProperty, null).Should().Be("(None)");
        PropertyText.Shown(VBProperties.PictureProperty, new byte[] { 1, 2, 3 }).Should().Be("(3 bytes)");
        PropertyText.TryParse(VBProperties.PictureProperty, "(None)", out _).Should().BeFalse("a picture is loaded through a dialog");
    }

    [Fact]
    public void A_number_is_shown_with_a_point_whatever_the_machine_culture()
    {
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");
            PropertyText.Shown(VBProperties.LeftProperty, 1.5).Should().Be("1.5");
        }
        finally { CultureInfo.CurrentCulture = before; }
    }

    public static TheoryData<string> Classes() =>
        new(new HexIDE.IDE.ComponentRegistry().Components.Select(c => c.VBTypeName));

    [Theory]
    [MemberData(nameof(Classes))]
    public void Every_row_marked_settable_sets_the_value_it_shows(string vbType)
    {
        var type = new HexIDE.IDE.ComponentRegistry().Components.Single(c => c.VBTypeName == vbType);
        var control = new ComponentInstance(type, "Control1");

        foreach (var row in PropertyText.Rows(control).Where(r => r.Settable))
        {
            var property = type.Properties.Single(p => p.Name == row.Name);
            PropertyText.TryParse(property, row.Value, out var back).Should().BeTrue(row.Name);
            (back ?? "").Should().Be(control.GetBoxedPropertyOrDefault(property) ?? "",
                $"{vbType}.{row.Name} shown as '{row.Value}' sets the value it holds");
        }
    }
}
