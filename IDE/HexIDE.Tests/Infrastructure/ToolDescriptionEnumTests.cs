using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards hexide-io/HexIDE#400: a tool that renders an enum into its reply must name every member in its
/// description, because the description is the only place a caller learns what the strings mean.
/// </summary>
/// <remarks>
/// <para>
/// Opt-in through <c>[DescribesEnum(typeof(T))]</c> on the tool, so ordinary prose is never mistaken for a
/// vocabulary. The tools live in <c>HexIDE.Desktop</c>, which no test project references, so this reads
/// <c>HexIdeTools.cs</c> as text, as <c>CommandLineDocumentationTests</c> reads <c>ServerOptions.cs</c>, and
/// takes the enums from the assemblies it does reference.
/// </para>
/// <para>
/// It checks that no member is missing. It does not check that the prose names nothing that is not a member:
/// in running English that cannot be told apart from an ordinary capitalised word. A renamed member is
/// still caught, because its new name is missing.
/// </para>
/// </remarks>
public class ToolDescriptionEnumTests
{
    private sealed record Tool(string Name, string Description, IReadOnlyList<(string Enum, string[] NotRendered)> Enums);

    private static readonly Regex ToolStart = new(@"\[McpServerTool\(Name = ""(?<name>[a-z_]+)""\)\]");
    private static readonly Regex Literal = new(@"""(?<text>(?:[^""\\]|\\.)*)""");
    private static readonly Regex Describes = new(@"\[DescribesEnum\(typeof\((?:[\w.]+\.)?(?<type>\w+)\)(?<rest>[^\]]*)\)\]");

    private static IReadOnlyList<Tool> Tools()
    {
        var source = File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "HexIdeTools.cs"));
        var starts = ToolStart.Matches(source).ToList();
        var tools = new List<Tool>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? starts[i + 1].Index : source.Length;
            var block = source[starts[i].Index..end];
            var header = block[..block.IndexOf("\n    public ", StringComparison.Ordinal)];
            var enums = Describes.Matches(header)
                .Select(m => (m.Groups["type"].Value,
                    Literal.Matches(m.Groups["rest"].Value).Select(l => l.Groups["text"].Value).ToArray()))
                .ToList();
            tools.Add(new Tool(starts[i].Groups["name"].Value, DescriptionOf(header), enums));
        }
        return tools;
    }

    /// <summary>The description's text: every literal in the attribute, joined as the compiler joins a `+` chain.</summary>
    private static string DescriptionOf(string header)
    {
        var at = header.IndexOf("[Description(", StringComparison.Ordinal);
        if (at < 0) return "";
        var text = new StringBuilder();
        var position = at;
        while (Literal.Match(header, position) is { Success: true } literal)
        {
            text.Append(Regex.Unescape(literal.Groups["text"].Value));
            position = literal.Index + literal.Length;
            var next = header[position..].TrimStart();
            if (!next.StartsWith('+')) break;
        }
        return text.ToString();
    }

    private static Type EnumNamed(string name)
    {
        var candidates = new[] { "HexIDE", "HexIDE.Core", "HexIDE.Runtime" }
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsEnum && t.Name == name)
            .ToList();
        candidates.Should().ContainSingle($"[DescribesEnum(typeof({name}))] must name exactly one HexIDE enum");
        return candidates[0];
    }

    [Fact]
    public void Tools_that_render_an_enum_are_opted_in()
    {
        Tools().Should().Contain(t => t.Enums.Count > 0, "a guard with nothing opted in guards nothing");
    }

    [Fact]
    public void Every_opted_in_description_names_every_member_it_renders()
    {
        var missing = new List<string>();
        foreach (var tool in Tools())
            foreach (var (enumName, notRendered) in tool.Enums)
                foreach (var member in Enum.GetNames(EnumNamed(enumName)).Except(notRendered))
                    if (!Regex.IsMatch(tool.Description, $@"\b{member}\b"))
                        missing.Add($"{tool.Name}: {enumName}.{member}");

        string.Join(Environment.NewLine, missing).Should().BeEmpty(
            "a caller shown one of these in a reply has nowhere else to learn what it means. Name it in the tool's " +
            "[Description], or list it in [DescribesEnum] as not rendered if the reply can never show it");
    }

    [Fact]
    public void Every_listed_exception_is_a_real_member()
    {
        foreach (var tool in Tools())
            foreach (var (enumName, notRendered) in tool.Enums.Where(e => e.NotRendered.Length > 0))
                Enum.GetNames(EnumNamed(enumName)).Should().Contain(notRendered,
                    $"{tool.Name} excuses members of {enumName} that do not exist");
    }
}
