using Avalonia.Media;
using HexIDE.Runtime.Components;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.VisualDesigner;

public partial class ComponentToolViewModel : ObservableObject
{
    private readonly ToolBoxToolViewModel parent;

    public ComponentToolViewModel(ToolBoxToolViewModel parent, IDesignerComponent baseClass, IImage icon)
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
        BaseClass = component as IDesignerComponent;
        Icon = icon;
        Name = component.Name;
    }

    /// <summary>The class a control placed from this button has. The designer asks it through Core's contract (#5591).</summary>
    public IDesignerComponent? BaseClass { get; }

    /// <summary>The library an in-box control is placed from, which its type name and its form's header name (#1103).</summary>
    public IControlLibrary? Library { get; init; }

    /// <summary>The type name a control placed from this button takes: <c>ComctlLib.ProgressBar</c> from COMCTL32.OCX's.</summary>
    public string? VBTypeName => LibraryTypeName ?? BaseClass?.VBTypeName;

    /// <summary>The library's own type name for the control, where this button places one from a library.</summary>
    public string? LibraryTypeName { get; init; }

    public IImage Icon { get; }

    /// <summary>The key of the icon the registry named for this button, where it named one.</summary>
    public string? IconKey { get; init; }
    public string Name { get; }

    /// <summary>
    /// The control's class for its tooltip and automation name, matching VB6's measured toolbox tips (#2751,
    /// oracle PR #4729). <see cref="Name"/> remains the prefix a new control's name takes.
    /// </summary>
    public string DisplayName => VBTypeName is { } typeName && typeName.LastIndexOf('.') is var dot and >= 0
        ? typeName[(dot + 1)..]
        : Name;

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