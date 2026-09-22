using System.Text.Json;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Guards hexide-io/HexIDE#396, option 2: <c>docs/mcp-first-contact.md</c>, the worked path a first-time caller
/// takes through the automation surface, cannot drift from the tools it shows.
/// </summary>
/// <remarks>
/// <para>
/// The transcript's replies were taken from a real run, not composed from the record types — a reply written
/// from the author's model of the tool is the thing the transcript exists to test. What this checks is the
/// mechanical half: that every call it shows could still be made as written, and that every field a reply
/// shows is one a reply can still carry.
/// </para>
/// <para>
/// A call is a fenced <c>mcp</c> block holding <c>tool_name {json arguments}</c>; the <c>json</c> block after it
/// is what came back.
/// </para>
/// </remarks>
public class FirstContactTranscriptTests
{
    private static readonly Regex Fence = new(@"^```(?<lang>\w+)\r?\n(?<body>.*?)^```", RegexOptions.Multiline | RegexOptions.Singleline);
    private static readonly Regex Call = new(@"^(?<tool>[a-z_]+)\s*(?<args>\{.*\})?\s*$", RegexOptions.Singleline);

    private static readonly Lazy<IReadOnlyList<(string Lang, string Body)>> Blocks = new(() =>
        Fence.Matches(File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "mcp-first-contact.md")))
            .Select(m => (m.Groups["lang"].Value, m.Groups["body"].Value.Trim()))
            .ToList());

    private static IEnumerable<string> Calls => Blocks.Value.Where(b => b.Lang == "mcp").Select(b => b.Body);

    private static IEnumerable<string> Replies => Blocks.Value.Where(b => b.Lang == "json").Select(b => b.Body);

    [Fact]
    public void The_transcript_shows_calls_and_their_replies()
    {
        Calls.Should().HaveCountGreaterThan(3, "a transcript with nothing in it guards nothing");
        Replies.Should().HaveCountGreaterThanOrEqualTo(Calls.Count(), "every call shows what came back");
    }

    [Fact]
    public void Every_call_could_still_be_made_as_written()
    {
        var wrong = new List<string>();
        foreach (var text in Calls)
        {
            var call = Call.Match(text);
            if (!call.Success) { wrong.Add($"'{text}' is not `tool_name {{json}}`"); continue; }

            var tool = ToolSource.Tools.SingleOrDefault(t => t.Name == call.Groups["tool"].Value);
            if (tool is null) { wrong.Add($"no tool is called {call.Groups["tool"].Value}"); continue; }

            var passed = call.Groups["args"].Success
                ? JsonDocument.Parse(call.Groups["args"].Value).RootElement.EnumerateObject().Select(p => p.Name).ToList()
                : [];
            wrong.AddRange(passed.Where(a => tool.Parameters.All(p => p.Name != a))
                .Select(a => $"{tool.Name} has no parameter '{a}'"));
            wrong.AddRange(tool.Parameters.Where(p => !p.Optional && !passed.Contains(p.Name))
                .Select(p => $"{tool.Name} is shown without its required '{p.Name}'"));
        }

        string.Join(Environment.NewLine, wrong).Should().BeEmpty(
            "a first-time caller copies the call the transcript shows, and a stale one fails on first contact");
    }

    /// <summary>Each call with the reply shown after it: the first <c>json</c> block before the next call.</summary>
    private static IEnumerable<(string Tool, string Reply)> CallsWithReplies()
    {
        string? tool = null;
        foreach (var (lang, body) in Blocks.Value)
        {
            if (lang == "mcp")
                tool = Call.Match(body).Groups["tool"].Value;
            else if (lang == "json" && tool is not null)
            {
                yield return (tool, body);
                tool = null;
            }
        }
    }

    /// <remarks>
    /// Checked against the reply type of the tool that was called, not against every field any reply has: a
    /// field renamed in one reply otherwise stays green for as long as some other reply carries a field of the
    /// same name, which for <c>note</c> is four of them (hexide-io/HexIDE#549).
    /// </remarks>
    [Fact]
    public void Every_reply_field_is_one_that_tools_reply_can_still_carry()
    {
        var unknown = new List<string>();
        var shown = 0;
        foreach (var (name, reply) in CallsWithReplies())
        {
            shown++;
            var tool = ToolSource.Tools.SingleOrDefault(t => t.Name == name);
            if (tool is null) continue; // Every_call_could_still_be_made_as_written names it
            unknown.AddRange(UnknownFields(JsonDocument.Parse(reply).RootElement, tool.ReplyType, tool.ReplyType)
                .Select(f => $"{name} shows '{f}'"));
        }

        shown.Should().Be(Calls.Count(), "every call in the transcript is followed by what came back");
        string.Join(Environment.NewLine, unknown.Distinct()).Should().BeEmpty(
            "a transcript showing a field its reply no longer carries teaches the caller to look for nothing");
    }

    /// <summary>
    /// The fields in <paramref name="element"/> that <paramref name="record"/> does not have, following a
    /// field into the record its type names. A field whose type names no reply record, a string or a raw
    /// JSON body, is not looked inside.
    /// </summary>
    private static IEnumerable<string> UnknownFields(JsonElement element, string record, string path)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                foreach (var unknown in UnknownFields(item, record, path + "[]"))
                    yield return unknown;
            yield break;
        }
        if (element.ValueKind != JsonValueKind.Object)
            yield break;

        var fields = ToolSource.FieldsOf(record) ?? [];
        foreach (var property in element.EnumerateObject())
        {
            var field = fields.FirstOrDefault(f => f.Name == property.Name);
            if (field is null)
            {
                yield return $"{path}.{property.Name}";
                continue;
            }
            foreach (var nested in ToolSource.RecordsIn(field.Type).Take(1))
                foreach (var unknown in UnknownFields(property.Value, nested, $"{path}.{property.Name}"))
                    yield return unknown;
        }
    }
}
