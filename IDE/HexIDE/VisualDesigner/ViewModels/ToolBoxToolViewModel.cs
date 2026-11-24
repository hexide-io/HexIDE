using System;
using System.Collections.ObjectModel;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;

namespace HexIDE.VisualDesigner;

public partial class ToolBoxToolViewModel : Tool
{
    private readonly IDocumentDockService documentDockService;
    private readonly IComponentRegistry componentRegistry;
    // The items owners registered (#5594), after the arrow; the referenced libraries' controls start after them. Kept by
    // entry, since a class that isn't a designer component has no BaseClass to find its button by.
    private readonly List<(ToolboxEntry Entry, ComponentToolViewModel Tool)> registeredTools = [];
    private int LibraryToolsStart => 1 + registeredTools.Count;
    [Notify] [AlsoNotify(nameof(IsCursorSelected))] private ComponentToolViewModel? selectedComponent;
    public ObservableCollection<ComponentToolViewModel> Components { get; } = new();
    public bool IsCursorSelected => selectedComponent?.BaseClass is null;
    public ComponentToolViewModel Arrow { get; }

    public ToolBoxToolViewModel(IDocumentDockService documentDockService, IComponentRegistry componentRegistry,
        ILocalizationService localization)
    {
        this.documentDockService = documentDockService;
        this.componentRegistry = componentRegistry;
        localization.BindTitle(this, "Str.Toolbox.Title");
        CanPin = false;
        CanClose = true;

        Arrow = new ComponentToolViewModel(this, "Mouse", IconFactory.Themed("Geo.Cursor"));
        Components.Add(Arrow);
        foreach (var entry in componentRegistry.Toolbox)
            OnToolboxItemAdded(entry);

        selectedComponent = Arrow;

        documentDockService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IDocumentDockService.ActiveDocument)
                && documentDockService.ActiveDocument is FormEditViewModel { FormDefinition.Owner: { } project })
                ShowProject(project);
        };

        // The IDE builds the toolbox before the provider registers, so its items arrive here, as an add-in's do.
        componentRegistry.ToolboxItemAdded += OnToolboxItemAdded;
        componentRegistry.ToolboxItemRemoved += OnToolboxItemRemoved;
    }

    private ProjectDefinition? project;
    private readonly List<ComponentToolViewModel> inBoxTools = new();

    /// <summary>The project whose in-box controls the toolbox offers: the one whose form was designed last.</summary>
    public ProjectDefinition? Project => project;

    /// <summary>
    /// Offer the in-box controls HexIDE implements from each library <paramref name="shown"/> references, one button for each
    /// library that declares one (#1103). VB6's toolbox shows a library's controls while the project references it, and the
    /// Components dialog is where a project comes to reference one.
    /// </summary>
    public void ShowProject(ProjectDefinition? shown)
    {
        if (!ReferenceEquals(project, shown))
        {
            if (project is not null)
                project.ObjectReferencesChanged -= OnObjectReferencesChanged;
            project = shown;
            if (project is not null)
                project.ObjectReferencesChanged += OnObjectReferencesChanged;
        }

        if (SelectedComponent is { Library: not null })
            SelectedComponent = Arrow;
        foreach (var tool in inBoxTools)
            Components.Remove(tool);
        inBoxTools.Clear();
        if (project is null)
            return;

        var libraries = project.ObjectReferences
            .Select(reference => componentRegistry.Libraries.FirstOrDefault(library => library.IsNamedBy(reference)))
            .OfType<IControlLibrary>().Distinct();
        foreach (var library in libraries)
        foreach (var control in componentRegistry.LibraryControls.Where(c => ReferenceEquals(c.Library, library)))
            inBoxTools.Add(new ComponentToolViewModel(this, control.Class, IconFactory.Themed(control.IconKey))
                { Library = library, LibraryTypeName = control.TypeName, IconKey = control.IconKey });
        var at = LibraryToolsStart;
        foreach (var tool in inBoxTools)
            Components.Insert(at++, tool);
    }

    /// <summary>
    /// Every library's entry for the control <paramref name="type"/> names, by its name or a type name, whether or not a
    /// project references one: what a caller is told when the control isn't in the toolbox.
    /// </summary>
    public IReadOnlyList<LibraryControl> LibrariesOffering(string type)
    {
        var found = componentRegistry.LibraryControls.FirstOrDefault(c =>
            string.Equals(c.ControlName, type, StringComparison.OrdinalIgnoreCase)
            || string.Equals(c.TypeName, type, StringComparison.OrdinalIgnoreCase));
        return found is null ? [] : [.. componentRegistry.LibraryControls.Where(c => ReferenceEquals(c.Class, found.Class))];
    }

    private void OnObjectReferencesChanged(ProjectDefinition changed) => ShowProject(changed);

    private ComponentToolViewModel Tool(ToolboxEntry entry) =>
        new(this, entry.Class, IconFactory.Themed(entry.IconKey)) { IconKey = entry.IconKey };

    private void OnToolboxItemAdded(ToolboxEntry entry)
    {
        var tool = Tool(entry);
        Components.Insert(LibraryToolsStart, tool);
        registeredTools.Add((entry, tool));
    }

    private void OnToolboxItemRemoved(ToolboxEntry entry)
    {
        var at = registeredTools.FindIndex(registered => ReferenceEquals(registered.Entry.Class, entry.Class));
        if (at < 0)
            return;
        var tool = registeredTools[at].Tool;
        registeredTools.RemoveAt(at);
        if (SelectedComponent == tool) SelectedComponent = Arrow;
        Components.Remove(tool);
    }

    protected void OnSelectedComponentChanged()
    {
        foreach (var component in Components)
            component.RaiseIsSelected();
    }

    public void SpawnControl(ComponentToolViewModel tool)
    {
        if (tool.BaseClass is { } baseClass && documentDockService.ActiveDocument is FormEditViewModel formEditViewModel)
            formEditViewModel.SpawnControlCenter(baseClass, tool.Library);
    }

    public void SpawnControl(IDesignerComponent baseClass)
    {
        // that's not the best pattern to check the active window and call the method directly
        // but publishing an event also seems like a poor idea (then all windows need to react and see if they are active or not)
        // fix this once I have a better idea
        if (documentDockService.ActiveDocument is FormEditViewModel formEditViewModel)
        {
            formEditViewModel.SpawnControlCenter(baseClass);
        }
    }
}