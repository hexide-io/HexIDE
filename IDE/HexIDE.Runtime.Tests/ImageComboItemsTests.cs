using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #1092 group 1, issuecomment-6441888761: native and p-code agree.
public class ImageComboItemsTests : BaseVBTestFixture
{
    private ImageComboItems Load()
    {
        // The collection is the first slice of #1092 A; the control owner follows.
        var collection = new ImageComboItems();
        context.AllocVariable(rootEnv, "items", new Vb6Value(collection));
        return collection;
    }

    [Fact]
    public async Task Add_lookup_default_text_and_variant_tag_match_the_probe()
    {
        var collection = Load();
        await Run("Set a = items.Add(, \"a\", \"Alpha\")\nSet b = items.Add(, , \"Bravo\")\n" +
            "Set c = items.Add(1, \"c\", \"Charlie\")\nDebug.Print TypeName(items), TypeName(a)\n" +
            "v = items(1)\nDebug.Print v\na.Tag = 5\nDebug.Print a.Tag\n" +
            "a.Text = \"Alef\"\na.Key = \"alef\"\nDebug.Print items(\"alef\").Text\n" +
            "Set blank = items.Add()\nDebug.Print blank.Text, blank.Key\n");
        AssertDebugLog(["IComboItems", "IComboItem", "Charlie", 5, "Alef", "", ""]);
        collection.Count.Should().Be(4);
        collection.Index(collection.Item(new Vb6Value("alef"))).Should().Be(2);
    }

    [Fact]
    public async Task Remove_renumbers_held_items_and_clear_empties_collection()
    {
        var collection = Load();
        await Run("Set a = items.Add(, \"a\", \"Alpha\")\nSet b = items.Add(, \"b\", \"Bravo\")\n");
        var held = collection.Item(new Vb6Value("b"));
        await Run("items.Remove 1\n");
        collection.Index(held).Should().Be(1);
        collection.Count.Should().Be(1);
        await Run("items.Remove \"b\"\n");
        collection.Count.Should().Be(0);
        await Run("Set a = items.Add(, , \"x\", , , 3)\n");
        collection.Item(new Vb6Value(1)).Indentation.Should().Be(3);
        await Run("items.Clear\n");
        collection.Count.Should().Be(0);
    }

    [Theory]
    [InlineData("items.Add , \"a\", \"dup\"", 35602)]
    [InlineData("items.Add 0", 35600)]
    [InlineData("items.Add 9", 35600)]
    [InlineData("items.Add , \"1\"", 35603)]
    [InlineData("Set b = items(0)", 35600)]
    [InlineData("Set b = items(99)", 35600)]
    [InlineData("Set b = items(\"nope\")", 35601)]
    [InlineData("items.Remove 9", 35600)]
    [InlineData("items.Count = 3", 383)]
    [InlineData("a.Index = 2", 383)]
    [InlineData("a.Indentation = -1", 380)]
    [InlineData("a.Indentation = 32767", 380)]
    [InlineData("items.Add , , \"img\", 1", 35613)]
    public async Task Collection_errors_match_the_measurement(string statement, int number)
    {
        Load();
        await Run("Set a = items.Add(, \"a\", \"Alpha\")\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value((long)number)]);
    }

    [Fact]
    public async Task Measured_numeric_getters_keep_their_VB6_types()
    {
        Load();
        await Run("Debug.Print items.Count\nSet a = items.Add(, , , , , 3)\n" +
            "Debug.Print items.Count, a.Index, a.Indentation\nitems.Add 1\nDebug.Print a.Index\n");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(1L), new Vb6Value(1L), 3, new Vb6Value(2L)]);
    }

    [Fact]
    public async Task Keys_are_case_sensitive_for_lookup_add_rename_and_remove()
    {
        Load();
        await Run("Set a = items.Add(, \"abc\", \"Alpha\")\nOn Error Resume Next\n" +
            "Set b = items(\"ABC\")\nDebug.Print Err.Number\n" +
            "Set b = items.Add(, \"ABC\", \"Upper\")\nDebug.Print items(\"ABC\").Text\n" +
            "a.Key = \"XYZ\"\nSet c = items(\"xyz\")\nDebug.Print Err.Number\n" +
            "items.Remove \"xYz\"\nDebug.Print Err.Number\nDebug.Print items(\"XYZ\").Text\n");
        AssertDebugLog([new Vb6Value(35601L), "Upper", new Vb6Value(35601L), new Vb6Value(35601L), "Alpha"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a.Tag = Empty")]
    public async Task Tag_default_and_Empty_are_strings(string assignment)
    {
        Load();
        await Run("Set a = items.Add()\n" + assignment + "\nDebug.Print TypeName(a.Tag), VarType(a.Tag), a.Tag, IsEmpty(a.Tag)\n");
        AssertDebugLog(["String", new Vb6Value(8L), "", false]);
    }

    [Fact]
    public async Task Tag_can_hold_Nothing()
    {
        Load();
        await Run("Set a = items.Add()\nSet a.Tag = Nothing\nDebug.Print a.Tag Is Nothing\n");
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("items.Clear\nDebug.Print a.Text")]
    [InlineData("a.Indentation = 4.5")]
    [InlineData("a.Key = \"NaN\"")]
    [InlineData("Debug.Print a.Selected")]
    [InlineData("v = items")]
    [InlineData("Debug.Print VarType(items)")]
    [InlineData("Debug.Print a.Text(1)")]
    [InlineData("Set a.Tag = a")]
    [InlineData("Dim values(1)\na.Tag = values")]
    public async Task Unmeasured_or_remaining_members_refuse(string statement)
    {
        Load();
        await Run("Set a = items.Add(, \"a\", \"Alpha\")\n");
        await ((Func<Task>)(() => Run("On Error Resume Next\n" + statement + "\n")))
            .Should().ThrowAsync<NotImplementedException>();
    }
}
