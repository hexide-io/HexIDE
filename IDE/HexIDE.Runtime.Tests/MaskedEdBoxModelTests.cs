using System;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class MaskedEdBoxModelTests
{
    [Fact]
    public void Defaults_are_the_measured_design_time_defaults()
    {
        var box = new MaskedEdBoxModel();
        box.Mask.Should().BeEmpty();
        box.Text.Should().BeEmpty();
        box.MaxLength.Should().Be(64);
        box.PromptChar.Should().Be("_");
        box.PromptInclude.Should().BeTrue();
        box.AllowPrompt.Should().BeFalse();
        box.AutoTab.Should().BeFalse();
        box.ClipMode.Should().Be(0);
    }

    [Theory]
    [InlineData(true, "12-34", "12-34", "1234", "12-34")]
    [InlineData(true, "1_-3_", "1_-3_", "13", "1 -3 ")]
    [InlineData(true, "__-__", "__-__", "", "  -  ")]
    [InlineData(false, "12-34", "1234", "1234", "12-34")]
    [InlineData(false, "13", "13", "13", "13-  ")]
    [InlineData(false, "1-3", "13", "13", "13-  ")]
    [InlineData(false, "1_-3_", "13", "13", "1 -3 ")]
    [InlineData(false, "", "", "", "  -  ")]
    public void Readings(bool include, string input, string text, string clip, string formatted)
    {
        var box = new MaskedEdBoxModel { Mask = "##-##", PromptInclude = include };
        var changes = 0;
        box.Changed += () => changes++;
        box.Text = input;
        changes.Should().Be(1);
        box.Text.Should().Be(text);
        box.ClipText.Should().Be(clip);
        box.FormattedText.Should().Be(formatted);
    }

    [Theory]
    [InlineData("999", "1 2", "1 2", "1 2")]
    [InlineData("AAA", "a1B", "a1B", "a1B")]
    [InlineData("aaa", "a b", "a b", "a b")]
    [InlineData("&&&", "+ !", "+ !", "+ !")]
    [InlineData("CCC", "+ !", "+ !", "+ !")]
    [InlineData(">??", "ab", "AB", "AB")]
    [InlineData("<??", "AB", "ab", "ab")]
    [InlineData("\\##", "#5", "#5", "5")]
    [InlineData("(###) ###-####", "(555) 123-4567", "(555) 123-4567", "5551234567")]
    [InlineData("##.##", "12.34", "12.34", "1234")]
    [InlineData("##:##", "12:34", "12:34", "1234")]
    [InlineData("##/##/####", "01/02/2003", "01/02/2003", "01022003")]
    [InlineData("#,###", "1,234", "1,234", "1234")]
    public void Mask_characters(string mask, string input, string expected, string clip)
    {
        var box = new MaskedEdBoxModel { Mask = mask, Text = input };
        box.Text.Should().Be(expected);
        box.ClipText.Should().Be(clip);
    }

    [Theory]
    [InlineData("##-##", "1a-34")]
    [InlineData("##-##", "12/34")]
    [InlineData("##-##", "1234")]
    [InlineData("##-##", "12-3")]
    [InlineData("##-##", "12-345")]
    [InlineData("##-##", "")]
    [InlineData("999", "1a2")]
    [InlineData("??", "a1")]
    [InlineData("AAA", "a b")]
    public void A_refused_assignment_keeps_the_text_and_still_fires_Change(string mask, string input)
    {
        var box = new MaskedEdBoxModel { Mask = mask };
        var original = box.Text;
        var changes = 0;
        box.Changed += () => changes++;
        Action assign = () => box.Text = input;
        assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
        box.Text.Should().Be(original);
        changes.Should().Be(1);
    }

    [Fact]
    public void Mask_assignment_fills_places_and_fires_Change()
    {
        var box = new MaskedEdBoxModel();
        var changes = 0;
        box.Changed += () => changes++;
        box.Mask = "##-##";
        box.Text.Should().Be("__-__");
        box.ClipText.Should().BeEmpty();
        box.FormattedText.Should().Be("  -  ");
        box.MaxLength.Should().Be(5);
        changes.Should().Be(1);
        box.PromptInclude = false;
        changes.Should().Be(1);
        box.PromptChar = "_";
        changes.Should().Be(2);
        box.FormattedText.Should().Be("  -  ");
    }

    [Fact]
    public void No_mask_assignment_can_exceed_MaxLength()
    {
        var box = new MaskedEdBoxModel { MaxLength = 5, Text = "abcdefgh" };
        box.Text.Should().Be("abcdefgh");
        box.MaxLength.Should().Be(8);
    }

    [Fact]
    public void Refused_long_text_changes_MaxLength()
    {
        var box = new MaskedEdBoxModel { Mask = "##-##" };
        Action assign = () => box.Text = "12-345";
        assign.Should().Throw<VBRunTimeException>();
        box.MaxLength.Should().Be(6);
    }

    [Fact]
    public void MaxLength_with_a_mask_can_be_shorter_than_Text()
    {
        var box = new MaskedEdBoxModel { Mask = "##-##", MaxLength = 3 };
        box.MaxLength.Should().Be(3);
        box.Text.Should().Be("__-__");
    }

    [Theory]
    [InlineData(0, 1, 3, "2-3")]
    [InlineData(1, 0, 5, "1234")]
    public void Selection_reads_with_ClipMode(int mode, int start, int length, string expected)
    {
        var box = new MaskedEdBoxModel { Mask = "##-##", Text = "12-34", ClipMode = mode, SelStart = start, SelLength = length };
        box.SelText.Should().Be(expected);
        box.SelStart = 1;
        box.SelLength = 99;
        box.SelLength.Should().Be(4);
        box.SelStart = 99;
        box.SelStart.Should().Be(5);
        box.SelLength.Should().Be(0);
    }

    [Theory]
    [InlineData("prompt-empty")]
    [InlineData("prompt-long")]
    [InlineData("length")]
    [InlineData("clip")]
    [InlineData("start")]
    [InlineData("selection")]
    public void Invalid_properties_raise_380(string property)
    {
        var box = new MaskedEdBoxModel();
        Action assign = property switch
        {
            "prompt-empty" => () => box.PromptChar = "",
            "prompt-long" => () => box.PromptChar = "ab",
            "length" => () => box.MaxLength = 0,
            "clip" => () => box.ClipMode = 2,
            "start" => () => box.SelStart = -1,
            _ => () => box.SelLength = -1,
        };
        assign.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(380);
    }

    [Fact]
    public void AllowPrompt_does_not_turn_a_prompt_into_ClipText()
    {
        var box = new MaskedEdBoxModel { Mask = "##-##", AllowPrompt = true, Text = "1_-34" };
        box.ClipText.Should().Be("134");
    }

    [Theory]
    [InlineData("replace-mask")]
    [InlineData("clear-mask")]
    [InlineData("prompt")]
    [InlineData("unicode")]
    [InlineData("mask-character")]
    [InlineData("escape")]
    [InlineData("negative-max")]
    [InlineData("clip-mode")]
    public void Unfinished_or_unmeasured_model_edges_refuse(string edge)
    {
        var box = new MaskedEdBoxModel();
        Action action = edge switch
        {
            "replace-mask" => () => { box.Text = "12-34"; box.Mask = "###"; },
            "clear-mask" => () => box.Mask = "",
            "prompt" => () => box.PromptChar = "?",
            "unicode" => () => { box.Mask = "?"; box.Text = "é"; },
            "mask-character" => () => box.Mask = "!",
            "escape" => () => box.Mask = "\\",
            "negative-max" => () => box.MaxLength = -1,
            _ => () => box.ClipMode = 3,
        };
        action.Should().Throw<NotImplementedException>();
        if (edge == "unicode")
        {
            var sign = new MaskedEdBoxModel { Mask = "9" };
            Action assignSign = () => sign.Text = "+";
            assignSign.Should().Throw<NotImplementedException>();
        }
    }

}
