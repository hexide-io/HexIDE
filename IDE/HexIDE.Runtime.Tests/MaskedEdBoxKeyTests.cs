using System.Collections.Generic;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

public class MaskedEdBoxKeyTests
{
    public static IEnumerable<object[]> CharacterRows()
    {
        var masks = new[] { "#", "9", "?", "A", "a", "&", "C" };
        var accepted = new[] { "5", "5 +", "aA", "5aA", "5aA ", "5aA +", "5aA +" };
        for (var i = 0; i < masks.Length; i++)
            foreach (var key in "5aA +_")
                yield return new object[] { masks[i], key, accepted[i].Contains(key) };
    }

    [Theory]
    [MemberData(nameof(CharacterRows))]
    public void Every_mask_character_against_every_kind_of_key(string mask, char key, bool accepted)
    {
        var box = new MaskedEdBoxModel { Mask = mask };
        var events = new List<string>();
        box.Changed += () => events.Add("Change");
        box.ValidationError += (text, at) => events.Add($"VE {text} {at}");
        box.TypeCharacter(key);
        box.Text.Should().Be(accepted ? key.ToString() : "_");
        box.SelStart.Should().Be(accepted ? 1 : 0);
        box.SelLength.Should().Be(0);
        events.Should().Equal(accepted ? "Change" : $"VE {key} 0");
    }

    [Theory]
    [InlineData("#####", "12___", 0, 0, '9', "912__", 1, "Change")]
    [InlineData("#####", "1_3__", 0, 0, '9', "913__", 1, "Change")]
    [InlineData("#####", "12345", 0, 0, '9', "12345", 0, "VE 91234 5")]
    [InlineData("##-##", "12-34", 2, 0, '9', "12-34", 2, "VE 12-93 5")]
    [InlineData("##-##", "12-34", 2, 0, '-', "12-34", 4, "Change")]
    [InlineData("##-##", "1_-__", 1, 0, '-', "1_-__", 1, "VE 1--__ 1")]
    [InlineData("##-##", "12-34", 5, 0, '9', "12-34", 5, "VE 12-34 5")]
    [InlineData("##-##", "12-34", 1, 3, '9', "19-4_", 3, "Change")]
    [InlineData("##-##", "12-34", 0, 5, '9', "9_-__", 1, "Change")]
    [InlineData("##-##", "12-34", 0, 5, 'a', "12-34", 0, "VE a_-__ 0")]
    [InlineData("\\##", "#_", 0, 0, '5', "#5", 2, "Change")]
    [InlineData("\\##", "#_", 0, 0, '#', "#_", 1, "Change")]
    [InlineData(">?", "_", 0, 0, 'a', "A", 1, "Change")]
    [InlineData(">?", "_", 0, 0, 'A', "A", 1, "Change")]
    [InlineData("<?", "_", 0, 0, 'a', "a", 1, "Change")]
    [InlineData("<?", "_", 0, 0, 'A', "a", 1, "Change")]
    [InlineData(">A", "_", 0, 0, 'a', "A", 1, "Change")]
    [InlineData("<a", "_", 0, 0, 'A', "a", 1, "Change")]
    [InlineData(">&", "_", 0, 0, 'a', "A", 1, "Change")]
    [InlineData(">?", "_", 0, 0, '5', "_", 0, "VE 5 0")]
    [InlineData(">A", "_", 0, 0, '5', "5", 1, "Change")]
    public void Typing_rows(string mask, string before, int start, int length, char key, string after, int caret, string expectedEvent)
    {
        var box = new MaskedEdBoxModel { Mask = mask, Text = before, SelStart = start, SelLength = length };
        var events = new List<string>();
        box.Changed += () => events.Add("Change");
        box.ValidationError += (text, at) => events.Add($"VE {text} {at}");
        box.TypeCharacter(key);
        box.Text.Should().Be(after);
        box.SelStart.Should().Be(caret);
        box.SelLength.Should().Be(expectedEvent.StartsWith("VE") ? length : 0);
        events.Should().Equal(expectedEvent);
    }

    [Theory]
    [InlineData("##-##", "12-34", 5, 0, true, "12-3_", 4, "Change")]
    [InlineData("##-##", "12-34", 4, 0, true, "12-4_", 3, "Change")]
    [InlineData("##-##", "12-34", 3, 0, true, "13-4_", 1, "Change")]
    [InlineData("##-##", "12-34", 2, 0, true, "13-4_", 1, "Change")]
    [InlineData("##-##", "12-34", 1, 0, true, "23-4_", 0, "Change")]
    [InlineData("##-##", "12-34", 0, 0, true, "12-34", 0, "")]
    [InlineData("#####", "12345", 2, 0, true, "1345_", 1, "Change")]
    [InlineData("##-##", "12-34", 0, 0, false, "23-4_", 0, "Change")]
    [InlineData("##-##", "12-34", 1, 0, false, "13-4_", 1, "Change")]
    [InlineData("##-##", "12-34", 3, 0, false, "12-4_", 3, "Change")]
    [InlineData("#####", "12345", 1, 0, false, "1345_", 1, "Change")]
    [InlineData("##-##", "1_-34", 0, 0, false, "34-__", 0, "Change")]
    [InlineData("##-##", "12-34", 2, 0, false, "12-34", 2, "VE 123__ 2")]
    [InlineData("?#?#", "a1b2", 0, 0, false, "a1b2", 0, "VE 1___ 0")]
    [InlineData("##-##", "12-34", 1, 3, true, "14-__", 1, "Change")]
    [InlineData("##-##", "12-34", 1, 3, false, "14-__", 1, "Change")]
    [InlineData("##-##", "12-34", 0, 5, false, "__-__", 0, "Change")]
    public void Removal_rows(string mask, string before, int start, int length, bool backspace, string after, int caret, string expectedEvent)
    {
        var box = new MaskedEdBoxModel { Mask = mask, Text = before, SelStart = start, SelLength = length };
        var events = new List<string>();
        box.Changed += () => events.Add("Change");
        box.ValidationError += (text, at) => events.Add($"VE {text} {at}");
        box.Remove(backspace);
        box.Text.Should().Be(after);
        box.SelStart.Should().Be(caret);
        box.SelLength.Should().Be(0);
        events.Should().Equal(expectedEvent == "" ? [] : new[] { expectedEvent });
    }

    [Theory]
    [InlineData("#")]
    [InlineData("&")]
    public void AllowPrompt_accepts_a_prompt_and_raises_Change(string mask)
    {
        var box = new MaskedEdBoxModel { Mask = mask, AllowPrompt = true };
        var changes = 0;
        box.Changed += () => changes++;
        box.TypeCharacter('_');
        box.Text.Should().Be("_");
        box.SelStart.Should().Be(1);
        changes.Should().Be(1);
    }

    [Fact]
    public void No_mask_inserts_and_respects_MaxLength_without_validation()
    {
        var box = new MaskedEdBoxModel { Text = "abc", SelStart = 1 };
        box.TypeCharacter('x');
        box.Text.Should().Be("axbc");
        box = new MaskedEdBoxModel { Text = "abc", SelStart = 1 };
        box.Remove(true);
        box.Text.Should().Be("bc");
        box = new MaskedEdBoxModel { MaxLength = 3 };
        var events = new List<string>();
        box.Changed += () => events.Add("Change");
        box.ValidationError += (_, _) => events.Add("VE");
        foreach (var key in "abcdef") box.TypeCharacter(key);
        box.Text.Should().Be("abc");
        box.SelStart.Should().Be(3);
        events.Should().Equal("Change", "Change", "Change");
    }
}
