using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #1092 groups 1/2 and the return-type follow-up: native and p-code agree.
public class ImageComboModelTests : BaseVBTestFixture
{
    private readonly EventPump events = new();

    private ImageComboModel Load()
    {
        var model = new ImageComboModel(events, () => Task.CompletedTask);
        context.AllocVariable(rootEnv, "combo", new Vb6Value(model));
        return model;
    }

    [Fact]
    public async Task Defaults_and_no_default_member_match_the_probe()
    {
        Load();
        await Run("Debug.Print combo.Text, combo.SelectedItem Is Nothing, combo.Indentation, combo.Locked\n" +
            "Debug.Print combo.ComboItems.Count\nOn Error Resume Next\nv = combo\nDebug.Print Err.Number\n");
        AssertDebugLog(["", true, 0, false, new Vb6Value(0L), new Vb6Value(438L)]);
    }

    [Fact]
    public async Task Selection_sets_Text_and_Text_never_selects()
    {
        Load();
        await Run("Set a = combo.ComboItems.Add(, , \"Alpha\")\nSet b = combo.ComboItems.Add(, , \"Bravo\")\n" +
            "Set combo.SelectedItem = combo.ComboItems(2)\nDebug.Print combo.Text, b.Selected, a.Selected\n" +
            "a.Selected = True\nDebug.Print combo.Text, a.Selected, b.Selected\n" +
            "combo.SelectedItem = b\nDebug.Print combo.Text\n");
        Func<Task> writeWhileSelected = () => Run("combo.Text = \"Alpha\"\n");
        await writeWhileSelected.Should().ThrowAsync<NotImplementedException>();
        await Run("Set combo.SelectedItem = Nothing\nDebug.Print combo.Text\ncombo.Text = \"Alpha\"\n" +
            "Debug.Print combo.SelectedItem Is Nothing, combo.Text\n");
        AssertDebugLog(["Bravo", true, false, "Alpha", true, false, "Bravo", "Bravo", true, "Alpha"]);
    }

    [Fact]
    public async Task Selected_text_follows_item_and_removal_keeps_Text()
    {
        Load();
        await Run("Set a = combo.ComboItems.Add(, , \"Alpha\")\nSet combo.SelectedItem = a\n" +
            "a.Text = \"Alef\"\nDebug.Print combo.Text\ncombo.ComboItems.Remove 1\n" +
            "Debug.Print combo.SelectedItem Is Nothing, combo.Text\n" +
            "Set b = combo.ComboItems.Add(, , \"Bravo\")\nSet combo.SelectedItem = b\n" +
            "Set combo.SelectedItem = Nothing\ncombo.ComboItems.Clear\nDebug.Print combo.SelectedItem Is Nothing\n");
        AssertDebugLog(["Alef", true, "Alef", true]);
        await Run("Set b = combo.ComboItems.Add()\nSet combo.SelectedItem = b\n");
        Func<Task> clearSelected = () => Run("combo.ComboItems.Clear\n");
        await clearSelected.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Indentation_is_the_default_for_new_items_and_Locked_allows_code()
    {
        Load();
        await Run("combo.Indentation = 2\nSet a = combo.ComboItems.Add()\n" +
            "Set b = combo.ComboItems.Add(, , , , , 4)\nDebug.Print a.Indentation, b.Indentation\n" +
            "combo.Locked = 2\ncombo.Text = \"locked text\"\nDebug.Print combo.Locked, combo.Text\n" +
            "combo.Locked = 1\nDebug.Print combo.Locked\ncombo.Locked = -1\nDebug.Print combo.Locked\n" +
            "combo.Locked = True\nDebug.Print combo.Locked\ncombo.Locked = False\nDebug.Print combo.Locked\n");
        AssertDebugLog([2, 4, true, "locked text", true, true, true, false]);
        Func<Task> otherIndentation = () => Run("combo.Indentation = 3\n");
        await otherIndentation.Should().ThrowAsync<NotImplementedException>();
        Func<Task> longLocked = () => Run("combo.Locked = CLng(1)\n");
        await longLocked.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Numeric_SelectedItem_raises_type_mismatch()
    {
        Load();
        await Run("On Error Resume Next\ncombo.SelectedItem = 1\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(13L)]);
    }
}
