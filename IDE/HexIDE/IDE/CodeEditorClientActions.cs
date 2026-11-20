using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.IDE;

public sealed class CodeEditorClientActions(Func<IEditorService> editors, IEventBus events) : IAddinCodeEditorActions
{
    public Task<bool> StartRenameAsync(string uri, Position position, CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => StartRenameAsync(editors(), null, uri, position),
            DispatcherPriority.Normal, cancellationToken).GetTask().Unwrap();

    public Task<bool> GoToLocationAsync(string uri, HexIDE.Lsp.Messages.Range range,
        CancellationToken cancellationToken) =>
        Dispatcher.UIThread.InvokeAsync(() => GoToLocation(editors(), uri, range),
            DispatcherPriority.Normal, cancellationToken).GetTask();

    public async Task ShowReferencesAsync(IReadOnlyList<Location> locations, CancellationToken cancellationToken) =>
        await Dispatcher.UIThread.InvokeAsync(() => ShowReferences(events, locations),
            DispatcherPriority.Normal, cancellationToken);

    internal static Task<bool> StartRenameAsync(IEditorService editors, CodeEditorViewModel? current,
        string uri, Position position)
    {
        var target = current is not null && LspDocumentUri.AreSame(uri, current.GetDocumentUriPublic())
            ? current : editors.OpenCodeForUri(uri);
        return target?.StartRenameAtAsync(position) ?? Task.FromResult(false);
    }

    internal static AvaloniaEdit.Document.TextLocation EditorLocation(Position position) =>
        new(position.Line + 1, position.Character + 1);

    private static bool GoToLocation(IEditorService editors, string uri, HexIDE.Lsp.Messages.Range range)
    {
        var target = editors.OpenCodeForUri(uri);
        if (target is null || !TryOffset(range.Start, out var start) || !TryOffset(range.End, out var end)
            || end < start) return false;
        target.NavigateToOffset(start);
        target.SelectionLength = end - start;
        return true;

        bool TryOffset(Position position, out int offset)
        {
            offset = 0;
            if (position.Line < 0 || position.Line >= target.Document.LineCount || position.Character < 0
                || position.Character > target.Document.GetLineByNumber(position.Line + 1).Length) return false;
            var location = EditorLocation(position);
            offset = target.Document.GetOffset(location.Line, location.Column);
            return true;
        }
    }

    internal static void ShowReferences(IEventBus events, IReadOnlyList<Location> locations) =>
        events.Publish(new ShowReferenceLocationsEvent(locations.ToArray()));
}
