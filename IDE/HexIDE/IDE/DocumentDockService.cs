using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Core.Events;
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
    // The document area that last held focus, so focus moving to a tool pane doesn't hand the active tab back to the first area.
    private IDocumentDock? lastFocusedArea;

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
            factory.Value.FocusDockable(dock, tab);
            dock.ActiveDockable = tab;
        }, entry);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public IReadOnlyList<BaseEditorWindowViewModel> OpenDocuments => openDocuments;

    public BaseEditorWindowViewModel? ActiveDocument => activeDocument;

    public IReadOnlyList<IDockable> AllTabs =>
        factory.Value is { } f ? [.. f.DocumentAreas.SelectMany(d => d.VisibleDockables ?? [])] : [];

    public IDockable? ActiveTab
    {
        get
        {
            if (factory.Value is not { } f) return Dock?.ActiveDockable;
            var areas = f.DocumentAreas.ToList();
            var focused = f.DocumentLayout?.FocusedDockable;
            var area = areas.FirstOrDefault(d => ReferenceEquals(d, focused?.Owner) || ReferenceEquals(d, focused));
            if (area is not null) lastFocusedArea = area;
            else if (lastFocusedArea is not null && areas.Contains(lastFocusedArea)) area = lastFocusedArea;
            return area?.ActiveDockable ?? Dock?.ActiveDockable;
        }
    }

    public int AreaOf(IDockable tab) => factory.Value is { } f
        ? f.DocumentAreas.TakeWhile(d => d.VisibleDockables?.Contains(tab) != true).Count() : 0;

    public bool TryActivateAny(Func<IDockable, bool> predicate)
    {
        var match = AllTabs.FirstOrDefault(predicate);
        if (match?.Owner is not IDocumentDock dock) return false;

        factory.Value!.FocusDockable(dock, match);
        dock.ActiveDockable = match;
        return true;
    }

    public bool TryCloseAny(Func<IDockable, bool> predicate)
    {
        var match = AllTabs.FirstOrDefault(predicate);
        if (match?.Owner is not IDocumentDock) return false;

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

    public bool TryActivate<T>(Func<T, bool> predicate, bool takeFocus = true) where T : BaseEditorWindowViewModel
    {
        var match = openDocuments.OfType<T>().FirstOrDefault(predicate);
        if (match?.Owner is not IDocumentDock dock) return false;

        if (!takeFocus) return true;
        factory.Value!.FocusDockable(dock, match);
        dock.ActiveDockable = match;
        return true;
    }

    public void OpenDocument(BaseEditorWindowViewModel vm, bool takeFocus = true)
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
        vm.IsActive = takeFocus;

        dock.VisibleDockables!.Add(vm);
        factory.Value!.InitDockable(vm, dock);
        if (takeFocus)
        {
            factory.Value!.FocusDockable(dock, vm);
            dock.ActiveDockable = vm;
        }
    }

    private void EnsureDockSubscription(DocumentDock dock)
    {
        if (subscribedToDock) return;
        // Collection removal also happens during a split or a move. Only a real close disposes an editor.
        factory.Value!.DockableClosed += OnDockableClosed;
        factory.Value.FocusedDockableChanged += OnFocusedDockableChanged;
        dock.PropertyChanged += OnDockPropertyChanged;
        subscribedToDock = true;
    }

    private void UpdateActiveDocument()
    {
        activeDocument = ActiveTab as BaseEditorWindowViewModel;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveDocument)));
    }

    private void OnFocusedDockableChanged(object? sender, FocusedDockableChangedEventArgs e) => UpdateActiveDocument();

    private void OnDockPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentDock.ActiveDockable)) UpdateActiveDocument();
    }

    private void OnCloseRequest(IMdiWindow window)
    {
        if (window is BaseEditorWindowViewModel vm && Dock is not null)
            factory.Value!.CloseDockable(vm);
    }

    private void OnDockableClosed(object? sender, DockableClosedEventArgs e)
    {
        if (e.Dockable is not { } tab) return;
        if (tab.Owner is not IDocumentDock && tab is not BaseEditorWindowViewModel) return;
        RecentlyClosed.Record(tab);
        if (tab is BaseEditorWindowViewModel removed && openDocuments.Remove(removed))
        {
            eventBus.Publish(new FileClosedEvent(removed.Title, removed.OpenDocument));
            removed.CloseRequest -= OnCloseRequest;
            removed.Dispose();
            Log.Debug("DocumentDockService: Disposed '{Title}'", removed.Title);
        }
        UpdateActiveDocument();
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
        if (subscribedToDock && factory.Value is { } f)
        {
            f.DockableClosed -= OnDockableClosed;
            f.FocusedDockableChanged -= OnFocusedDockableChanged;
        }
        if (Dock is { } dock)
            dock.PropertyChanged -= OnDockPropertyChanged;
    }
}
