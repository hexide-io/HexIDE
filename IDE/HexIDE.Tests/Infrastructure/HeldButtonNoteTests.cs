using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.Automation;
using HexIDE.Desktop.Server;
using ModelContextProtocol.Protocol;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// What a held button means for a call joins its reply as <c>heldButton</c> (#2231), as a modal dialog's note joins as
/// <c>modalDialog</c>. While a button is held the integration tests read the note itself; this is how it reaches a reply.
/// </summary>
public class HeldButtonNoteTests
{
    private static CallToolResult Reply(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    private static string Text(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    private static ValueTask<CallToolResult> Succeeded() => ValueTask.FromResult(Reply("""{"success":true}"""));

    [AvaloniaFact]
    public async Task What_became_of_a_hold_joins_the_next_reply_once()
    {
        HeldButton.Ended("The held left button was dropped without a release.");

        var noted = await HeldButtonNote.NoteAsync(Succeeded, CancellationToken.None);
        var json = JsonNode.Parse(Text(noted))!.AsObject();
        json["success"]!.GetValue<bool>().Should().BeTrue();
        json["heldButton"]!.GetValue<string>().Should().Be("The held left button was dropped without a release.");

        Text(await HeldButtonNote.NoteAsync(Succeeded, CancellationToken.None)).Should().Be("""{"success":true}""",
            "the note is given once, and nothing is held");
    }

    [Fact]
    public void A_reply_that_already_names_a_dialog_keeps_it_beside_the_held_button()
    {
        var both = OpenDialogNote.With(OpenDialogNote.With(Reply("""{"success":true}"""), "'Options' is open"), "held", "heldButton");

        Text(both).Should().Be("""{"success":true,"modalDialog":"'Options' is open","heldButton":"held"}""");
    }
}
