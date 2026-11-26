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

    // ── read back as the Properties window shows it, and set again from that text (#923) ────────────────────────

    [Fact]
    public void A_picture_is_shown_as_its_size_or_none()
    {
        PropertyText.Shown(VBProperties.PictureProperty, null).Should().Be("(None)");
        PropertyText.Shown(VBProperties.PictureProperty, new byte[] { 1, 2, 3 }).Should().Be("(3 bytes)");
    }

    // The text a picture with none is shown as clears one, on all four picture rows, and is the only text one takes (#2872).
    [Theory]
    [InlineData("Picture")]
    [InlineData("Icon")]
    [InlineData("MouseIcon")]
    [InlineData("DragIcon")]
    public void None_clears_a_picture_and_no_other_text_reads_as_one(string name)
    {
        var property = name switch
        {
            "Picture" => VBProperties.PictureProperty,
            "Icon" => VBProperties.IconProperty,
            "MouseIcon" => VBProperties.MouseIconProperty,
            _ => VBProperties.DragIconProperty,
        };

        PropertyText.TryParse(property, "(None)", out var cleared, current: new byte[] { 1, 2, 3 }).Should().BeTrue();
        cleared.Should().BeNull();
        PropertyText.TryParse(property, " (none) ", out _).Should().BeTrue("case and spaces make no difference to it");
        PropertyText.TryParse(property, "", out _).Should().BeFalse("empty is not what a row with no picture shows");
        PropertyText.TryParse(property, "pic.bmp", out _).Should().BeFalse("a picture is loaded through a dialog");
        PropertyText.Accepted(property).Should().StartWith("(None), which clears it");
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
            var property = PropertyText.InWindowOrder(type).Single(p => p.Name == row.Name);
            PropertyText.TryParse(property, row.Value, out var back).Should().BeTrue(row.Name);
            // A string or Tag never set reads as "" and sets "", which the window does too; nothing else may differ.
            var held = control.GetBoxedPropertyOrDefault(property);
            (held is null && property.PropertyType is var t && (t == typeof(string) || t == typeof(object)) ? "" : held)
                .Should().Be(back,
                $"{vbType}.{row.Name} shown as '{row.Value}' sets the value it holds");
        }
    }

    // ── what the window's rows alone would hide (#923 review) ───────────────────────────────────────────────────

    [Fact]
    public void The_tools_that_change_a_named_control_resolve_it_as_one()
    {
        // No test project references HexIDE.Desktop, so this reads the source: both took the first match by name.
        foreach (var tool in new[] { "set_control_property", "move_control" })
        {
            var body = ToolSource.Tools.Single(t => t.Name == tool).Body;
            body.Should().Contain("PropertyText.NamedOne(", tool);

            // The refusal is returned, inside the reply. Without that branch NamedOne's null read as "no control named",
            // which sent the caller looking for a control that exists, and every test still passed (#1322).
            body.Should().MatchRegex(
                @"PropertyText\.NamedOne\([^;]*\bout\s+var\s+(?<v>\w+)\s*\)\s*;\s*if\s*\(\s*\k<v>\s+is\s+not\s+null\s*\)\s*return\b[^;]*\{\s*\k<v>\s*\}",
                $"{tool} returns NamedOne's refusal, naming the array's indexes");

            // And the reply names the element it changed, as lblTitle(1), not as lblTitle.
            body.Should().Contain("PropertyText.AddressedName(", tool);
        }
    }

    [Fact]
    public void A_font_s_other_weights_and_charset_are_shown()
    {
        PropertyText.Shown(VBProperties.FontProperty, new VBFont("Arial", 10, weight: 600, charset: 204))
            .Should().Be("Arial, 10 pt, weight 600, charset 204");
    }

    // ── a font set from text, as get_form_controls shows it (#952) ───────────────────────────────────────────────

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
        Font("DejaVu Sans").Should().Be(new VBFont("DejaVu Sans", 9.75, bold: true, italic: true), "the face, as the gap sent it, with the charset VB6 gives a text face, 0");
        Font("12 pt").Should().Be(new VBFont("Tahoma", 12, bold: true, italic: true, charset: 204));
        Font("12").Should().Be(new VBFont("Tahoma", 12, bold: true, italic: true, charset: 204));
        Font("charset 0").Should().Be(new VBFont("Tahoma", 9.75, bold: true, italic: true));
    }

    [Theory]
    [InlineData("Wingdings", 2)]
    [InlineData("WEBDINGS", 2)]
    [InlineData("Symbol", 2)]
    [InlineData("Marlett", 2)]
    [InlineData("Terminal", 255)]
    [InlineData("modern", 255)]
    [InlineData("Roman", 255)]
    [InlineData("Script", 255)]
    [InlineData("Arial", 0)]
    public void A_new_face_without_a_charset_takes_the_charset_vb6_gives_it(string face, int charset)
    {
        // As FontName sets it, measured on vb6.exe: a symbol font at 0 is not drawn as itself, by GDI or by HexIDE (#1310).
        Font(face, current: new VBFont("Arial", 9.75)).Charset.Should().Be(charset);
        Font(face, current: new VBFont("Courier New", 9.75, charset: 204)).Charset.Should().Be(charset);
    }

    [Fact]
    public void The_same_face_in_another_case_is_the_same_face_and_keeps_its_charset()
    {
        // The comparison was ordinal, so "wingdings" on a control in Wingdings was a new face, at charset 0 (#1310).
        var wingdings = new VBFont("Wingdings", 12, charset: 2);

        Font("wingdings", current: wingdings).Should().Be(wingdings, "the face as the control spells it, and its charset");
        Font("Arial", current: wingdings).Charset.Should().Be(0, "a new face, which VB6 gives 0");
        Font("Wingdings, charset 0", current: new VBFont("Arial", 12)).Charset.Should().Be(0, "a charset given is the charset");
    }

    [Fact]
    public void The_font_text_form_says_a_new_face_takes_its_own_charset()
    {
        PropertyText.Accepted(VBProperties.FontProperty).Should().NotContain("takes charset 0")
            .And.Contain("Wingdings").And.Contain("charset 2").And.Contain("255");
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
        PropertyText.Accepted(VBProperties.PictureProperty).Should().StartWith("(None)").And.Contain("... button").And.Contain("answer_next_file_dialog");
        PropertyText.Accepted(VBProperties.OleObjectBlobProperty).Should().Contain("no automation route");
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

    [Fact]
    public void A_face_the_caller_knows_is_a_face_whatever_its_name_ends_in()
    {
        static bool Known(string face) => face == "Wingdings 2";

        PropertyText.TryParse(VBProperties.FontProperty, "Wingdings 2, 12 pt", out var value, Tahoma, Known).Should().BeTrue();
        value.Should().BeOfType<VBFont>().Which.FontFamilyName.Should().Be("Wingdings 2");

        PropertyText.TryParse(VBProperties.FontProperty, "Arial 12", out _, Tahoma, Known).Should().BeFalse("no face is named so");
        PropertyText.TryParse(VBProperties.FontProperty, "Tahoma bold", out _, Tahoma, Known).Should().BeFalse();

        // The tool asks the running program what FontName takes here.
        ToolSource.Tools.Single(t => t.Name == "set_control_property").Body
            .Should().MatchRegex(@"PropertyText\.TryParse\([^;]*RunningProgram\.Current\.CanNameFont\s*\)");
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
}
