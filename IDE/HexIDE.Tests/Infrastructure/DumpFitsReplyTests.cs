using HexIDE.Automation;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// A dump too large for a client to take inline is cut at the most levels down that fit, and says so (#1982). The whole IDE
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

    // Three children a level, six levels down: 3, 9, 27, 81, 243 and 729 nodes, so a cut at 3 fits and one at 4 does not.
    private static UiNode Deep(string path = "Window", int levels = 6) => levels == 0
        ? Node(path)
        : Node(path, [.. Enumerable.Range(0, 3).Select(i => Deep($"{path}/Pane[#{i}]", levels - 1))]);

    private static IEnumerable<(UiNode Node, int Level)> Flat(UiNode n, int level = 0) =>
        new[] { (n, level) }.Concat(n.Children.SelectMany(c => Flat(c, level + 1)));

    private static int Size(UiNode n) => System.Text.Json.JsonSerializer.Serialize(n, ReplyJson.Options).Length;

    [Fact]
    public void A_tree_that_fits_comes_back_whole_with_no_note()
    {
        var tree = Node("Window", Node("Window/Button[OK]"));

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        fitted.Should().BeSameAs(tree);
        note.Should().BeNull();
    }

    [Fact]
    public void A_deep_tree_is_cut_at_the_most_levels_that_fit()
    {
        var tree = Deep();
        Size(tree).Should().BeGreaterThan(HexIdeTools.TreeCharacterBudget, "the tree must be too large for this to test anything");

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        var levels = Flat(fitted).ToList();
        var cutAt = levels.Max(n => n.Level);
        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        Size(CutAt(tree, cutAt + 1)).Should().BeGreaterThan(HexIdeTools.TreeCharacterBudget, "one level more must not fit");
        cutAt.Should().BeGreaterThan(1, "the search must go deeper than one level when it can");
        levels.Where(n => n.Node.Truncated == true).Should().OnlyContain(n => n.Level == cutAt);
        levels.Where(n => n.Level == cutAt).Should().OnlyContain(n => n.Node.Truncated == true);
        note.Should().StartWith("This tree is about ").And.Contain($"so it was cut {cutAt} levels down, about ");
    }

    [Fact]
    public void A_wide_tree_is_cut_one_level_down_and_marks_what_holds_more()
    {
        var (fitted, note) = HexIdeTools.FitToReply(Wide());

        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        fitted.Children.Should().HaveCount(20);
        fitted.Children.Should().AllSatisfy(c =>
        {
            c.Children.Should().BeEmpty();
            c.Truncated.Should().BeTrue();
        });
        note.Should().Contain("so it was cut 1 level down, about ").And.Contain("dump again with its path as 'root'");
    }

    [Fact]
    public void A_cut_keeps_a_leaf_a_leaf()
    {
        var tree = Node("Window", [Node("Window/Button[OK]"), .. Wide().Children]);

        var (fitted, _) = HexIdeTools.FitToReply(tree);

        fitted.Children[0].Truncated.Should().BeNull("a node with nothing below it has nothing cut away");
    }

    [Fact]
    public void A_node_with_too_many_children_says_it_is_still_over()
    {
        var tree = Node("Window", [.. Enumerable.Range(0, 800).Select(i =>
            Node($"Window/Pane[#{i}]/Custom[SomeFairlyLongControlName]", Node("leaf")))]);

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        Flat(fitted).Should().NotContain(n => n.Node.Path == "leaf");
        note.Should().Contain("cut 1 level down").And.EndWith("scope it with 'root'.");
    }

    [Fact]
    public void A_node_with_only_children_is_not_said_to_be_cut()
    {
        var tree = Node("Window", [.. Enumerable.Range(0, 800).Select(i => Node($"Window/Pane[#{i}]/Custom[SomeFairlyLongControlName]"))]);

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        fitted.Should().BeSameAs(tree);
        note.Should().Contain("goes only one level down").And.NotContain("cut ");
    }

    [Fact]
    public void Hidden_children_are_cut_before_visible_depth()
    {
        var visible = Deep("Window/Document", 3);
        var hidden = Wide() with { Path = "Window/Hidden", IsHidden = true };
        var tree = Node("Window", Node("Window/Wrapper", hidden), visible);

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        fitted.Children[1].Should().BeEquivalentTo(visible);
        var keptHidden = fitted.Children[0].Children.Single();
        keptHidden.Path.Should().Be(hidden.Path);
        keptHidden.IsHidden.Should().BeTrue();
        keptHidden.Children.Should().BeEmpty();
        keptHidden.Truncated.Should().BeTrue();
        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        note.Should().Contain("children below hidden nodes were omitted").And.Contain("its path as 'root'");
        hidden.Children.Should().NotBeEmpty("fitting must not mutate the original dump");
    }

    [Fact]
    public void Depth_is_cut_after_hidden_children_when_the_visible_tree_is_still_large()
    {
        var tree = Node("Window", Wide() with { Path = "Window/Hidden", IsHidden = true }, Deep("Window/Document"));

        var (fitted, note) = HexIdeTools.FitToReply(tree);

        fitted.Children[0].Children.Should().BeEmpty();
        fitted.Children[0].Truncated.Should().BeTrue();
        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        Flat(fitted.Children[1]).Should().Contain(n => n.Node.Truncated == true);
        note.Should().Contain("children below hidden nodes were omitted").And.Contain("levels down");
    }

    [Fact]
    public void Active_document_keeps_its_depth_while_other_visible_branches_are_cut()
    {
        var document = Deep("Window/Host/Document", 4);
        var tree = Node("Window", Deep("Window/Tools"), Node("Window/Host", document));

        var (fitted, note) = HexIdeTools.FitToReply(tree, document.Path);

        Flat(fitted).Single(n => n.Node.Path == document.Path).Node.Should().BeEquivalentTo(document);
        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        note.Should().Contain("active document");
    }

    [Fact]
    public void An_active_document_too_large_to_fit_is_kept_and_the_note_says_so()
    {
        var document = Deep("Window/Host/Document");
        var tree = Node("Window", Node("Window/Host", document), Deep("Window/Tools"));

        var (fitted, note) = HexIdeTools.FitToReply(tree, document.Path);

        Flat(fitted).Single(n => n.Node.Path == document.Path).Node.Should().BeEquivalentTo(document);
        note.Should().Contain("active document").And.Contain("still over").And.Contain("scope it with 'root'");
    }

    // What is hidden inside the active document shows no more than anywhere else, and every form designer now carries a
    // hidden draw preview and rubber band in its tree (#2231): their children go first there too. The document's own node
    // is kept even when hidden, as the root of what was asked for.
    [Fact]
    public void Hidden_children_inside_the_active_document_are_cut_too()
    {
        var hidden = Wide() with { Path = "Window/Host/Document/Hidden", IsHidden = true };
        var document = Node("Window/Host/Document", hidden, Node("Window/Host/Document/Shown")) with { IsHidden = true };
        var tree = Node("Window", Node("Window/Host", document));

        var (fitted, note) = HexIdeTools.FitToReply(tree, document.Path);

        var keptDocument = Flat(fitted).Single(n => n.Node.Path == document.Path).Node;
        keptDocument.Children.Select(c => c.Path).Should().Equal(hidden.Path, "Window/Host/Document/Shown");
        var keptHidden = keptDocument.Children[0];
        keptHidden.Children.Should().BeEmpty();
        keptHidden.Truncated.Should().BeTrue();
        Size(fitted).Should().BeLessThanOrEqualTo(HexIdeTools.TreeCharacterBudget);
        note.Should().Contain("children below hidden nodes were omitted");
    }

    // The note comes before the tree, so a client that keeps only the start of a long reply still has it.
    [Fact]
    public void The_note_is_written_before_the_tree()
    {
        var json = System.Text.Json.JsonSerializer.Serialize(
            new VisualTreeResult(null, "MainWindow", Node("Window"), Note: "a note"), ReplyJson.Options);

        json.IndexOf("\"note\"", StringComparison.Ordinal).Should()
            .BeLessThan(json.IndexOf("\"root\"", StringComparison.Ordinal));
    }

    private static UiNode CutAt(UiNode node, int levels) => levels == 0
        ? node with { Children = [], Truncated = node.Children.Length > 0 ? true : null }
        : node with { Children = [.. node.Children.Select(c => CutAt(c, levels - 1))] };
}
