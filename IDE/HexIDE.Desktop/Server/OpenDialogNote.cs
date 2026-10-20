using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using HexIDE.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Names, in a tool's reply, a modal dialog open once the call is done (#681). A tool acts on the IDE behind a modal that
/// a person could not get past, and nothing in its reply used to say one was there: Set Next Statement's refusal came
/// back "Invoked", with a message box saying why left open; run_project and set_file_content succeeded under Add
/// Procedure; continue_program said Running while the program was held at its run-time error dialog.
/// </summary>
/// <remarks>
/// <para>
/// The call still acts; the reply says so. Refusing would stop a caller answering the dialog through the same tools, and
/// a word in each tool's description reaches only a caller that reads it. The note goes in as a <c>modalDialog</c> field
/// when the reply is a JSON object, and as a block of its own otherwise. Which dialogs <see cref="OpenDialogs.Note"/>
/// names is decided there; the window the call acts in is resolved here, as the tools resolve it.
/// </para>
/// <para>
/// The note never costs a call its reply. Each look at the windows is bounded, so a tool that never waits on the UI
/// thread, such as list_lsp_messages, still answers while a native file picker holds it, and a look that fails, as one
/// does once shutdown_ide has closed the IDE, leaves the reply as the tool gave it. A tool that throws gets no note.
/// </para>
/// </remarks>
internal static class OpenDialogNote
{
    // These address a window themselves, resolved from their 'window' argument; the rest act in the IDE.
    internal static readonly HashSet<string> WindowTools =
        ["interact", "type_text", "press_key", "hover", "inspect_element", "dump_visual_tree", "take_snapshot"];

    // These wait for the program behind their window, and name its dialogs (#1232).
    internal static readonly HashSet<string> ProgramWaitTools = ["interact", "type_text", "press_key"];

    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(500);

    /// <summary>The windows to look at, and the IDE's own. Replaced by a test, whose host keeps no list of windows.</summary>
    internal static Func<(IReadOnlyList<Window> Windows, Window? Main)> Windows = () =>
        Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime lifetime
            ? (lifetime.Windows, lifetime.MainWindow)
            : ([], null);

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (request, ct) => NoteAsync(request.Params, () => next(request, ct), ct);

    /// <summary>The filter's work: <paramref name="call"/> made through <paramref name="next"/>, its reply noted.</summary>
    internal static async ValueTask<CallToolResult> NoteAsync(
        CallToolRequestParams? call, Func<ValueTask<CallToolResult>> next, CancellationToken ct)
    {
        var tool = call?.Name ?? "";
        var watch = await OnUiThread(() =>
        {
            var (windows, main) = Windows();
            return OpenDialogs.Watch(windows, TargetOf(call, windows, main), ProgramWaitTools.Contains(tool));
        }, ct);
        var result = await next();
        if (watch is null)
            return result;
        var note = await OnUiThread(() => OpenDialogs.Note(watch, Windows().Windows), ct);
        return note is null ? result : With(result, note);
    }

    // The window a call acts in: the one its 'window' argument picks, as every window tool resolves it, or the IDE's.
    private static Window? TargetOf(CallToolRequestParams? call, IReadOnlyList<Window> windows, Window? main)
    {
        if (main is null || !WindowTools.Contains(call?.Name ?? ""))
            return main;
        string? scope = call?.Arguments?.TryGetValue("window", out var window) == true
                        && window.ValueKind == System.Text.Json.JsonValueKind.String ? window.GetString() : null;
        return HexIDE.IDE.ForegroundWindow.Pick(scope, main, windows).Window;
    }

    // At Send priority, so a running program's handler, which keeps the dispatcher busy at its own, cannot hold the reply;
    // bounded, so a UI thread held by a native dialog cannot either. Null when the look did not happen.
    private static async Task<T?> OnUiThread<T>(Func<T?> read, CancellationToken ct) where T : class
    {
        try
        {
            return await Dispatcher.UIThread.InvokeAsync(read, DispatcherPriority.Send).GetTask().WaitAsync(Look, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return null;
        }
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
