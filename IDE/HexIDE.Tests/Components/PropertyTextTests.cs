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

    // The toolbox's classes, and the two a form holds that it does not offer: the Line and the Menu.
    private static IEnumerable<IComponentClass> AllClasses() =>
        new HexIDE.IDE.ComponentRegistry().Components.Concat([LineComponentClass.Instance, MenuComponentClass.Instance]);

    public static TheoryData<string> Classes() => new(AllClasses().Select(c => c.VBTypeName));

    [Theory]
    [MemberData(nameof(Classes))]
    public void Every_row_marked_settable_sets_the_value_it_shows(string vbType)
    {
        var type = AllClasses().Single(c => c.VBTypeName == vbType);
        var control = new ComponentInstance(type, "Control1");

        foreach (var row in PropertyText.Rows(control).Where(r => r.Settable))
        {
            var property = type.Properties.Single(p => p.Name == row.Name);
            PropertyText.TryParse(property, row.Value, out var back).Should().BeTrue(row.Name);
            // A string or Tag never set reads as "" and sets "", which the window does too; nothing else may differ.
            var held = control.GetBoxedPropertyOrDefault(property);
            (held is null && property.PropertyType is var t && (t == typeof(string) || t == typeof(object)) ? "" : held)
                .Should().Be(back,
                $"{vbType}.{row.Name} shown as '{row.Value}' sets the value it holds");
        }
    }

    // ── what the window's rows alone would hide (#923 review) ───────────────────────────────────────────────────

    private sealed class NullSink : HexIDE.Runtime.Serialization.IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static HexIDE.Runtime.ProjectElements.FormDefinition Load(string controls) =>
        new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(
            new HexIDE.Runtime.ProjectElements.ProjectDefinition(HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n" + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;

    private static ComponentInstance Control(HexIDE.Runtime.ProjectElements.FormDefinition form, string name) =>
        form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == name);

    [Fact]
    public void A_value_the_file_holds_and_HexIDE_could_not_read_is_the_file_s_text_not_the_default()
    {
        // A palette colour (#786): the window shows the default, and setting what it shows would replace the file's.
        var label = Control(Load("   Begin VB.Label Label1\r\n      BackColor       =   &H02C0C0C0&\r\n   End\r\n"), "Label1");

        var row = PropertyText.Rows(label).Single(r => r.Name == "BackColor");

        row.Value.Should().Be("&H02C0C0C0&");
        row.Settable.Should().BeFalse();
        row.Note.Should().Contain("could not read");
    }

    [Fact]
    public void A_property_HexIDE_does_not_model_is_a_row_with_the_file_s_text()
    {
        var box = Control(Load("   Begin VB.TextBox Text1\r\n      MultiLine       =   -1  'True\r\n      Text            =   \"a\"\r\n   End\r\n"), "Text1");

        var rows = PropertyText.Rows(box);

        rows.Should().ContainSingle(r => r.Name == "MultiLine").Which.Should().Match<PropertyRow>(r =>
            r.Value == "-1  'True" && !r.Settable && r.Note!.Contains("not modelled"));
        rows.Select(r => r.Name).Should().BeInAscendingOrder(StringComparer.CurrentCulture, "as the window lists them, by name");
    }

    [Fact]
    public void A_control_array_s_element_is_named_by_its_index()
    {
        var form = Load("   Begin VB.Label lblTitle\r\n      Index           =   0\r\n   End\r\n"
                        + "   Begin VB.Label lblTitle\r\n      Index           =   1\r\n      Caption = \"second\"\r\n   End\r\n");

        PropertyText.Named(form.Components, "LBLTITLE").Should().HaveCount(2);
        var second = PropertyText.Named(form.Components, "lblTitle(1)").Should().ContainSingle().Subject;
        second.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("second");
        PropertyText.ArrayIndexOf(second).Should().Be(1);
        PropertyText.Named(form.Components, "lblTitle(2)").Should().BeEmpty();
    }

    [Fact]
    public void A_timer_s_size_and_visibility_are_not_settable_since_the_file_does_not_keep_them()
    {
        var rows = PropertyText.Rows(new ComponentInstance(TimerComponentClass.Instance, "Timer1")).ToDictionary(r => r.Name);

        foreach (var name in new[] { "Width", "Height", "Visible" })
            rows[name].Should().Match<PropertyRow>(r => !r.Settable && r.Note!.Contains("has no " + name));
        rows["Left"].Settable.Should().BeTrue("a Timer keeps its position");
    }

    [Fact]
    public void A_menu_has_no_bounds()
    {
        PropertyText.Rows(new ComponentInstance(MenuComponentClass.Instance, "mnuFile")).Select(r => r.Name)
            .Should().NotContain(["Left", "Top", "Width", "Height"]);
    }

    [Fact]
    public void A_font_s_other_weights_and_charset_are_shown()
    {
        PropertyText.Shown(VBProperties.FontProperty, new VBFont("Arial", 10, weight: 600, charset: 204))
            .Should().Be("Arial, 10 pt, weight 600, charset 204");
    }
}
