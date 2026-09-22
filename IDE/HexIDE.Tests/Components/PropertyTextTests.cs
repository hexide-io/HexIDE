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
}
