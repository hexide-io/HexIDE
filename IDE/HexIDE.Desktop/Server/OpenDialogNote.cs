using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HexIDE.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Names, in every tool's reply, a modal dialog open once the call is done (#681). A tool acts on the IDE behind a modal
/// that a person could not get past, and nothing in its reply used to say one was there: Set Next Statement's refusal
/// came back "Invoked", with a message box saying why left open; run_project and set_file_content succeeded under Add
/// Procedure; continue_program said Running while the program was held at its run-time error dialog.
/// </summary>
/// <remarks>
/// The call still acts; the reply says so. Refusing would stop a caller answering the dialog through the same tools, and
/// a word in each tool's description reaches only a caller that reads it. The note goes in as a <c>modalDialog</c> field
/// when the reply is a JSON object, and as a block of its own otherwise. Which dialogs <see cref="OpenDialogs.Note"/>
/// names is decided there.
/// </remarks>
internal static class OpenDialogNote
{
    // These address a window themselves. Left to the frontmost, they act in a dialog already open, or report that they
    // used it, so it is theirs to see; sent to the IDE or to a window by name, they act behind it, and it is named. A
    // program's MsgBox is named by their wait for the program (#1232).
    private static readonly HashSet<string> WindowTools =
        ["interact", "type_text", "press_key", "hover", "inspect_element", "dump_visual_tree", "take_snapshot"];

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            var before = await OnUiThread(OpenDialogs.Among) ?? [];
            var result = await next(request, ct);
            var addressesAWindow = WindowTools.Contains(request.Params?.Name ?? "") && AtTheFrontmost(request.Params);
            // On the UI thread too: a window's title and owner are read there or not at all.
            var note = await OnUiThread(windows => OpenDialogs.Note(before, OpenDialogs.Among(windows),
                namesOneAlreadyOpen: !addressesAWindow, leavesProgramDialogs: addressesAWindow));
            return note is null ? result : With(result, note);
        };

    internal static bool AtTheFrontmost(CallToolRequestParams? call) =>
        call?.Arguments?.TryGetValue("window", out var window) != true
        || window.ValueKind != System.Text.Json.JsonValueKind.String
        || window.GetString() is not { } scope || string.IsNullOrWhiteSpace(scope)
        || scope.Equals("auto", StringComparison.OrdinalIgnoreCase);

    // At Send priority, so a running program's handler, which keeps the dispatcher busy at its own, cannot hold the reply.
    private static async Task<T?> OnUiThread<T>(Func<IReadOnlyList<Window>, T> read)
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime lifetime)
            return default;
        return await Dispatcher.UIThread.InvokeAsync(() => read(lifetime.Windows), DispatcherPriority.Send);
    }

    /// <summary><paramref name="result"/> with <paramref name="note"/> added: a field of its JSON object, or a block.</summary>
    internal static CallToolResult With(CallToolResult result, string note)
    {
        var content = result.Content.ToList();
        var index = content.FindIndex(c => c is TextContentBlock);
        if (index >= 0 && TryParseObject(((TextContentBlock)content[index]).Text) is { } reply)
        {
            reply["modalDialog"] = note;
            content[index] = new TextContentBlock { Text = reply.ToJsonString() };
        }
        else
            content.Add(new TextContentBlock { Text = note });
        return new CallToolResult
        {
            Content = content,
            IsError = result.IsError,
            StructuredContent = result.StructuredContent,
            Meta = result.Meta,
        };
    }

    private static JsonObject? TryParseObject(string text)
    {
        try
        {
            return JsonNode.Parse(text) as JsonObject;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }
}
