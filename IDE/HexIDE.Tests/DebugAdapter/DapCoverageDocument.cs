using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Tests.DebugAdapter;

internal static class DapCoverageDocument
{
    internal const string Start = "<!-- BEGIN GENERATED DAP COVERAGE -->";
    internal const string End = "<!-- END GENERATED DAP COVERAGE -->";

    internal static string Generate(IReadOnlyList<DapSpecificationModel.Message> messages)
    {
        var root = DapSpecificationModel.Root();
        var session = File.ReadAllText(Path.Combine(root, "IDE", "HexIDE.Dap", "DapSession.cs"));
        var host = File.ReadAllText(Path.Combine(root, "IDE", "HexIDE", "Debugging", "ExternalDebugService.cs"));
        var connection = File.ReadAllText(Path.Combine(root, "IDE", "HexIDE.Dap", "DapConnection.cs"));
        // Fail closed if reverse dispatch changes: generic rejection is not reverse-request support.
        if (!Regex.IsMatch(connection, """case "request":\s*var reverseCommand = Text\(message, "command"\);\s*RecordFrame\(\);\s*await RejectReverseRequestAsync\(sequence, reverseCommand\)\.ConfigureAwait\(false\);\s*break;"""))
            throw new InvalidDataException("Reverse-request dispatch changed; update coverage extraction.");
        var requests = Matches(session, "(?:Request|Inspect|RequestAsync|ResumeCoreAsync)\\([^;]*?\"(?<name>[a-zA-Z]+)\"");
        // ResumeAsync validates its dynamic command against this literal pattern before forwarding it.
        var resume = Regex.Match(session, "command is not \\((?<commands>[^)]+)\\)");
        if (!resume.Success) throw new InvalidDataException("Resume command validation changed; update coverage extraction.");
        requests.UnionWith(Matches(resume.Groups["commands"].Value, "\"(?<name>[a-zA-Z]+)\""));
        var events = Matches(session, "case \"(?<name>[a-zA-Z]+)\"");
        events.UnionWith(Matches(host, "update\\.Event\\?\\.Event == \"(?<name>[a-zA-Z]+)\""));
        var known = messages.Select(m => m.Name).ToHashSet(StringComparer.Ordinal);
        if (requests.Concat(events).Any(name => !known.Contains(name)))
            throw new InvalidDataException("Client handles a message absent from the pinned DAP schema.");

        bool Supported(DapSpecificationModel.Message message) => message.Kind switch
        {
            "request" => requests.Contains(message.Name),
            "event" => events.Contains(message.Name),
            _ => false // DapConnection rejects every reverse request.
        };
        var output = new StringBuilder().AppendLine(Start)
            .AppendLine($"The pinned schema defines **{messages.Count} messages**. HexIDE implements {messages.Count(Supported)} of the {messages.Count} at the command/event level.")
            .AppendLine().AppendLine("| Support | Message | Kind | Direction |")
            .AppendLine("|---|---|---|---|");
        foreach (var message in messages)
            output.AppendLine($"| {(Supported(message) ? "✅" : "○")} | `{message.Name}` | {message.Kind} | {message.Direction} |");
        return output.Append(End).ToString().ReplaceLineEndings("\n");
    }

    private static HashSet<string> Matches(string source, string pattern) => Regex.Matches(source, pattern)
        .Select(match => match.Groups["name"].Value).ToHashSet(StringComparer.Ordinal);
}
