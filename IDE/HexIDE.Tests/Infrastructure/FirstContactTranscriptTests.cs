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

    [Fact]
    public void Every_reply_field_is_one_a_reply_can_still_carry()
    {
        var unknown = Replies
            .SelectMany(r => FieldsOf(JsonDocument.Parse(r).RootElement))
            .Distinct()
            .Where(f => !ToolSource.ReplyFields.Contains(f))
            .ToList();

        string.Join(Environment.NewLine, unknown).Should().BeEmpty(
            "a transcript showing a field no reply carries any longer teaches the caller to look for nothing");
    }

    private static IEnumerable<string> FieldsOf(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.Object => element.EnumerateObject().SelectMany(p => FieldsOf(p.Value).Prepend(p.Name)),
        JsonValueKind.Array => element.EnumerateArray().SelectMany(FieldsOf),
        _ => [],
    };
}
