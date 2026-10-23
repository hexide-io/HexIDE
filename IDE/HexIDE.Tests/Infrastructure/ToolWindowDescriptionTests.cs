namespace HexIDE.Tests.Infrastructure;

/// <summary>Guards hexide-io/HexIDE#1321: auto scores windows rather than reading z-order.</summary>
public class ToolWindowDescriptionTests
{
    [Fact]
    public void No_tool_text_describes_auto_as_frontmost()
    {
        ToolSource.Tools.Where(t => t.AllText.Contains("frontmost", StringComparison.OrdinalIgnoreCase))
            .Select(t => t.Name)
            .Should().BeEmpty("auto prefers a visible window other than the IDE, regardless of z-order");
    }
}
