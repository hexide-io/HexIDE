using HexIDE.Lsp;

namespace HexIDE.Tests.LspClient;

public class HoverContentRendererTests
{
    // clangd hover text recorded in #4129.
    private const string ClangdHover = "### function `twice`\n---\n→ int\n\nParameters:\n- int n\n---\n```cpp\nint twice(int n)\n```";

    [Fact]
    public void Clangd_heading_is_a_separate_block_without_markers()
    {
        var blocks = HoverContentRenderer.Render(new("markdown", ClangdHover)).ToArray();
        blocks[0].Text.Should().Be("function `twice`");
        blocks[0].IsHeading.Should().BeTrue();
        for (var level = 1; level <= 6; level++)
        {
            var heading = HoverContentRenderer.Render(new("markdown", new string('#', level) + " title")).Single();
            heading.Text.Should().Be("title");
            heading.IsHeading.Should().BeTrue();
        }
    }

    [Fact]
    public void Clangd_breaks_are_separate_blocks_without_markers()
    {
        var blocks = HoverContentRenderer.Render(new("markdown", ClangdHover)).ToArray();
        blocks.Where(b => b.IsThematicBreak).Should().HaveCount(2);
        foreach (var line in new[] { "---", "****", "___", " - - - ", " * * * ", " _ _ _ " })
        {
            var separator = HoverContentRenderer.Render(new("markdown", line)).Single();
            separator.IsThematicBreak.Should().BeTrue();
            separator.Text.Should().BeEmpty();
        }
        blocks.Select(b => b.Text).Should().Equal("function `twice`", "", "→ int", "Parameters:", "int n", "", "int twice(int n)");
    }
}
