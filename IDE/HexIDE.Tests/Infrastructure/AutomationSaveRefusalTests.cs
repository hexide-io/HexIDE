using System.Text.RegularExpressions;
using HexIDE.Projects;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// No call an automation tool makes into the project service can wait on a dialog (hexide-io/HexIDE#751).
/// </summary>
/// <remarks>
/// A save that refuses reports it in a message box, and nothing answers a message box for an automation
/// caller, so the tool's reply waits until a person closes it. A method that can refuse takes an
/// <c>interactive</c> parameter, and a tool passes <c>interactive: false</c> and takes the refusal from the
/// result. Every other method a tool calls must be one listed here as showing no dialog, so a tool that
/// starts calling Save Project, Make or a close, none of which can be told not to ask, fails here rather
/// than on its first refusal. Read from <c>HexIdeTools.cs</c> as text, as <see cref="ToolSource"/> does,
/// because no test project references <c>HexIDE.Desktop</c>.
/// </remarks>
public class AutomationSaveRefusalTests
{
    private static readonly Regex ServiceCall = new(@"ProjectService\.(?<method>\w+)\(");

    /// <summary>
    /// The methods a tool calls that show no dialog. A method added here needs a reason, in a comment, why
    /// nothing it can reach asks the person anything.
    /// </summary>
    private static readonly HashSet<string> ShowNoDialog =
    [
        // Renders the document and compares it with what was last loaded or saved; it writes nothing.
        nameof(IProjectService.HasUnsavedChanges),
    ];

    private static readonly HashSet<string> TakeInteractive = typeof(IProjectService).GetMethods()
        .Where(method => method.GetParameters().Any(parameter => parameter.Name == "interactive"))
        .Select(method => method.Name)
        .ToHashSet();

    [Fact]
    public void Every_project_service_call_an_automation_tool_makes_shows_no_dialog()
    {
        var source = ToolSource.WithoutComments(ToolSource.Source);
        var calls = ServiceCall.Matches(source);

        calls.Should().HaveCountGreaterThanOrEqualTo(8, "the four tools that write make at least this many save calls");
        foreach (Match call in calls)
        {
            var method = call.Groups["method"].Value;
            var line = source.AsSpan(0, call.Index).Count('\n') + 1;
            if (TakeInteractive.Contains(method))
                ArgumentsFrom(source, call.Index + call.Length).Should().Contain("interactive: false",
                    "the {0} call on line {1} of HexIdeTools.cs (comments removed) would wait on a dialog nobody can answer",
                    method, line);
            else
                ShowNoDialog.Should().Contain(method,
                    "the {0} call on line {1} of HexIdeTools.cs (comments removed) has no interactive parameter, so it "
                    + "is either listed as showing no dialog or given one", method, line);
        }
    }

    [Fact]
    public void The_methods_that_can_be_told_not_to_ask_are_the_ones_a_tool_saves_through()
    {
        // The premise of the test above: if the parameter were renamed, every save call would fall to the
        // no-dialog list and fail there, which is the right outcome but a confusing one to read.
        TakeInteractive.Should().Contain(["SaveForm", "SaveModule", "AddNewForm", "AddNewModule",
            "AddNewUserControl", "AddNewPropertyPage"]);
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
