using Avalonia.Media;
using HexIDE.Runtime.Components;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.VisualDesigner;

public partial class ComponentToolViewModel : ObservableObject
{
    private readonly ToolBoxToolViewModel parent;

    public ComponentToolViewModel(ToolBoxToolViewModel parent, ComponentBaseClass baseClass, IImage icon)
    {
        this.parent = parent;
        BaseClass = baseClass;
        Icon = icon;
        Name = baseClass.Name;
    }

    public ComponentToolViewModel(ToolBoxToolViewModel parent, string name, IImage icon)
    {
        this.parent = parent;
        Icon = icon;
        Name = name;
    }

    public ComponentToolViewModel(ToolBoxToolViewModel parent, IComponentClass component, IImage icon)
    {
        this.parent = parent;
        BaseClass = component as ComponentBaseClass;
        Icon = icon;
        Name = component.Name;
    }

    public ComponentBaseClass? BaseClass { get; }
    public IImage Icon { get; }
    public string Name { get; }

    public bool IsSelected
    {
        get => parent.SelectedComponent == this;
        set
        {
            if (value)
                parent.SelectedComponent = this;
            else
                parent.SelectedComponent = parent.Components[0];
        }
    }

    public void RaiseIsSelected() => OnPropertyChanged(nameof(IsSelected));
}