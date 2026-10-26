using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;

namespace HexIDE.VisualDesigner;

public partial class ToolBoxToolViewModel : Tool
{
    private readonly IDocumentDockService documentDockService;
    private readonly Lazy<IImage> _addinComponentIcon;
    [Notify] [AlsoNotify(nameof(IsCursorSelected))] private ComponentToolViewModel? selectedComponent;
    public ObservableCollection<ComponentToolViewModel> Components { get; } = new();
    public bool IsCursorSelected => selectedComponent?.BaseClass is null;
    public ComponentToolViewModel Arrow { get; }

    public ToolBoxToolViewModel(IDocumentDockService documentDockService, IComponentRegistry componentRegistry,
        ILocalizationService localization)
    {
        this.documentDockService = documentDockService;
        localization.BindTitle(this, "Str.Toolbox.Title");
        CanPin = false;
        CanClose = true;

        _addinComponentIcon = new Lazy<IImage>(() => LoadIcon("object"));

        Arrow = new ComponentToolViewModel(this, "Mouse", LoadIcon("cursor"));
        Components.Add(Arrow);
        Components.Add(new ComponentToolViewModel(this, PictureBoxComponentClass.Instance, LoadIcon("picture")));
        Components.Add(new ComponentToolViewModel(this, LabelComponentClass.Instance, LoadIcon("label")));
        Components.Add(new ComponentToolViewModel(this, TextBoxComponentClass.Instance, LoadIcon("textbox")));
        Components.Add(new ComponentToolViewModel(this, FrameComponentClass.Instance, LoadIcon("groupbox")));
        Components.Add(new ComponentToolViewModel(this, CommandButtonComponentClass.Instance, LoadIcon("button")));
        Components.Add(new ComponentToolViewModel(this, CheckBoxComponentClass.Instance, LoadIcon("checkbox")));
        Components.Add(new ComponentToolViewModel(this, OptionButtonComponentClass.Instance, LoadIcon("radio")));
        Components.Add(new ComponentToolViewModel(this, ComboBoxComponentClass.Instance, LoadIcon("combo")));
        Components.Add(new ComponentToolViewModel(this, ListBoxComponentClass.Instance, LoadIcon("listbox")));
        Components.Add(new ComponentToolViewModel(this, HScrollBarComponentClass.Instance, LoadIcon("hscroll")));
        Components.Add(new ComponentToolViewModel(this, VScrollBarComponentClass.Instance, LoadIcon("vscroll")));
        Components.Add(new ComponentToolViewModel(this, TimerComponentClass.Instance, LoadIcon("timer")));
        Components.Add(new ComponentToolViewModel(this, ShapeComponentClass.Instance, LoadIcon("shape")));

        selectedComponent = Arrow;

        documentDockService.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(IDocumentDockService.ActiveDocument)
                && documentDockService.ActiveDocument is FormEditViewModel { FormDefinition.Owner: { } project })
                ShowProject(project);
        };

        componentRegistry.ComponentRegistered += OnComponentRegistered;
        componentRegistry.ComponentUnregistered += OnComponentUnregistered;

        IImage LoadIcon(string name)
        {
            return IconFactory.Themed($"Geo.{char.ToUpperInvariant(name[0])}{name[1..]}");
        }
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

        var libraries = project.ObjectReferences.Select(ControlLibraries.For).OfType<ControlLibrary>().Distinct();
        foreach (var library in libraries)
        foreach (var inBox in InBoxComponentClass.Implemented.Where(c => library.Controls.ContainsKey(c.ControlClassName)))
            inBoxTools.Add(new ComponentToolViewModel(this, inBox, IconFactory.Themed("Geo.Object")) { Library = library });
        var at = Components.IndexOf(Components.Last(c => c.BaseClass is ShapeComponentClass)) + 1;
        foreach (var tool in inBoxTools)
            Components.Insert(at++, tool);
    }

    private void OnObjectReferencesChanged(ProjectDefinition changed) => ShowProject(changed);

    private void OnComponentRegistered(IComponentClass component)
    {
        Components.Add(new ComponentToolViewModel(this, component, _addinComponentIcon.Value));
    }

    private void OnComponentUnregistered(IComponentClass component)
    {
        var tool = Components.LastOrDefault(c => ReferenceEquals(c.BaseClass, component));
        if (tool is null) return;
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

    public void SpawnControl(ComponentBaseClass baseClass)
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