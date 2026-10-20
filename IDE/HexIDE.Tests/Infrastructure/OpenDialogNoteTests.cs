using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Desktop.Server;
using ModelContextProtocol.Protocol;


namespace HexIDE.Tests.Infrastructure;

/// <summary>How a modal dialog's note joins a tool's reply, and which calls are told of one (#681).</summary>
public class OpenDialogNoteTests
{
    private const string Note = "'Add Procedure', a modal dialog over 'HexIDE', opened during this call and waits for an answer.";

    private static string Text(CallToolResult result, int block = 0) => ((TextContentBlock)result.Content[block]).Text;

    private static CallToolResult Reply(string text) => new() { Content = [new TextContentBlock { Text = text }] };

    [Fact]
    public void A_JSON_reply_gains_a_field_and_keeps_its_own()
    {
        var noted = OpenDialogNote.With(Reply("""{"success":true,"note":"Invoked 'Add Procedure...'."}"""), Note);

        noted.Content.Should().HaveCount(1);
        var json = JsonNode.Parse(Text(noted))!.AsObject();
        json["success"]!.GetValue<bool>().Should().BeTrue();
        json["note"]!.GetValue<string>().Should().Be("Invoked 'Add Procedure...'.");
        json["modalDialog"]!.GetValue<string>().Should().Be(Note);
    }

    [Fact]
    public void The_reply_it_rewrites_keeps_its_text_as_written()
    {
        // Rewritten with the default encoder, every apostrophe and non-ASCII character came back escaped (#753).
        var noted = OpenDialogNote.With(Reply("""{"note":"Invoked 'Ж1' → there."}"""), "'Найти' is open");

        Text(noted).Should().Be("""{"note":"Invoked 'Ж1' → there.","modalDialog":"'Найти' is open"}""");
    }

    [Fact]
    public void A_reply_that_is_not_a_JSON_object_gains_a_block_and_keeps_whether_it_failed()
    {
        var reply = Reply("No main window");
        reply.IsError = true;

        var noted = OpenDialogNote.With(reply, Note);

        noted.IsError.Should().BeTrue();
        noted.Content.Select(c => ((TextContentBlock)c).Text).Should().Equal("No main window", Note);
    }

    [Fact]
    public void Every_tool_the_note_singles_out_is_one_the_server_has()
    {
        var tools = ToolSource.Tools.Select(t => t.Name).ToList();

        tools.Should().Contain(OpenDialogNote.WindowTools).And.Contain(OpenDialogNote.ProgramWaitTools);
    }

    // Through the filter itself, over windows of the test's own.
    private sealed class Ide : IDisposable
    {
        private readonly Func<(IReadOnlyList<Window>, Window?)> saved = OpenDialogNote.Windows;
        public readonly Window Main = new() { Title = "HexIDE" };
        public readonly List<Window> Windows = [];

        public Ide()
        {
            Main.Show();
            Windows.Add(Main);
            OpenDialogNote.Windows = () => (Windows, Main);
        }

        public Window Open(string title)
        {
            var dialog = new Window { Title = title };
            _ = dialog.ShowDialog(Main);
            Dispatcher.UIThread.RunJobs();
            Windows.Add(dialog);
            return dialog;
        }

        public void Dispose()
        {
            OpenDialogNote.Windows = saved;
            foreach (var window in Enumerable.Reverse(Windows))
                window.Close();
        }
    }

    private static async Task<string> CallAsync(string tool, string? window = null, Action? during = null)
    {
        var arguments = new Dictionary<string, JsonElement>();
        if (window is not null)
            arguments["window"] = JsonSerializer.SerializeToElement(window);
        var result = await OpenDialogNote.NoteAsync(new CallToolRequestParams { Name = tool, Arguments = arguments }, () =>
        {
            during?.Invoke();
            return ValueTask.FromResult(Reply("""{"success":true}"""));
        }, CancellationToken.None);
        return Text(result);
    }

    private static string? ModalDialog(string reply) => JsonNode.Parse(reply)!["modalDialog"]?.GetValue<string>();

    [AvaloniaFact]
    public async Task A_dialog_a_call_opens_is_named_in_its_reply()
    {
        using var ide = new Ide();

        var reply = await CallAsync("invoke_menu_item", during: () => ide.Open("Add Procedure"));

        ModalDialog(reply).Should().StartWith(Note);
    }

    [AvaloniaFact]
    public async Task A_dialog_already_open_is_named_to_a_call_that_acts_behind_it_and_not_to_one_that_acts_in_it()
    {
        using var ide = new Ide();
        ide.Open("Add Procedure");

        ModalDialog(await CallAsync("set_file_content")).Should().Contain("this call reached past it");
        ModalDialog(await CallAsync("dump_visual_tree", window: "ide")).Should().Contain("this call reached past it");
        ModalDialog(await CallAsync("dump_visual_tree")).Should().BeNull("left to the frontmost window, it walks the dialog");
        ModalDialog(await CallAsync("type_text", window: "Add Procedure")).Should().BeNull("it was sent to the dialog");
    }

    // A look that fails, as one does once shutdown_ide has closed the IDE, leaves the tool's reply as it was.
    [AvaloniaFact]
    public async Task A_look_at_the_windows_that_fails_leaves_the_reply_as_the_tool_gave_it()
    {
        using var ide = new Ide();
        ide.Open("Add Procedure");
        OpenDialogNote.Windows = () => throw new InvalidOperationException("The dispatcher has shut down.");

        (await CallAsync("shutdown_ide")).Should().Be("""{"success":true}""");
    }
}
