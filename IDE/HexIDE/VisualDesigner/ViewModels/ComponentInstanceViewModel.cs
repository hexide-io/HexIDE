using System;
using Avalonia;
using System.Linq;
using Avalonia.Data;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.VisualDesigner;

public partial class ComponentInstanceViewModel : ObservableObject
{
    private readonly FormEditViewModel parentViewModel;
    private ComponentInstance instance;

    public ComponentInstance Instance => instance;
    public FormEditViewModel Owner => parentViewModel;

    public ComponentInstanceViewModel(FormEditViewModel parentViewModel, ComponentInstance instance)
    {
        this.parentViewModel = parentViewModel;
        this.instance = instance;
        this.instance.OnComponentPropertyChanged += InstanceOnOnComponentPropertyChanged;
        this.Instance.OnComponentPropertyChanging += InstancePropertyChanging;
    }

    private void InstancePropertyChanging(ComponentInstance _, PropertyClass propertyClass, object? oldvalue, object? newValue)
    {
        if (propertyClass == VBProperties.NameProperty)
        {
            if (string.IsNullOrEmpty(newValue as string))
                throw new DataValidationException("Name can't be empty");

            var proposed = newValue as string;

            // A commit that does not change the name collides with nothing, and that is the case this guard
            // kept rejecting. The property grid commits Name on every focus change, so it fired constantly;
            // and it fired on real VB6 data, because a control ARRAY shares one name across its elements —
            // Options Dialog.frm has four sibling controls all called picOptions, and Treeview Listview
            // Splitter.frm has two lblTitle inside one picTitles — so the moment one of those was touched,
            // "unique in form" flagged it as a duplicate of its own siblings.
            if (string.Equals(oldvalue as string, proposed, System.StringComparison.Ordinal))
                return;

            // For a genuine rename the check stands, and it stays strict on purpose. VB6's real rule is
            // uniqueness per name AND Index; Index is not modelled yet, so a rename that would JOIN an
            // existing array cannot be told apart from a collision, and refusing is the recoverable answer.
            if (parentViewModel.AllComponents.Any(c => !ReferenceEquals(c, this) && c.Name == proposed))
                throw new DataValidationException("Name must be unique in form");
        }
    }

