using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Every save an automation tool makes shows no dialog (hexide-io/HexIDE#751).
/// </summary>
/// <remarks>
/// A save that refuses reports it in a message box, and nothing answers a message box for an automation
/// caller, so the tool's reply waits until a person closes it. A tool that saves therefore passes
/// <c>interactive: false</c> and takes the refusal from the result. Read from <c>HexIdeTools.cs</c> as text,
/// as <see cref="ToolSource"/> does, because no test project references <c>HexIDE.Desktop</c>; so a tool added
/// later that saves the way the IDE does fails here rather than on its first refusal.
/// </remarks>
public class AutomationSaveRefusalTests
{
    private static readonly Regex SaveCall = new(@"ProjectService\.(?<method>SaveForm|SaveModule|AddNew\w+)\(");

    [Fact]
    public void Every_save_an_automation_tool_makes_passes_interactive_false()
    {
        var source = ToolSource.WithoutComments(ToolSource.Source);
        var calls = SaveCall.Matches(source);

        calls.Should().HaveCountGreaterThanOrEqualTo(8, "the four tools that write make at least this many save calls");
        foreach (Match call in calls)
        {
            var line = source.AsSpan(0, call.Index).Count('\n') + 1;
            ArgumentsFrom(source, call.Index + call.Length).Should().Contain("interactive: false",
                "the {0} call on line {1} of HexIdeTools.cs (comments removed) would wait on a dialog nobody can answer",
                call.Groups["method"].Value, line);
        }
    }

    /// <summary>The text from <paramref name="start"/> to the parenthesis that closes the call.</summary>
    private static string ArgumentsFrom(string source, int start)
    {
        var depth = 1;
        for (var i = start; i < source.Length; i++)
        {
            if (source[i] == '(') depth++;
            else if (source[i] == ')' && --depth == 0) return source[start..i];
        }
        return source[start..];
    }
}
