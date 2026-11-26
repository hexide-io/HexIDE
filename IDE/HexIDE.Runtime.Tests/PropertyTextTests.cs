using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.Tests;

public class PropertyTextTests
{
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

    private const string TwoElementArrayAndAButton =
        "   Begin VB.Label lblTitle\r\n      Index           =   3\r\n   End\r\n"
        + "   Begin VB.Label lblTitle\r\n      Index           =   1\r\n      Caption = \"second\"\r\n   End\r\n"
        + "   Begin VB.CommandButton cmdGo\r\n   End\r\n";

    private static readonly VBFont Tahoma = new("Tahoma", 9.75, bold: true, italic: true, charset: 204);

    private static VBFont Font(string text, VBFont? current = null)
    {
        PropertyText.TryParse(VBProperties.FontProperty, text, out var value, current ?? Tahoma).Should().BeTrue(text);
        return value.Should().BeOfType<VBFont>().Subject;
    }

    [Fact]
    public void A_label_s_rows_are_every_property_the_properties_window_shows()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");

        var rows = PropertyText.Rows(label);

        rows.Select(r => r.Name).Should().Equal(LabelComponentClass.Instance.Properties.Select(p => p.Name).Append("Index").OrderBy(n => n));
        // What get_form_controls left out, and the gap had to infer from a caption.
        rows.Select(r => r.Name).Should().Contain(["Alignment", "AutoSize", "WordWrap", "BorderStyle", "UseMnemonic", "Font"]);
    }

    // A position is shown as the caller says the Properties window shows it, with its unit, and stays settable: Core cannot
    // tell a container's ScaleMode or a form's frame, so get_form_controls passes them (#1636).
    [Fact]
    public void A_position_s_row_is_what_the_caller_says_the_window_shows()
    {
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetUntypedProperty(VBProperties.LeftProperty, 8d);

        var rows = PropertyText.Rows(label, (property, pixels) =>
            property == VBProperties.LeftProperty ? (pixels * 15, "twips") : null).ToDictionary(r => r.Name);

        rows["Left"].Should().Be(new PropertyRow("Left", "120", true, "in twips, as the Properties window shows it"));
        rows["Top"].Note.Should().BeNull("the caller said nothing of Top");
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
    public void A_value_the_file_holds_and_HexIDE_could_not_read_is_the_file_s_text_not_the_default()
    {
        // An invalid system colour: the window shows the default, and setting what it shows would replace the file's.
        var label = Control(Load("   Begin VB.Label Label1\r\n      BackColor       =   &H80000019&\r\n   End\r\n"), "Label1");

        var row = PropertyText.Rows(label).Single(r => r.Name == "BackColor");

        row.Value.Should().Be("&H80000019&");
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
    public void A_font_row_is_settable_only_when_its_text_sets_that_font_back()
    {
        // A face holding a comma, as a file may: "Foo, italic, 8 pt, regular, charset 0" parses, as an italic Foo, which is
        // not the font the control has.
        var label = new ComponentInstance(LabelComponentClass.Instance, "Label1");
        label.SetUntypedProperty(VBProperties.FontProperty, new VBFont("Foo, italic", 8));

        PropertyText.Rows(label).Single(r => r.Name == "Font").Settable.Should().BeFalse();
    }
}
