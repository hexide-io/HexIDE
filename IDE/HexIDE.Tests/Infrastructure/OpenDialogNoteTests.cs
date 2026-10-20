using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using HexIDE.Desktop.Server;
using ModelContextProtocol.Protocol;

namespace HexIDE.Tests.Infrastructure;

/// <summary>How a modal dialog's note joins a tool's reply, and which calls name one already open (#681).</summary>
public class OpenDialogNoteTests
{
    private const string Note = "This call opened a modal dialog, 'Add Procedure', over 'HexIDE'.";

    private static string Text(CallToolResult result, int block = 0) => ((TextContentBlock)result.Content[block]).Text;

    [Fact]
    public void A_JSON_reply_gains_a_field_and_keeps_its_own()
    {
        var reply = new CallToolResult { Content = [new TextContentBlock { Text = """{"success":true,"note":"Invoked 'Add Procedure...'."}""" }] };

        var noted = OpenDialogNote.With(reply, Note);

        noted.Content.Should().HaveCount(1);
        var json = JsonNode.Parse(Text(noted))!.AsObject();
        json["success"]!.GetValue<bool>().Should().BeTrue();
        json["note"]!.GetValue<string>().Should().Be("Invoked 'Add Procedure...'.");
        json["modalDialog"]!.GetValue<string>().Should().Be(Note);
    }

    [Fact]
    public void A_reply_that_is_not_a_JSON_object_gains_a_block_and_keeps_whether_it_failed()
    {
        var reply = new CallToolResult { Content = [new TextContentBlock { Text = "No main window" }], IsError = true };

        var noted = OpenDialogNote.With(reply, Note);

        noted.IsError.Should().BeTrue();
        noted.Content.Select(c => ((TextContentBlock)c).Text).Should().Equal("No main window", Note);
    }

    private static CallToolRequestParams Call(string? window) => new()
    {
        Name = "interact",
        Arguments = window is null ? new Dictionary<string, JsonElement>()
            : new Dictionary<string, JsonElement> { ["window"] = JsonSerializer.SerializeToElement(window) },
    };

    // Sent to the IDE or to a window by name, a call acts behind a dialog that is open, so the dialog is named.
    [Theory]
    [InlineData(null, true)]
    [InlineData("auto", true)]
    [InlineData("", true)]
    [InlineData("ide", false)]
    [InlineData("frmSetup", false)]
    public void Only_a_call_left_to_the_frontmost_window_sees_a_dialog_for_itself(string? window, bool frontmost) =>
        OpenDialogNote.AtTheFrontmost(Call(window)).Should().Be(frontmost);
}
