using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;

namespace HexIDE.Forms.ViewModels;

/// <summary>Keeps a closed code window's unsaved text owned by the project on the language layer.</summary>
internal sealed class ProjectLspDocument : IDisposable
{
    private static readonly ConditionalWeakTable<ILspClient, Dictionary<DocumentIdentity, ProjectLspDocument>> Held = new();
    private readonly Dictionary<DocumentIdentity, ProjectLspDocument> documents;
    private readonly DocumentIdentity identity;
    private readonly LspDocumentSession session;
    private readonly List<IDisposable> subscriptions = [];
    private bool disposed;

    private ProjectLspDocument(ILspClient client, DocumentIdentity identity, LspDocumentSession session, IEventBus events)
    {
        documents = Held.GetOrCreateValue(client);
        this.identity = identity;
        this.session = session;
        if (documents.TryGetValue(identity, out var previous)) previous.Dispose();
        documents[identity] = this;
        subscriptions.Add(events.Subscribe<DocumentSavedEvent>(e =>
        {
            if ((e.Module == identity.Module && e.Module is not null) || (e.Form == identity.Form && e.Form is not null))
                SavedAsync().ListenErrors();
        }));
        subscriptions.Add(events.Subscribe<ModuleMovedEvent>(e =>
        {
            if (e.Module == identity.Module) session.RenameAsync(DocumentWireName.For(identity)).ListenErrors();
        }));
        subscriptions.Add(events.Subscribe<ModuleUnloadedEvent>(e => { if (e.Module == identity.Module) Dispose(); }));
        subscriptions.Add(events.Subscribe<FormUnloadedEvent>(e => { if (e.Form == identity.Form) Dispose(); }));
        subscriptions.Add(events.Subscribe<FileReloadedFromDiskEvent>(e =>
        {
            if (identity.AbsolutePath is { } path &&
                LspDocumentUri.AreSame(LspDocumentUri.ForFile(path), LspDocumentUri.ForFile(e.AbsolutePath))) Dispose();
        }));
        subscriptions.Add(events.Subscribe<ProjectUnloadedEvent>(e => { if (e.Project == identity.Project) Dispose(); }));
    }

    public static void CloseWindow(ILspClient client, DocumentIdentity identity, LspDocumentSession session,
        IEventBus events, bool dirty)
    {
        if (dirty)
        {
            var holder = session.CreateProjectHolder();
            _ = new ProjectLspDocument(client, identity, holder, events);
        }
        session.Dispose();
    }

    // Called after the new window has joined the shared document, so releasing the project holder cannot close it.
    public static void Reopened(ILspClient client, DocumentIdentity identity)
    {
        if (Held.TryGetValue(client, out var documents) && documents.TryGetValue(identity, out var held)) held.Dispose();
    }

    private async Task SavedAsync()
    {
        await session.FlushAsync();
        if (disposed) return;
        session.UpdateText(identity.Module is { } module ? FormCodeText.WholeFile(module) : FormCodeText.WholeFile(identity.Form!));
        await session.RenameAsync(DocumentWireName.For(identity));
        await session.NotifySavedAsync();
        Dispose();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        documents.Remove(identity);
        foreach (var subscription in subscriptions) subscription.Dispose();
        ReleaseAsync().ListenErrors();
    }

    private async Task ReleaseAsync()
    {
        await session.FlushAsync();
        session.Dispose();
    }
}
