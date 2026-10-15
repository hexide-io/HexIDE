using System;
using Avalonia;
using System.Linq;
using Avalonia.Data;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
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
        if (propertyClass == VBProperties.NameProperty
            && ComponentNaming.RefusalFor(instance, oldvalue as string, newValue as string,
                parentViewModel.AllComponents.Select(c => c.Instance), parentViewModel.FormDefinition,
                ReferenceEquals(this, parentViewModel.Form), parentViewModel.Localization) is { } refusal)
            throw new DataValidationException(refusal);
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
        else if (IsLine && (propertyClass == VBProperties.X1Property || propertyClass == VBProperties.Y1Property
                            || propertyClass == VBProperties.X2Property || propertyClass == VBProperties.Y2Property
                            || propertyClass == VBProperties.BorderWidthProperty))
        {
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
            OnPropertyChanged(nameof(RelativeLeft));
            OnPropertyChanged(nameof(RelativeTop));
            OnPropertyChanged(nameof(Width));
            OnPropertyChanged(nameof(Height));
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

    /// <summary>The name of what contains this component: a Frame or PictureBox, or else the form.</summary>
    public string ContainerName => ContainerViewModel?.Name ?? parentViewModel.Form.Name;

    /// <summary>
    /// Whether this component lies wholly inside its container's client area. A control outside it is
    /// legal in VB6 and invisible at run time, and is what passing twips where pixels are meant produces. (#675)
    /// </summary>
    public bool LiesWithinContainer => ContainerBounds.Contains(new Rect(Left, Top, Width, Height));

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
        get => ContainerBounds.X + RelativeLeft;
        set
        {
            if (IsLine)
                MoveLine(value - Left, 0);
            else
                instance.SetProperty(VBProperties.LeftProperty, value - ContainerBounds.X);
        }
    }

    public double Top
    {
        get => ContainerBounds.Y + RelativeTop;
        set
        {
            if (IsLine)
                MoveLine(0, value - Top);
            else
                instance.SetProperty(VBProperties.TopProperty, value - ContainerBounds.Y);
        }
    }

    /// <summary>The VB6 value the model holds — container-relative, which is what the status bar shows.</summary>
    public double RelativeLeft => IsLine ? LineBounds.X : instance.GetPropertyOrDefault(VBProperties.LeftProperty);

    public double RelativeTop => IsLine ? LineBounds.Y : instance.GetPropertyOrDefault(VBProperties.TopProperty);

    public double Width
    {
        get => IsLine ? LineBounds.Width : instance.GetPropertyOrDefault(VBProperties.WidthProperty);
        set
        {
            // A Line is moved by its rectangle but not resized by it: its endpoints are #163's to drag.
            if (IsLine)
                OnPropertyChanged(nameof(Width));
            else
                instance.SetProperty(VBProperties.WidthProperty, value);
        }
    }

    public double Height
    {
        get => IsLine ? LineBounds.Height : instance.GetPropertyOrDefault(VBProperties.HeightProperty);
        set
        {
            if (IsLine)
                OnPropertyChanged(nameof(Height));
            else
                instance.SetProperty(VBProperties.HeightProperty, value);
        }
    }

    /// <summary>
    /// A Line has no Left, Top, Width or Height (#163). The designer selects and moves it by the rectangle its endpoints
    /// and pen cover, in pixels, from the endpoints in its container's scale.
    /// </summary>
    public bool IsLine => instance.BaseClass is LineComponentClass;

    /// <summary>How many twips one of a Line's container's units is, or twips where HexIDE cannot run that scale.</summary>
    public (double X, double Y) LineTwipsPerUnit
    {
        get
        {
            try
            {
                return parentViewModel.FormDefinition is { } form ? LineComponentClass.TwipsPerUnit(instance, form) : (1, 1);
            }
            catch (NotImplementedException)
            {
                return (1, 1);
            }
        }
    }

    private Rect LineBounds
    {
        get
        {
            var (x, y) = LineTwipsPerUnit;
            return VBLine.BoundsFor(instance.GetPropertyOrDefault(VBProperties.X1Property) * x,
                instance.GetPropertyOrDefault(VBProperties.Y1Property) * y,
                instance.GetPropertyOrDefault(VBProperties.X2Property) * x,
                instance.GetPropertyOrDefault(VBProperties.Y2Property) * y,
                (int)instance.GetPropertyOrDefault(VBProperties.BorderWidthProperty));
        }
    }

    // Both endpoints by the same distance, in pixels, written back in the container's scale.
    private void MoveLine(double dxPixels, double dyPixels)
    {
        var (x, y) = LineTwipsPerUnit;
        var dx = dxPixels * VBScaleModeExtensions.PixelToTwips / x;
        var dy = dyPixels * VBScaleModeExtensions.PixelToTwips / y;
        if (dx != 0)
        {
            instance.SetProperty(VBProperties.X1Property, instance.GetPropertyOrDefault(VBProperties.X1Property) + dx);
            instance.SetProperty(VBProperties.X2Property, instance.GetPropertyOrDefault(VBProperties.X2Property) + dx);
        }
        if (dy != 0)
        {
            instance.SetProperty(VBProperties.Y1Property, instance.GetPropertyOrDefault(VBProperties.Y1Property) + dy);
            instance.SetProperty(VBProperties.Y2Property, instance.GetPropertyOrDefault(VBProperties.Y2Property) + dy);
        }
    }

    public string Name => instance.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";

    public string? Caption => instance.GetPropertyOrDefault(VBProperties.CaptionProperty);

    public IBrush BackColor => instance.GetPropertyOrDefault(VBProperties.BackColorProperty).ToBrush();

    public IBrush ForeColor => instance.GetPropertyOrDefault(VBProperties.ForeColorProperty).ToBrush();

    public string BaseClassName => instance.BaseClass.Name;

}