using Avalonia.Threading;
using HexIDE.Automation;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Names, in every tool's reply, a button <c>click … to x,y hold</c> left down (#2231), and, once, what became of one that
/// ended without a release: abandoned after the idle limit, or dropped with its window. Each call restarts the idle clock,
/// at its start and at its end, so a hold is abandoned only once nothing at all has been asked of the IDE for that long.
/// </summary>
/// <remarks>
/// A held button changes what other calls may do, since click, double_click and hover are refused, and what they see,
/// which is the drag in progress. So a caller that has lost track of one is told on whatever it calls next, not by a
/// refusal. The note goes in as a <c>heldButton</c> field, as <see cref="OpenDialogNote"/> adds <c>modalDialog</c>, and its
/// look is bounded the same way, so it never costs a call its reply.
/// </remarks>
internal static class HeldButtonNote
{
    private static readonly TimeSpan Look = TimeSpan.FromMilliseconds(500);

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        (request, ct) => NoteAsync(() => next(request, ct), ct);

    /// <summary>The filter's work: <paramref name="next"/> made between two touches of the idle clock, its reply noted.</summary>
    internal static async ValueTask<CallToolResult> NoteAsync(Func<ValueTask<CallToolResult>> next, CancellationToken ct)
    {
        HeldButton.Touch();
        CallToolResult result;
        try
        {
            result = await next();
        }
        finally
        {
            HeldButton.Touch();
        }
        string? note;
        try
        {
            note = await Dispatcher.UIThread.InvokeAsync(HeldButton.TakeNote, DispatcherPriority.Send).GetTask().WaitAsync(Look, ct);
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            note = null;
        }
        return note is null ? result : OpenDialogNote.With(result, note, "heldButton");
    }
}
