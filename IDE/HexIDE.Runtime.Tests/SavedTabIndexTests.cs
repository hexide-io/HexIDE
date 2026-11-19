using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Tests;

// Measured rows: https://github.com/hexide-io/HexIDE/issues/4843 (oracle PR #4873).
public class SavedTabIndexTests
{
    public static TheoryData<int[], int[]> Rows => new()
    {
        { [2, 0, 1], [2, 0, 1] },
        { [1, 1, 0], [1, 2, 0] },
        { [0, 5, 1], [0, 2, 1] },
        { [0, 0, 1], [0, 2, 1] },
        { [9, 7, 0], [1, 2, 0] },
        { [2, 2, 0, 9], [2, 1, 0, 3] },
        { [1, 1, 3, 9], [1, 0, 3, 2] },
        // Geometry row, with the absent value supplied as the class default.
        { [0, 0, 9, 9, 4, 0], [0, 1, 2, 3, 4, 5] },
        { [0, 1, 2, 3, 20, 25, 24, 23, 22, 21, 10, 11], Enumerable.Range(0, 12).ToArray() },
    };

    [Theory]
    [MemberData(nameof(Rows))]
    public void Saved_values_follow_the_measured_rule(int[] saved, int[] expected) =>
        SavedTabIndexes.Normalize(saved).Should().Equal(expected);

    private sealed class NullSink : IDeserializeErrorSink
    {
        public void LogError(string _) { }
    }

    private static FormDefinition Read(string controls) => new FormDeserializer().Deserialize(
        new ProjectDefinition(VBProjectType.EXE, "P"),
        "VERSION 5.00\r\nBegin VB.Form Form1\r\n   ClientHeight = 3600\r\n   ClientWidth = 4800\r\n"
        + controls + "End\r\nAttribute VB_Name = \"Form1\"\r\n", new NullSink())!;

    private static string Control(string type, string name, int? index, string children = "") =>
        $"   Begin {type} {name}\r\n      Height = 375\r\n      Width = 1200\r\n"
        + (index.HasValue ? $"      TabIndex = {index.Value}\r\n" : "") + children + "   End\r\n";

    private static int[] Indices(FormDefinition form) => form.Components
        .Where(c => c.BaseClass.Properties.Contains(VBProperties.TabIndexProperty))
        .Select(c => c.GetPropertyOrDefault(VBProperties.TabIndexProperty)).ToArray();

    [Fact]
    public void Designer_and_save_use_one_order_including_container_children()
    {
        int[] saved = [0, 1, 2, 3, 20, 25, 24, 23, 22, 21, 10, 11];
        var children = string.Concat(saved.Skip(1).Take(5).Select((v, i) => Control("VB.TextBox", $"Text{i}", v)));
        var controls = Control("VB.Frame", "Frame1", saved[0], children)
            + string.Concat(saved.Skip(6).Select((v, i) => Control("VB.TextBox", $"After{i}", v)));
        var form = Read(controls);
        Indices(form).Should().Equal(Enumerable.Range(0, 12));
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        PreservedControlText.TabIndexes(text).Should().Equal(Enumerable.Range(0, 12));
    }

    [Fact]
    public void Raw_ListView_TabIndex_participates_and_saves_its_edited_value()
    {
        var form = Read(Control("VB.TextBox", "Text1", 1)
            + Control("MSComctlLib.ListView", "ListView1", 1) + Control("VB.TextBox", "Text2", 0));
        var list = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "ListView1");
        list.UnknownRawProperties.Single(p => p.Name == "TabIndex").EditedValue.Should().Be("2");
        list.GetPropertyOrDefault(VBProperties.TabIndexProperty).Should().Be(2);
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        PreservedControlText.TabIndexes(text).Should().Equal(1, 2, 0);
    }

    [Theory]
    [InlineData(1, 1, 0)]
    [InlineData(9, 7, 0)]
    public void Changing_a_carried_index_leaves_the_whole_form_as_saved(int first, int carried, int last)
    {
        var form = Read(Control("VB.TextBox", "Text1", first)
            + Control("Unknown.Widget", "Carried1", carried) + Control("VB.TextBox", "Text2", last));
        Indices(form).Should().Equal(first, last);
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        PreservedControlText.TabIndexes(text).Should().Equal(first, carried, last);
    }

    [Fact]
    public void An_unchanged_carried_index_reserves_its_place()
    {
        var form = Read(Control("VB.TextBox", "Text1", 1)
            + Control("VB.Frame", "Frame1", 1, Control("Unknown.Widget", "Carried1", 0)));
        Indices(form).Should().Equal(1, 2);
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        PreservedControlText.TabIndexes(text).Should().Equal(1, 2, 0);
    }

    [Fact]
    public void Runtime_raw_number_reads_the_edited_TabIndex()
    {
        var control = new ComponentInstance(TextBoxComponentClass.Instance, "Text1");
        control.UnknownRawProperties.Add(new ComponentInstance.UnknownRawProperty("TabIndex", ["TabIndex = 1"])
            { EditedValue = "2" });
        HexIDE.Runtime.VBLoader.RawNumber(control, "TabIndex").Should().Be(2);
    }

    [Fact]
    public void A_TabIndex_line_on_a_class_VB6_gives_none_takes_no_number()
    {
        // Counted, the Shape's 0 would take Text1's place and push Text1 to 2. Left out, 0 and 1 are already VB6's numbers.
        var form = Read(Control("VB.Shape", "Shape1", 0) + Control("VB.TextBox", "Text1", 0) + Control("VB.TextBox", "Text2", 1));
        Indices(form).Should().Equal(0, 1);
        var shape = form.Components.Single(c => c.GetPropertyOrDefault(VBProperties.NameProperty) == "Shape1");
        shape.UnknownRawProperties.Single(p => p.Name == "TabIndex").EditedValue.Should().BeNull();
        var (text, _) = new FormSerializer().Serialize(form, "Form1.frm");
        // The save writes the Shape after the windowed TextBoxes, so compare the lines without their order.
        PreservedControlText.TabIndexes(text).Should().BeEquivalentTo([0, 0, 1]);
    }

    [Fact]
    public void An_absent_index_keeps_its_class_default()
    {
        var form = Read(Control("VB.TextBox", "Text1", 0) + Control("VB.TextBox", "Text2", null));
        Indices(form).Should().Equal(0, VBProperties.TabIndexProperty.DefaultValue(TextBoxComponentClass.Instance));
    }
}
