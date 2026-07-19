using System;
using System.Collections.ObjectModel;
using Avalonia.Media;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
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

        componentRegistry.ComponentRegistered += OnComponentRegistered;

        IImage LoadIcon(string name)
        {
            return IconFactory.Themed($"Geo.{char.ToUpperInvariant(name[0])}{name[1..]}");
        }
    }

    private void OnComponentRegistered(IComponentClass component)
    {
        Components.Add(new ComponentToolViewModel(this, component, _addinComponentIcon.Value));
    }

    protected void OnSelectedComponentChanged()
    {
        foreach (var component in Components)
            component.RaiseIsSelected();
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