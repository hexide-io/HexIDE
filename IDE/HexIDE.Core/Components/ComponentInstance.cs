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
