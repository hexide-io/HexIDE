using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using Serilog;

namespace HexIDE.IDE;

public sealed class DocumentDockService : IDocumentDockService, IDisposable
{
    private readonly DockFactoryReference factory;
    private readonly IEventBus eventBus;
    private readonly IDisposable projectUnloadedSub;
    private readonly List<BaseEditorWindowViewModel> openDocuments = new();
    private bool subscribedToDock;
    private BaseEditorWindowViewModel? activeDocument;

    public RecentlyClosedDocuments RecentlyClosed { get; } = new();

    public void TrackTabs()
    {
        if (Dock is { } dock) EnsureDockSubscription(dock);
    }

    public void ReopenClosedDocument(IEditorService editors, ClosedDocument? entry = null)
    {
        RecentlyClosed.Reopen(editors, tab =>
        {
            if (TryActivateAny(t => ReferenceEquals(t, tab))) return;
            if (tab is ArchiveDocumentViewModel archive)
            {
                var existing = openDocuments.OfType<ArchiveDocumentViewModel>()
                    .FirstOrDefault(vm => vm.Entry.Identity.Equals(archive.Entry.Identity));
                if (existing is null) OpenDocument(archive);
                else
                {
                    existing.CaretOffset = archive.CaretOffset;
                    TryActivate<ArchiveDocumentViewModel>(vm => vm == existing);
                }
                return;
            }
            if (Dock is not { } dock) return;
            dock.VisibleDockables!.Add(tab);
            factory.Value!.InitDockable(tab, dock);
            factory.Value.SetFocusedDockable(dock, tab);
            dock.ActiveDockable = tab;
        }, entry);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BaseEditorWindowViewModel> OpenDocuments => openDocuments;

    public BaseEditorWindowViewModel? ActiveDocument => activeDocument;

    public IReadOnlyList<IDockable> AllTabs =>
        Dock?.VisibleDockables is { } tabs ? [.. tabs] : [];

    public IDockable? ActiveTab => Dock?.ActiveDockable;

    public bool TryActivateAny(Func<IDockable, bool> predicate)
    {
        var dock = Dock;
        if (dock?.VisibleDockables is null) return false;

        var match = dock.VisibleDockables.FirstOrDefault(predicate);
        if (match is null) return false;

        factory.Value!.SetFocusedDockable(dock, match);
        dock.ActiveDockable = match;
        return true;
    }

    public bool TryCloseAny(Func<IDockable, bool> predicate)
    {
        var dock = Dock;
        if (dock?.VisibleDockables is null) return false;

        var match = dock.VisibleDockables.FirstOrDefault(predicate);
        if (match is null) return false;

        // An editor goes through the service's own path, which keeps its bookkeeping and its events
        // intact. Anything else is a document the shell added straight to the dock, so it leaves the same
        // way it arrived.
        if (match is BaseEditorWindowViewModel editor)
        {
            CloseDocument(editor);
            return true;
        }

        factory.Value!.CloseDockable(match);
        return true;
    }

    public DocumentDockService(DockFactoryReference factory, IEventBus eventBus)
    {
        this.factory = factory;
        this.eventBus = eventBus;
        projectUnloadedSub = eventBus.Subscribe<ProjectUnloadedEvent>(e =>
        {
            RecentlyClosed.Unload(e.Project);
            foreach (var document in openDocuments.Where(d => d.OpenDocument?.Project == e.Project
                         || d is RelatedDocumentEditorViewModel r && r.RelatedDocument?.Owner == e.Project
                         || d is ProjectDocumentViewModel p && p.Project == e.Project).ToArray())
                CloseDocument(document);
        });
    }

    private DocumentDock? Dock => factory.Value?.DocumentDock;

    public bool TryActivate<T>(Func<T, bool> predicate) where T : BaseEditorWindowViewModel
    {
        var dock = Dock;
        if (dock is null) return false;

        var match = openDocuments.OfType<T>().FirstOrDefault(predicate);
        if (match is null) return false;

        factory.Value!.SetFocusedDockable(dock, match);
        dock.ActiveDockable = match;
        return true;
    }

    public void OpenDocument(BaseEditorWindowViewModel vm)
    {
        var dock = Dock;
        if (dock is null)
        {
            Log.Warning("DocumentDockService: DocumentDock not yet initialized, cannot open '{Title}'", vm.Title);
            return;
        }

        EnsureDockSubscription(dock);

        openDocuments.Add(vm);
        eventBus.Publish(new FileOpenedEvent(vm.Title, vm.OpenDocument));
        vm.CloseRequest += OnCloseRequest;

        vm.IsOpen = true;
        vm.IsActive = true;

        dock.VisibleDockables!.Add(vm);
        factory.Value!.InitDockable(vm, dock);
        factory.Value!.SetFocusedDockable(dock, vm);
        dock.ActiveDockable = vm;
    }

    private void EnsureDockSubscription(DocumentDock dock)
    {
        if (subscribedToDock) return;
        if (dock.VisibleDockables is INotifyCollectionChanged ncc)
            ncc.CollectionChanged += OnVisibleDockablesChanged;
        dock.PropertyChanged += OnDockPropertyChanged;
        subscribedToDock = true;
    }

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentDock.ActiveDockable))
        {
            activeDocument = (sender as DocumentDock)?.ActiveDockable as BaseEditorWindowViewModel;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveDocument)));
        }
    }

    private void OnCloseRequest(IMdiWindow window)
    {
        if (window is BaseEditorWindowViewModel vm && Dock is not null)
            factory.Value!.CloseDockable(vm);
    }

    private void OnVisibleDockablesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is null) return;
        foreach (var item in e.OldItems)
        {
            if (item is IDockable tab) RecentlyClosed.Record(tab);
            if (item is BaseEditorWindowViewModel removed && openDocuments.Remove(removed))
            {
                eventBus.Publish(new FileClosedEvent(removed.Title, removed.OpenDocument));
                removed.CloseRequest -= OnCloseRequest;
                removed.Dispose();
                Log.Debug("DocumentDockService: Disposed '{Title}'", removed.Title);
            }
        }
    }

    public void CloseDocument(BaseEditorWindowViewModel vm)
    {
        if (Dock is not null)
            factory.Value!.CloseDockable(vm);
    }

    public void CloseAll()
    {
        var docs = openDocuments.ToList();
        foreach (var doc in docs)
        {
            if (Dock is not null)
                factory.Value!.CloseDockable(doc);
        }
    }

    public void Dispose()
    {
        projectUnloadedSub.Dispose();
        RecentlyClosed.Dispose();
        if (Dock is { } dock)
        {
            dock.PropertyChanged -= OnDockPropertyChanged;
            if (dock.VisibleDockables is INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnVisibleDockablesChanged;
        }
    }
}
