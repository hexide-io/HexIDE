using System.Globalization;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Tests.Infrastructure;
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

    // ââ read back as the Properties window shows it, and set again from that text (#923) ââââââââââââââââââââââââ

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
        rows["Font"].Should().Be(new PropertyRow("Font", "Tahoma, 9.75 pt, bold, italic, charset 0", Settable: true));
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

    // ââ what the window's rows alone would hide (#923 review) âââââââââââââââââââââââââââââââââââââââââââââââââââ

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

    private const string TwoElementArrayAndAButton =
        "   Begin VB.Label lblTitle\r\n      Index           =   3\r\n   End\r\n"
        + "   Begin VB.Label lblTitle\r\n      Index           =   1\r\n      Caption = \"second\"\r\n   End\r\n"
        + "   Begin VB.CommandButton cmdGo\r\n   End\r\n";

    [Fact]
    public void A_tool_that_saves_reaches_one_element_by_its_index()
    {
        var form = Load(TwoElementArrayAndAButton);

        var element = PropertyText.NamedOne(form.Components, "lblTitle(1)", out var refusal);

        refusal.Should().BeNull();
        element!.GetPropertyOrDefault(VBProperties.CaptionProperty).Should().Be("second");
        PropertyText.AddressedName(element).Should().Be("lblTitle(1)");
    }

    [Fact]
    public void A_bare_array_name_is_refused_naming_its_indexes_rather_than_taken_as_its_first()
    {
        // set_control_property wrote to the first element, saved, and replied as though the caller's was set (#1252).
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.NamedOne(form.Components, "LBLTITLE", out var refusal).Should().BeNull();

        refusal.Should().Be("'LBLTITLE' names a control array of 2, Index 1, 3; name one element, such as lblTitle(1)");
    }

    [Fact]
    public void A_single_control_is_reached_by_its_name_and_a_miss_is_not_a_refusal()
    {
        var form = Load(TwoElementArrayAndAButton);

        var button = PropertyText.NamedOne(form.Components, "CMDGO", out var refusal);
        refusal.Should().BeNull();
        PropertyText.AddressedName(button!).Should().Be("cmdGo");

        PropertyText.NamedOne(form.Components, "lblMissing", out refusal).Should().BeNull();
        refusal.Should().BeNull("a miss is the caller's 'no control named', not an ambiguity");
    }

    [Fact]
    public void An_index_the_array_does_not_have_is_refused_naming_those_it_has()
    {
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.NamedOne(form.Components, "lblTitle(2)", out var refusal).Should().BeNull();

        refusal.Should().Be("'lblTitle(2)' names no element: lblTitle has Index 1, 3");
    }

    [Fact]
    public void Two_controls_of_one_name_without_indexes_are_refused_as_such()
    {
        var form = Load("   Begin VB.Label lblDup\r\n   End\r\n   Begin VB.Label lblDup\r\n   End\r\n");

        PropertyText.NamedOne(form.Components, "lblDup", out var refusal).Should().BeNull();

        refusal.Should().Be("'lblDup' names 2 controls, which do not each have an Index of their own to tell them apart");
    }

    [Fact]
    public void An_index_on_a_control_that_is_no_array_is_refused_as_such()
    {
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.NamedOne(form.Components, "cmdGo(0)", out var refusal).Should().BeNull();

        refusal.Should().Be("'cmdGo' is not a control array, so it takes no index; name it as cmdGo");
    }

    [Fact]
    public void A_tool_that_saves_is_told_which_indexes_there_are_when_its_index_is_too_big_for_an_int()
    {
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.NamedOne(form.Components, "lblTitle(99999999999)", out var refusal).Should().BeNull();

        refusal.Should().Be("'lblTitle(99999999999)' names no element: lblTitle has Index 1, 3");
    }

    [Fact]
    public void An_index_in_digits_other_than_ascii_names_nothing_and_is_not_read_as_an_element()
    {
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.NamedOne(form.Components, "lblTitle(١)", out var refusal).Should().BeNull();

        refusal.Should().BeNull();
    }

    [Fact]
    public void The_tools_that_change_a_named_control_resolve_it_as_one()
    {
        // No test project references HexIDE.Desktop, so this reads the source: both took the first match by name.
        foreach (var tool in new[] { "set_control_property", "move_control" })
            ToolSource.Tools.Single(t => t.Name == tool).Body.Should().Contain("PropertyText.NamedOne(", tool);
    }

    [Theory]
    [InlineData("lblTitle(99999999999)")] // too big for an int, which threw
    [InlineData("lblTitle(١)")] // an Arabic-Indic one, which the regex took as a digit and the parse threw on
    public void An_index_that_is_no_int_names_no_element_rather_than_throwing(string name)
    {
        var form = Load(TwoElementArrayAndAButton);

        PropertyText.Named(form.Components, name).Should().BeEmpty();
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

    // ââ a font set from text, as get_form_controls shows it (#952) âââââââââââââââââââââââââââââââââââââââââââââââ

    private static readonly VBFont Tahoma = new("Tahoma", 9.75, bold: true, italic: true, charset: 204);

    private static VBFont Font(string text, VBFont? current = null)
    {
        PropertyText.TryParse(VBProperties.FontProperty, text, out var value, current ?? Tahoma).Should().BeTrue(text);
        return value.Should().BeOfType<VBFont>().Subject;
    }

    [Fact]
    public void A_font_s_shown_text_sets_that_font()
    {
        var fonts = new[]
        {
            Tahoma, VBFont.Default, new VBFont("Arial", 10, weight: 600, underline: true, strikethrough: true),
            new VBFont("Courier New", 12, charset: 2),
        };
        foreach (var font in fonts)
            Font(PropertyText.Shown(VBProperties.FontProperty, font), current: new VBFont("Wingdings", 30, bold: true, charset: 2))
                .Should().Be(font, "every part is in the text, a charset of 0 too");
    }

    [Fact]
    public void A_font_given_in_part_keeps_the_rest()
    {
        Font("DejaVu Sans").Should().Be(new VBFont("DejaVu Sans", 9.75, bold: true, italic: true), "the face, as the gap sent it, with the charset a new face takes, as the Font dialog gives it");
        Font("12 pt").Should().Be(new VBFont("Tahoma", 12, bold: true, italic: true, charset: 204));
        Font("12").Should().Be(new VBFont("Tahoma", 12, bold: true, italic: true, charset: 204));
        Font("charset 0").Should().Be(new VBFont("Tahoma", 9.75, bold: true, italic: true));
    }

    [Fact]
    public void Style_words_given_are_the_whole_style()
    {
        Font("underline").Should().Be(new VBFont("Tahoma", 9.75, underline: true, charset: 204), "bold and italic are not given");
        Font("Tahoma, regular").Should().Be(new VBFont("Tahoma", 9.75, charset: 204));
        Font("BOLD").Weight.Should().Be(VBFont.BoldWeight);
        Font("weight 300").Should().Match<VBFont>(f => f.Weight == 300 && !f.Italic);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Tahoma, ")]
    [InlineData("Tahoma, 0 pt")]
    [InlineData("Tahoma, -3 pt")]
    [InlineData("Tahoma, bold, Arial")]
    [InlineData("Tahoma, weight 0")]
    [InlineData("Tahoma, charset 300")]
    // The review's: digits the invariant parse threw on, or beyond an int.
    [InlineData("Tahoma, ８ pt")]
    [InlineData("Tahoma, ٣ pt")]
    [InlineData("Tahoma, weight 99999999999")]
    [InlineData("Tahoma, charset 99999999999")]
    // A part given twice, as a comma decimal splits a size into two.
    [InlineData("Tahoma, 9,75 pt")]
    [InlineData("Tahoma, 10 pt, 12 pt")]
    [InlineData("Tahoma, bold, weight 300")]
    [InlineData("Tahoma, bold, regular")]
    [InlineData("Tahoma, italic, italic")]
    [InlineData("Tahoma, charset 0, charset 2")]
    // A size or a style mistyped as the first part, which was taken for a face.
    [InlineData("bold italic")]
    [InlineData("Arial 12")]
    [InlineData("Tahoma bold")]
    [InlineData("12px")]
    [InlineData("12 points")]
    [InlineData("-3 pt")]
    [InlineData(".5 pt")]
    [InlineData("+12 pt")]
    [InlineData("1e2 pt")]
    // Beyond VB6: past 2160 points, finer than four decimals, a face GDI or the .frm cannot hold.
    [InlineData("Tahoma, 2161 pt")]
    [InlineData("Tahoma, 99999999999999999999 pt")]
    [InlineData("Tahoma, 9.12345 pt")]
    [InlineData("Ta\"homa")]
    [InlineData("Tahoma\nEnd")]
    [InlineData("A Face Name Longer Than Thirty One")]
    public void A_font_text_that_does_not_say_one_font_is_refused(string text)
    {
        PropertyText.TryParse(VBProperties.FontProperty, text, out _, Tahoma).Should().BeFalse();
    }

    [Fact]
    public void A_refusal_names_the_text_a_font_and_a_picture_take()
    {
        PropertyText.Accepted(VBProperties.FontProperty).Should().Contain("\"Tahoma, 9.75 pt, bold, italic, charset 0\"").And.Contain("regular");
        PropertyText.Accepted(VBProperties.PictureProperty).Should().StartWith("no text").And.Contain("no automation route");
    }

    [Theory]
    [InlineData("8514oem")]
    [InlineData("Segoe UI Light")]
    [InlineData("Arial Black")]
    [InlineData("Bold Font Name")]
    public void A_face_that_starts_with_a_digit_or_holds_a_style_word_is_still_a_face(string face)
    {
        Font(face).FontFamilyName.Should().Be(face);
    }

    [Theory]
    [InlineData("Wingdings 2")]
    [InlineData("Arial Rounded MT Bold")]
    [InlineData("MS Shell Dlg 2")]
    public void A_face_ending_in_a_number_or_style_word_round_trips(string face)
    {
        // The guess that a first part was a size or a style typed without its comma took these real faces for one, so
        // get_form_controls showed a font that set_control_property refused, in a row marked not settable (#1311).
        var font = new VBFont(face, 10, charset: 2);
        Font(PropertyText.Shown(VBProperties.FontProperty, font), current: font).Should().Be(font);

        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetUntypedProperty(VBProperties.FontProperty, font);
        PropertyText.Rows(label).Single(r => r.Name == "Font").Settable.Should().BeTrue();
    }

    [Fact]
    public void A_face_the_caller_knows_is_a_face_whatever_its_name_ends_in()
    {
        static bool Known(string face) => face == "Wingdings 2";

        PropertyText.TryParse(VBProperties.FontProperty, "Wingdings 2, 12 pt", out var value, Tahoma, Known).Should().BeTrue();
        value.Should().BeOfType<VBFont>().Which.FontFamilyName.Should().Be("Wingdings 2");

        PropertyText.TryParse(VBProperties.FontProperty, "Arial 12", out _, Tahoma, Known).Should().BeFalse("no face is named so");
        PropertyText.TryParse(VBProperties.FontProperty, "Tahoma bold", out _, Tahoma, Known).Should().BeFalse();

        // No test project references HexIDE.Desktop, so this reads the source: the tool asks what FontName takes here.
        ToolSource.Tools.Single(t => t.Name == "set_control_property").Body
            .Should().MatchRegex(@"PropertyText\.TryParse\([^;]*VBFontExtensions\.IsNameable\s*\)");
    }

    [Theory]
    [InlineData("Arial 12", "a size")]
    [InlineData("Arial 12pt", "a size")]
    [InlineData("Tahoma bold", "a style word")]
    public void A_face_followed_by_a_size_or_style_without_its_comma_is_refused_saying_so(string text, string what)
    {
        // The refusal described the text form the caller had sent, and never said which rule it broke (#1311).
        PropertyText.TryParse(VBProperties.FontProperty, text, out _, out var reason, Tahoma).Should().BeFalse();

        reason.Should().Contain($"'{text}'").And.Contain(what).And.Contain("comma").And.Contain("installed");
    }

    [Fact]
    public void The_font_text_form_says_when_a_face_ending_in_a_number_is_one()
    {
        PropertyText.Accepted(VBProperties.FontProperty).Should().Contain("Wingdings 2").And.Contain("installed");
    }

    [Fact]
    public void A_reply_shows_the_whole_font_not_its_face()
    {
        PropertyText.Display(Tahoma).Should().Be("Tahoma, 9.75 pt, bold, italic, charset 204");
    }

    [Fact]
    public void A_font_row_is_settable_only_when_its_text_sets_that_font_back()
    {
        // A face holding a comma, as a file may: "Foo, italic, 8 pt, regular, charset 0" parses, as an italic Foo, which is
        // not the font the control has.
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetUntypedProperty(VBProperties.FontProperty, new VBFont("Foo, italic", 8));

        PropertyText.Rows(label).Single(r => r.Name == "Font").Settable.Should().BeFalse();
    }
}
