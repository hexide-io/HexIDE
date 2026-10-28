using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// A dump says what isHidden and isOffscreen mean when a node in it carries one, ahead of the tree (#2566). The description
/// used to explain both, and had no room for it.
/// </summary>
public class DumpFlagsNoteTests
{
    private static UiNode Node(string path, bool offscreen = false, bool? hidden = null, params UiNode[] children) =>
        new(path, "Pane", null, null, "Panel", null, [], true, offscreen, hidden, children);

    [Fact]
    public void A_tree_with_neither_flag_has_no_note()
    {
        HexIdeTools.FlagsNote([Node("Window", children: Node("Window/Button[OK]"))]).Should().BeNull();
    }

    [Fact]
    public void A_hidden_node_anywhere_below_brings_the_isHidden_explanation_alone()
    {
        var tree = Node("Window", children: Node("Window/Pane[#0]", children: Node("Window/Pane[#0]/Button[OK]", hidden: true)));

        var note = HexIdeTools.FlagsNote([tree]);

        note.Should().StartWith("isHidden true marks a control that is in the tree but does not show")
            .And.Contain("interact refuses all but invoke_command and set_property on it")
            .And.NotContain("isOffscreen");
    }

    [Fact]
    public void An_offscreen_match_brings_the_isOffscreen_explanation_alone()
    {
        var note = HexIdeTools.FlagsNote([Node("Window/Button[OK]"), Node("Window/ListBox[Files]/ListBoxItem[#40]", offscreen: true)]);

        note.Should().StartWith("isOffscreen true means a control is clipped or scrolled out of view")
            .And.NotContain("isHidden");
    }

    [Fact]
    public void Both_flags_bring_both_explanations_and_the_note_comes_before_the_tree()
    {
        var tree = Node("Window", children: [Node("Window/Button[A]", hidden: true), Node("Window/Button[B]", offscreen: true)]);
        var note = HexIdeTools.FlagsNote([tree]);

        var json = System.Text.Json.JsonSerializer.Serialize(
            new VisualTreeResult(null, "MainWindow", tree, FlagsNote: note), ReplyJson.Options);

        note.Should().Contain("isHidden true").And.Contain("isOffscreen true");
        json.IndexOf("\"flagsNote\"", StringComparison.Ordinal).Should().BeLessThan(json.IndexOf("\"root\"", StringComparison.Ordinal));
    }
}
