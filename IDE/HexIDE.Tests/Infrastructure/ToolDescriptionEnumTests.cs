using System.Reflection;
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
        ToolSource.Tools.Should().Contain(t => t.Enums.Count > 0, "a guard with nothing opted in guards nothing");
    }

    [Fact]
    public void Every_opted_in_description_names_every_member_it_renders()
    {
        var missing = new List<string>();
        foreach (var tool in ToolSource.Tools)
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
        foreach (var tool in ToolSource.Tools)
            foreach (var (enumName, notRendered) in tool.Enums.Where(e => e.NotRendered.Length > 0))
                Enum.GetNames(EnumNamed(enumName)).Should().Contain(notRendered,
                    $"{tool.Name} excuses members of {enumName} that do not exist");
    }
}
