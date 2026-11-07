using System.Text.RegularExpressions;
using HexIDE.Automation;

namespace HexIDE.Tests.Infrastructure;

/// <summary>Keeps CLAUDE.md's curated tool signatures and interaction vocabulary in step with the tools.</summary>
public class McpToolTableDocumentationTests
{
    private static Match[] Rows()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "CLAUDE.md")).ReplaceLineEndings("\n");
        var rows = Regex.Matches(source,
            @"^\| `(?<name>\w+)\((?<parameters>[^)]*)\)` \| (?<description>.*)$",
            RegexOptions.Multiline).ToArray();
        rows.Should().NotBeEmpty("the curated table has tool signatures to check");
        rows.Should().Contain(r => r.Groups["name"].Value == "interact");
        return rows;
    }

    [Fact]
    public void Every_documented_signature_names_a_tool()
    {
        var names = ToolSource.Tools.Select(t => t.Name).ToArray();
        foreach (var row in Rows())
            names.Should().Contain(row.Groups["name"].Value,
                "each signature in CLAUDE.md must name an McpServerTool in HexIdeTools");
    }

    [Fact]
    public void Every_documented_signature_matches_the_tools_parameters()
    {
        foreach (var row in Rows())
        {
            var name = row.Groups["name"].Value;
            var tool = ToolSource.Tools.SingleOrDefault(t => t.Name == name);
            tool.Should().NotBeNull($"CLAUDE.md documents {name}");
            var expected = tool!.Parameters.Select(p => p.Name + (p.Optional ? "?" : ""));
            row.Groups["parameters"].Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Should().Equal(expected, $"CLAUDE.md's {name} signature must preserve parameter order and optionality");
        }
    }

    [Fact]
    public void The_interact_row_names_every_driver_verb()
    {
        var row = Rows().Single(r => r.Groups["name"].Value == "interact");
        var documented = Regex.Matches(row.Groups["description"].Value, @"`(?<verb>\w+)`")
            .Select(m => m.Groups["verb"].Value).ToArray();
        documented.Should().Contain(UiAutomationDriver.Verbs,
            "CLAUDE.md must tell callers every action the driver accepts");
    }
}
