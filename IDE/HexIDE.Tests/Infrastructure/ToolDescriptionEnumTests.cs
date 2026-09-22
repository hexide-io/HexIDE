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
/// Opting in is checked where it can be: a reply record with a field typed as a HexIDE enum must have its tool
/// opted in to that enum. It cannot be checked where a reply renders an enum into a <c>string</c> field, and
/// as of hexide-io/HexIDE#549 every reply does: no reply record has an enum-typed field, so the tools opted in
/// today were opted in by hand, nothing notices one that is not, and the check exists for the first reply
/// that carries an enum as itself.
/// </para>
/// <para>
/// Two directions go unchecked, and both for the same reason, that in running English a member name cannot be
/// told apart from an ordinary word. The prose may name something that is not a member. And a member is found
/// anywhere in the description, so a member that is also an everyday word elsewhere in it, say <c>Local</c>
/// in a sentence about local files, still passes after the sentence listing the vocabulary has dropped it. A
/// renamed member is caught all the same, because its new name is missing (hexide-io/HexIDE#549).
/// </para>
/// </remarks>
public class ToolDescriptionEnumTests
{
    private static readonly Lazy<IReadOnlyDictionary<string, List<Type>>> Enums = new(() =>
        new[] { "HexIDE", "HexIDE.Core", "HexIDE.Runtime" }
            .Select(Assembly.Load)
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsEnum)
            .GroupBy(t => t.Name)
            .ToDictionary(g => g.Key, g => g.ToList()));

    private static Type EnumNamed(string name)
    {
        var candidates = Enums.Value.GetValueOrDefault(name) ?? [];
        candidates.Should().ContainSingle($"[DescribesEnum(typeof({name}))] must name exactly one HexIDE enum");
        return candidates[0];
    }

    [Fact]
    public void At_least_one_tool_is_opted_in()
    {
        ToolSource.Tools.Should().Contain(t => t.Enums.Count > 0, "a guard with nothing opted in guards nothing");
    }

    [Fact]
    public void Every_enum_a_reply_record_carries_is_one_its_tool_describes()
    {
        var missing = new List<string>();
        foreach (var tool in ToolSource.Tools)
        {
            var seen = new HashSet<string>();
            var pending = new Queue<string>([tool.ReplyType]);
            while (pending.TryDequeue(out var record))
            {
                if (!seen.Add(record) || ToolSource.FieldsOf(record) is not { } fields)
                    continue;
                foreach (var field in fields)
                {
                    foreach (var name in Regex.Matches(field.Type, @"\w+").Select(m => m.Value))
                        if (Enums.Value.ContainsKey(name) && tool.Enums.All(e => e.Enum != name))
                            missing.Add($"{tool.Name}: {record}.{field.Name} is a {name}");
                    foreach (var nested in ToolSource.RecordsIn(field.Type))
                        pending.Enqueue(nested);
                }
            }
        }

        string.Join(Environment.NewLine, missing.Distinct()).Should().BeEmpty(
            "a reply that renders an enum needs [DescribesEnum] on its tool, or the check that every member is " +
            "named never runs for it");
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
