using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// A dump too large for a client to take inline is cut at the deepest level that fits, and says so (#1982). The whole IDE
/// window was 66,000 to 84,000 characters, which Claude Code saved to a file rather than show.
/// </summary>
public class DumpFitsReplyTests
{
    private static UiNode Node(string path, params UiNode[] children) =>
        new(path, "Pane", null, null, "Panel", null, [], true, false, null, children);

    // A root, 20 panes and 40 leaves under each, with paths long enough that the leaves alone pass the budget.
    private static UiNode Wide() => Node("Window", [.. Enumerable.Range(0, 20).Select(i =>
        Node($"Window/Pane[#{i}]", [.. Enumerable.Range(0, 40).Select(j =>
            Node($"Window/Pane[#{i}]/Custom[SomeFairlyLongControlName]/Pane[#{j}]"))]))]);

    private static IEnumerable<UiNode> Flat(UiNode n) => new[] { n }.Concat(n.Children.SelectMany(Flat));

    [Fact]
    public void A_tree_that_fits_comes_back_whole_with_no_note()
    {
        var tree = Node("Window", Node("Window/Button[OK]"));

        var (fitted, note) = HexIdeTools.FitToReply(tree, 20);

        fitted.Should().BeSameAs(tree);
        note.Should().BeNull();
    }

    [Fact]
    public void A_tree_too_large_is_cut_where_it_fits_and_marks_what_holds_more()
    {
        var (fitted, note) = HexIdeTools.FitToReply(Wide(), 20);

        System.Text.Json.JsonSerializer.Serialize(fitted, ReplyJson.Options).Length
            .Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        fitted.Children.Should().HaveCount(20);
        fitted.Children.Should().AllSatisfy(c =>
        {
            c.Children.Should().BeEmpty();
            c.Truncated.Should().BeTrue();
        });
        note.Should().StartWith("The tree to maxDepth 20 is about ")
            .And.Contain("so it was cut at maxDepth 1, about ")
            .And.Contain("dump again with its path as 'root'");
    }

    [Fact]
    public void A_cut_keeps_a_leaf_a_leaf()
    {
        var tree = Node("Window", [Node("Window/Button[OK]"), .. Wide().Children]);

        var (fitted, _) = HexIdeTools.FitToReply(tree, 20);

        fitted.Children[0].Truncated.Should().BeNull("a node with nothing below it has nothing cut away");
    }

    [Fact]
    public void A_node_with_too_many_children_says_it_is_still_over()
    {
        var tree = Node("Window", [.. Enumerable.Range(0, 800).Select(i =>
            Node($"Window/Pane[#{i}]/Custom[SomeFairlyLongControlName]", Node("leaf")))]);

        var (fitted, note) = HexIdeTools.FitToReply(tree, 20);

        Flat(fitted).Should().NotContain(n => n.Path == "leaf");
        note.Should().Contain("cut at maxDepth 1").And.EndWith("scope it with 'root'.");
    }

    [Fact]
    public void A_node_with_only_children_is_not_said_to_be_cut()
    {
        var tree = Node("Window", [.. Enumerable.Range(0, 800).Select(i => Node($"Window/Pane[#{i}]/Custom[SomeFairlyLongControlName]"))]);

        var (fitted, note) = HexIdeTools.FitToReply(tree, 20);

        fitted.Should().BeSameAs(tree);
        note.Should().Contain("goes only one level down").And.NotContain("cut at");
    }
}
