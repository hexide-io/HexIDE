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

    internal static void ShowReferences(IEventBus events, IReadOnlyList<Location> locations) =>
        events.Publish(new ShowReferenceLocationsEvent(locations.ToArray()));
}
