using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class TreeNodeAutomationTests : BaseVBTestFixture
{
    private TreeNodes Seed()
    {
        var nodes = new TreeNodes();
        // Keep this a runtime cast so these tests also compile against the model before it had proxies.
        context.AllocVariable(rootEnv, "nodes", new Vb6Value((ICSharpProxy)(object)nodes));
        return nodes;
    }

    [Fact]
    public async Task Program_builds_and_walks_a_tree_by_key_and_index()
    {
        var model = Seed();
        await Run("""
            Dim r As Object, c As Object, n As Object
            Set r = nodes.Add(Key:="r", Text:="root")
            Set c = nodes.Add("r", 4, "c", "child")
            Set n = nodes.Add("c", , "n", "next")
            Debug.Print nodes.Count, TypeName(nodes), TypeName(r)
            Debug.Print c.Parent.Text, r.Child.Text, c.Next.Text, n.Previous.Text
            Debug.Print nodes(2).FullPath, nodes.Item("c").Index, r.Children
            Debug.Print c, VarType(c) = vbString, nodes(1.5).Text, nodes(2.5).Text
            """);
        AssertDebugLog([new((short)3), new("INodes"), new("INode"), new("root"), new("child"),
            new("next"), new("child"), new("root\\child"), new((short)2), new((short)2), new("child"), new(true), new("child"), new("child")]);
        model.Clear();
        for (var i = 0; i < 65536; i++) model.Add();
        debug.Clear();
        await Run("Debug.Print nodes.Count, nodes(32768&).Index, nodes(65536&).Index");
        AssertDebugLog([new(0), new(-32768), new(0)]);
        Func<Task> hintedIndex = () => Run("Debug.Print nodes$(1)");
        await hintedIndex.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Node_properties_and_ensure_visible_reach_the_model()
    {
        Seed();
        await Run("""
            Dim r As Object, c As Object
            Set r = nodes.Add(, , "r", "root")
            Set c = nodes.Add("r", 4, "c", "child")
            c.Text = "leaf"
            c.Key = "new"
            c.Checked = True
            Debug.Print c.Visible, c.EnsureVisible(), r.Expanded, c.Visible
            c.Selected = True
            c.Selected = False
            Debug.Print c.Selected, c.Checked, nodes.Item("new").Text
            """);
        AssertDebugLog([new(false), new(false), new(true), new(true), new(false), new(true), new("leaf")]);
    }

    [Fact]
    public async Task Set_parent_moves_subtree_and_root_stays_the_first_root()
    {
        Seed();
        await Run("""
            Dim a As Object, b As Object, c As Object
            Set a = nodes.Add(, , "a", "a")
            Set b = nodes.Add(, , "b", "b")
            Set c = nodes.Add("a", 4, "c", "c")
            Set c.Parent = b
            Debug.Print c.FullPath, c.Index, c.Root.Text, b.Child.Text
            On Error Resume Next
            Set b.Parent = c
            Debug.Print Err.Number
            Set c.Parent = Nothing
            Debug.Print Err.Number
            c.Parent = 1
            Debug.Print Err.Number
            c.Index = 9
            Debug.Print Err.Number
            Set c.Next = a
            Debug.Print Err.Number
            """);
        AssertDebugLog([new("b\\c"), new((short)3), new("a"), new("c"), new(35614L), new(35610L), new(438L), new(383L), new(383L)]);
        Func<Task> tag = () => Run("Dim n As Object: Set n = nodes.Add(): Set n.Tag = n");
        await tag.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Remove_and_clear_invalidate_held_nodes_and_release_keys()
    {
        Seed();
        await Run("""
            Dim r As Object, c As Object
            Set r = nodes.Add(, , "r")
            Set c = nodes.Add("r", 4)
            nodes.Remove "r"
            On Error Resume Next
            Debug.Print c.Text
            Debug.Print Err.Number
            Set r = nodes.Add(, , "r")
            Debug.Print r.Index
            nodes.Clear
            Debug.Print nodes.Count
            Debug.Print r.Text
            Debug.Print Err.Number
            """);
        AssertDebugLog([new(35605L), new((short)1), new((short)0), new(35605L)]);
    }

    [Fact]
    public async Task Add_and_lookup_raise_measured_errors()
    {
        Seed();
        await Run("""
            Dim n As Object
            Set n = nodes.Add(, , "key")
            On Error Resume Next
            Set n = nodes.Add(, , "key")
            Debug.Print Err.Number
            Set n = nodes.Add(, , "5")
            Debug.Print Err.Number
            Set n = nodes.Add("missing")
            Debug.Print Err.Number
            Set n = nodes.Add(999)
            Debug.Print Err.Number
            Set n = nodes.Add(, 5)
            Debug.Print Err.Number
            Set n = nodes.Add(, "x")
            Debug.Print Err.Number
            Set n = nodes.Add(, , , , 1)
            Debug.Print Err.Number
            nodes.Count = 10
            Debug.Print Err.Number
            """);
        AssertDebugLog([new(35602L), new(35603L), new(35601L), new(35600L), new(380L), new(13L), new(35613L), new(383L)]);
        Func<Task> selectedImage = () => Run("Dim n As Object: Set n = nodes.Add(, , , , , 1)");
        await selectedImage.Should().ThrowAsync<NotImplementedException>();
    }

    [Fact]
    public async Task Sorted_node_ignores_relationship_and_rename_does_not_resort()
    {
        Seed();
        await Run("""
            Dim r As Object, b As Object, a As Object
            Set r = nodes.Add(, , "r")
            Set b = nodes.Add("r", 4, "b", "b")
            r.Sorted = True
            Set a = nodes.Add("b", 0, "a", "a10")
            Debug.Print r.Child.Text, a.Next.Text
            a.Text = "Zeta"
            Debug.Print r.Child.Text
            r.Sorted = True
            Debug.Print r.Child.Text
            """);
        AssertDebugLog([new("a10"), new("b"), new("Zeta"), new("b")]);
    }

    [Fact]
    public async Task Numeric_text_assignment_uses_the_measured_string_conversion()
    {
        Seed();
        await Run("""
            Dim n As Object
            Set n = nodes.Add()
            n.Text = 42
            Debug.Print n.Text, TypeName(n.Text), n
            """);
        AssertDebugLog([new("42"), new("String"), new("42")]);
    }

    [Fact]
    public async Task Bold_defaults_false_and_accepts_true()
    {
        Seed();
        await Run("""
            Dim n As Object
            Set n = nodes.Add()
            Debug.Print n.Bold
            n.Bold = True
            Debug.Print n.Bold
            """);
        AssertDebugLog([new(false), new(true)]);
    }

    [Fact]
    public async Task Setting_a_node_tag_to_a_control_raises_35700()
    {
        Seed();
        context.AllocVariable(rootEnv, "control", new Vb6Value(new Avalonia.Controls.TextBox()));
        await Run("""
            Dim n As Object
            Set n = nodes.Add()
            On Error Resume Next
            Set n.Tag = control
            Debug.Print Err.Number, n.Tag
            """);
        AssertDebugLog([new(35700L), new("")]);
    }
}