    private void InstanceOnOnComponentPropertyChanged(ComponentInstance _, PropertyClass propertyClass)
    {
        if (propertyClass == VBProperties.LeftProperty)
        {
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(RelativeLeft));
            NotifyDescendantGeometry();
        }
        else if (propertyClass == VBProperties.TopProperty)
        {
            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(RelativeTop));
            NotifyDescendantGeometry();
        }
        else if (propertyClass == VBProperties.WidthProperty)
        {
            OnPropertyChanged(nameof(Width));
            NotifyDescendantGeometry();
        }
        else if (propertyClass == VBProperties.HeightProperty)
        {
            OnPropertyChanged(nameof(Height));
            NotifyDescendantGeometry();
        }
        else if (propertyClass == VBProperties.NameProperty)
            OnPropertyChanged(nameof(Name));
        else if (propertyClass == VBProperties.CaptionProperty)
            OnPropertyChanged(nameof(Caption));
        else if (propertyClass == VBProperties.BackColorProperty)
            OnPropertyChanged(nameof(BackColor));
        else if (propertyClass == VBProperties.ForeColorProperty)
            OnPropertyChanged(nameof(ForeColor));
    }

    /// <summary>
    /// Raises Left, Top and ContainerBounds on everything inside this component, recursively.
    ///
    /// Not an optimisation — the half of the mechanism that makes a flat canvas with computed positions
    /// render at all. The model raises a change only for the instance whose own property changed, so without
    /// this, moving a Frame silently changes every descendant's absolute position: the TwoWay
    /// <c>(Canvas.Left)</c> setter never fires, the children stay drawn where they were, and the marquee and
    /// the align commands read the new value. Selection rectangles stop matching what is painted, and the
    /// children only jump into place on the next reload.
    /// </summary>
    private void NotifyDescendantGeometry()
    {
        foreach (var child in instance.ContainedControls)
        {
            if (parentViewModel.TryGetViewModel(child, out var childVm))
            {
                childVm.OnPropertyChanged(nameof(Left));
                childVm.OnPropertyChanged(nameof(Top));
                childVm.OnPropertyChanged(nameof(ContainerBounds));
                childVm.NotifyDescendantGeometry();
            }
        }
    }

    private ComponentInstanceViewModel? containerViewModel;
    private bool containerResolved;

    /// <summary>
    /// The view-model of the component this one sits inside, or null when that is the form itself (whose
    /// client area IS the canvas) or when nothing has recorded a container — which is every control on a
    /// form the designer built.
    /// </summary>
    private ComponentInstanceViewModel? ContainerViewModel
    {
        get
        {
            if (containerResolved)
                return containerViewModel;
            containerResolved = true;
            if (instance.Container is { } container && container.BaseClass is not FormComponentClass &&
                parentViewModel.TryGetViewModel(container, out var vm))
                containerViewModel = vm;
            return containerViewModel;
        }
    }

    /// <summary>Forgets the cached container link. For whatever eventually re-parents a control.</summary>
    internal void InvalidateContainer()
    {
        containerResolved = false;
        containerViewModel = null;
        OnPropertyChanged(nameof(Left));
        OnPropertyChanged(nameof(Top));
        OnPropertyChanged(nameof(ContainerBounds));
    }

    /// <summary>
    /// This component's container's client rectangle, in canvas space — its accumulated origin plus its
    /// usable size.
    ///
    /// The one place the accumulated origin is computed. Everything that needs the container's space rather
    /// than the canvas's — the snap, the resize clamp — reads it from here rather than recomputing it, so
    /// there is nothing to drift out of step with <see cref="Left"/>.
    /// </summary>
    public Rect ContainerBounds
    {
        get
        {
            if (ContainerViewModel is not { } container)
                return new Rect(0, 0, parentViewModel.Form.Width, parentViewModel.Form.Height);

            var inset = container.ClientInset;
            return new Rect(
                container.Left + inset.Left,
                container.Top + inset.Top,
                Math.Max(0, container.Width - inset.Left - inset.Right),
                Math.Max(0, container.Height - inset.Top - inset.Bottom));
        }
    }

    /// <summary>How far inside its own bounds this component measures its contents from.</summary>
    private Thickness ClientInset =>
        instance.BaseClass is ComponentBaseClass baseClass ? baseClass.ClientInset(instance) : default;

    /// <summary>
    /// The canvas position, which is NOT what the model stores. The model stores the VB6 value — relative to
    /// whatever contains the control — and the designer draws on one flat canvas, so the boundary between
    /// the two conversions is here and only here.
    ///
    /// Making the view-model relative instead, and rebasing its consumers, was the alternative: correct
    /// end to end and about thirty edits, including the marquee, all sixteen align and spacing commands, the
    /// drag write-back and two shipped MCP tool surfaces whose meaning would have changed silently.
    /// </summary>
    public double Left
    {
        get => ContainerBounds.X + instance.GetPropertyOrDefault(VBProperties.LeftProperty);
        set => instance.SetProperty(VBProperties.LeftProperty, value - ContainerBounds.X);
    }

    public double Top
    {
        get => ContainerBounds.Y + instance.GetPropertyOrDefault(VBProperties.TopProperty);
        set => instance.SetProperty(VBProperties.TopProperty, value - ContainerBounds.Y);
    }

    /// <summary>The VB6 value the model holds — container-relative, which is what the status bar shows.</summary>
    public double RelativeLeft => instance.GetPropertyOrDefault(VBProperties.LeftProperty);

    public double RelativeTop => instance.GetPropertyOrDefault(VBProperties.TopProperty);

    public double Width
    {
        get => instance.GetPropertyOrDefault(VBProperties.WidthProperty);
        set => instance.SetProperty(VBProperties.WidthProperty, value);
    }

    public double Height
    {
        get => instance.GetPropertyOrDefault(VBProperties.HeightProperty);
        set => instance.SetProperty(VBProperties.HeightProperty, value);
    }

    public string Name => instance.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";

    public string? Caption => instance.GetPropertyOrDefault(VBProperties.CaptionProperty);

    public IBrush BackColor => instance.GetPropertyOrDefault(VBProperties.BackColorProperty).ToBrush();

    public IBrush ForeColor => instance.GetPropertyOrDefault(VBProperties.ForeColorProperty).ToBrush();

    public string BaseClassName => instance.BaseClass.Name;

}