using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using Avalonia;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public abstract class ComponentBaseClass : IComponentClass
{
    public ComponentBaseClass(IReadOnlyList<PropertyClass> extendedProperties,
        IReadOnlyList<EventClass>? events = null)
    {
        Properties = new List<PropertyClass>(extendedProperties)
        {
            NameProperty,
            LeftProperty,
            TopProperty,
            WidthProperty,
            HeightProperty,
            VisibleProperty,
            TagProperty
        };
        PropertiesByName = Properties.ToDictionary(p => p.Name, p => p);
        Events = events ?? [];
    }

    public IReadOnlyList<PropertyClass> Properties { get; }

    public IReadOnlyDictionary<string, PropertyClass> PropertiesByName { get; }

    public IReadOnlyList<EventClass> Events { get; }

    public abstract string Name { get; }
    public abstract string VBTypeName { get; }

    /// <summary>Visible controls (default) have Width/Height/Visible; invisible ones (Timer) override false.</summary>
    public virtual bool IsVisual => true;

    /// <summary>
    /// The Canvas this control hosts its contained controls on, when it is a container. False for everything
    /// else, which is most classes.
    ///
    /// Declared here rather than on <see cref="IComponentClass"/> on purpose. HexIDE.Core declares no
    /// third-party dependencies and has no Avalonia reference, so it cannot name a Canvas; and
    /// IComponentClass is the interface an add-in implements, where this would be a question no add-in can
    /// answer — a class HexIDE did not build is not a container, and a control nested under one keeps the
    /// form read-only.
    /// </summary>
    public virtual bool TryGetChildHost(Control control, [NotNullWhen(true)] out Canvas? host)
    {
        host = null;
        return false;
    }

    /// <summary>
    /// How far inside this control's own bounds its contained controls are measured from — zero for
    /// everything that is not a bordered container.
    ///
    /// The run time gets this for free by hosting children inside the border decorator, which insets its
    /// child by what it draws. The designer keeps one flat canvas and has to do the arithmetic, so it needs
    /// the same number as a value; this is the single place it comes from, so the two cannot disagree.
    /// </summary>
    public virtual Thickness ClientInset(ComponentInstance instance) => default;

    /// <summary>
    /// The Canvas a container class hands to its control at instantiation.
    ///
    /// <c>TabNavigation = Continue</c> is not a detail. VB6's tab order is a single flat form-wide
    /// <c>TabIndex</c> sequence, and Avalonia resolves TabIndex among siblings within a navigation scope
    /// before descending into one. Every control being a sibling on one canvas is the only reason the flat
    /// order falls out for free today; the moment children move onto a container's own canvas, a scope here
    /// would make ODBC Log In.frm tab 13, 12, 14 and only then descend into 0-11.
    /// </summary>
    protected static Canvas CreateChildHost()
    {
        var host = new Canvas { ClipToBounds = true };
        KeyboardNavigation.SetTabNavigation(host, KeyboardNavigationMode.Continue);
        return host;
    }

    protected abstract Control InstantiateInternal(ComponentInstance instance);

    public Control Instantiate(ComponentInstance instance)
    {
        var control = InstantiateInternal(instance);
        control.IsEnabled = instance.GetPropertyOrDefault(EnabledProperty);
        ToolTip.SetTip(control, instance.GetPropertyOrDefault(ToolTipTextProperty));
        if (instance.GetPropertyOrDefault(TagProperty) is {} tag)
            control.Tag = tag;
        KeyboardNavigation.SetTabIndex(control, instance.GetPropertyOrDefault(TabIndexProperty));
        KeyboardNavigation.SetIsTabStop(control, instance.GetPropertyOrDefault(TabStopProperty));
        VBProps.SetName(control, instance.GetPropertyOrDefault(NameProperty));
        return control;
    }

    public static EventClass ClickEvent = new EventClass("Click");
    public static EventClass GotFocusEvent = new EventClass("GotFocus");
    public static EventClass LostFocusEvent = new EventClass("LostFocus");
    public static EventClass KeyDownEvent = new EventClass("KeyDown", new EventClassArgument("KeyCode", "Integer"), new EventClassArgument("Shift", "Integer"));
    public static EventClass KeyPressEvent = new EventClass("KeyPress", new EventClassArgument("KeyAscii", "Integer"));
    public static EventClass KeyUpEvent = new EventClass("KeyUp", new EventClassArgument("KeyCode", "Integer"), new EventClassArgument("Shift", "Integer"));
    public static EventClass MouseDownEvent = new EventClass("MouseDown", new EventClassArgument("Button", "Integer"), new EventClassArgument("Shift", "Integer"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"));
    public static EventClass MouseMoveEvent = new EventClass("MouseMove", new EventClassArgument("Button", "Integer"), new EventClassArgument("Shift", "Integer"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"));
    public static EventClass MouseUpEvent = new EventClass("MouseUp", new EventClassArgument("Button", "Integer"), new EventClassArgument("Shift", "Integer"), new EventClassArgument("X", "Single"), new EventClassArgument("Y", "Single"));
}