using System;
using System.Collections.Generic;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public partial class ComponentInstance
{
    public IComponentClass BaseClass { get; }

    public event Action<ComponentInstance, PropertyClass, object?, object?> OnComponentPropertyChanging = delegate { };
    public event Action<ComponentInstance, PropertyClass> OnComponentPropertyChanged = delegate { };

    private Dictionary<PropertyClass, object?> properties { get; } = new();

    // Raw lines of unrecognised properties, preserved verbatim for round-trip fidelity.
    public List<string> UnknownRawPropertyLines { get; } = [];

    public ComponentInstance(IComponentClass baseClass, string name)
    {
        BaseClass = baseClass;
        SetProperty(NameProperty, name);
    }

    private readonly List<ComponentInstance> containedControls = [];

    /// <summary>
    /// The component this one is drawn inside, or null when it sits directly on the designer root.
    ///
    /// Deliberately a typed member rather than an entry in the property bag. <see cref="GetAllSetProperties"/>
    /// is a shallow copy and the designer clipboard replays every entry it returns through
    /// <see cref="SetUntypedProperty"/>, so a containment list kept there would hand a pasted container the
    /// *original's* children. It is not on <see cref="IComponentClass.Properties"/> either, or the writer's
    /// property loop and the Properties window would both try to render it.
    /// </summary>
    public ComponentInstance? Container { get; private set; }

    /// <summary>
    /// The components drawn inside this one, in document order.
    ///
    /// The order is load-bearing twice over: it is the z-order, and it is the order the writer emits nested
    /// <c>Begin</c> blocks in. Menus are NOT in here — a menu is not drawn inside anything, and its tree
    /// lives on <c>MenuComponentClass.SubItemsProperty</c>; the serializer bridges the two with one
    /// children-of helper rather than by merging the mechanisms.
    /// </summary>
    public IReadOnlyList<ComponentInstance> ContainedControls => containedControls;

    /// <summary>
    /// The one way to change containment. Maintains both directions at once, so the back-pointer and the
    /// child list can never disagree — a drift there makes the writer emit a control twice or not at all.
    ///
    /// Enforces one container per component (attaching detaches from the previous one) and refuses a cycle,
    /// neither of which the writer's recursion guards against on its own.
    /// </summary>
    /// <param name="container">The new container, or null to detach.</param>
    /// <param name="index">Where among the container's existing contents to insert; negative appends.</param>
    public void SetContainer(ComponentInstance? container, int index = -1)
    {
        for (var ancestor = container; ancestor is not null; ancestor = ancestor.Container)
        {
            if (ReferenceEquals(ancestor, this))
                throw new InvalidOperationException(
                    $"'{GetPropertyOrDefault(NameProperty)}' cannot be placed inside itself or its own contents.");
        }

        Container?.containedControls.Remove(this);
        Container = container;

        if (container is null)
            return;

        if (index < 0 || index >= container.containedControls.Count)
            container.containedControls.Add(this);
        else
            container.containedControls.Insert(index, this);
    }

    /// <summary>
    /// A <c>Begin</c> block for a control class HexIDE does not model, preserved verbatim, together with
    /// the two positions it held.
    ///
    /// <paramref name="Ordinal"/> is its place among its container's children, which is what lets the
    /// writer interleave it back among the modelled siblings instead of dumping every preserved block at
    /// the end of the form.
    ///
    /// <paramref name="DocumentOrder"/> is its place in the file as a whole. The two differ, and only this
    /// one can reconstruct the flat view: aggregating per container walks containers in turn, so a block
    /// read from inside a Frame would come back *after* a form-level block that followed the Frame in the
    /// file.
    /// </summary>
    public readonly record struct PreservedSubtree(int Ordinal, int DocumentOrder, string Text);

    private readonly List<PreservedSubtree> preservedChildSubtrees = [];

    /// <summary>
    /// Unmodelled child blocks read from inside this component, in ordinal order.
    ///
    /// Held on the container rather than on the form because an unmodelled control inside a Frame that is
    /// re-emitted at form level has been silently re-parented — it keeps its frame-relative coordinates and
    /// lands somewhere else entirely. Modelling a control type used to be what lost its children; this is
    /// the half of that inversion that applies to the ones still unmodelled.
    /// </summary>
    public IReadOnlyList<PreservedSubtree> PreservedChildSubtrees => preservedChildSubtrees;

    public void AddPreservedChildSubtree(int ordinal, int documentOrder, string text)
        => preservedChildSubtrees.Add(new PreservedSubtree(ordinal, documentOrder, text));

    public ComponentInstance SetProperty<T>(PropertyClass<T> propertyClass, T? value)
    {
        var oldValue = properties.GetValueOrDefault(propertyClass, UnsetValue.Instance);
        OnComponentPropertyChanging?.Invoke(this, propertyClass, oldValue, value);
        properties[propertyClass] = value;
        OnComponentPropertyChanged?.Invoke(this, propertyClass);
        return this;
    }

    public void SetUntypedProperty(PropertyClass propertyClass, object? untypedValue)
    {
        if (untypedValue == null || propertyClass.PropertyType.IsInstanceOfType(untypedValue))
        {
            if (ReferenceEquals(propertyClass, NameProperty) &&
                string.IsNullOrEmpty(untypedValue as string))
                throw new InvalidOperationException("Name can't be empty");
            var oldValue = properties.GetValueOrDefault(propertyClass, UnsetValue.Instance);
            OnComponentPropertyChanging?.Invoke(this, propertyClass, oldValue, untypedValue);
            properties[propertyClass] = untypedValue;
            OnComponentPropertyChanged?.Invoke(this, propertyClass);
        }
    }

    public object? GetBoxedPropertyOrDefault(PropertyClass property)
    {
        if (properties.TryGetValue(property, out var result))
        {
            return result;
        }

        return property.BoxedDefaultValue(BaseClass);
    }

    public T? GetPropertyOrDefault<T>(PropertyClass<T> property)
    {
        if (TryGetProperty<T>(property, out var result))
            return result;
        return property.DefaultValue(BaseClass);
    }

    public bool TryGetProperty<T>(PropertyClass<T> property, out T value)
    {
        if (properties.TryGetValue(property, out var result))
        {
            if (result == null)
            {
                value = default!;
                return true;
            }

            if (result is T t)
            {
                value = t;
                return true;
            }
        }

        value = default!;
        return false;
    }

    public bool TryGetBoxedProperty(PropertyClass property, out object? value)
    {
        if (properties.TryGetValue(property, out var result))
        {
            value = result;
            return true;
        }

        value = default!;
        return false;
    }

    public IReadOnlyDictionary<PropertyClass, object?> GetAllSetProperties()
        => new Dictionary<PropertyClass, object?>(properties);

    public int this[PropertyClass<int> property] => GetPropertyOrDefault(property);
    public double this[PropertyClass<double> property] => GetPropertyOrDefault(property);
    public string? this[PropertyClass<string> property] => GetPropertyOrDefault(property);
}
