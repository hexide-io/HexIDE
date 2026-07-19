using System;
using System.Collections.Generic;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Components;

public enum PropertyCategory
{
    Misc,
    Appearance,
    Behavior,
    Position,
    Internal,
    List
}

public abstract class PropertyClass
{
    private static readonly Dictionary<Type, Func<string, (bool success, object? value)>> s_customParsers = new();

    /// <summary>
    /// Registers a custom string parser for a property type. Framework-specific types
    /// (e.g. Avalonia Color) register their parsers at startup.
    /// </summary>
    public static void RegisterParser<T>(Func<string, (bool success, object? value)> parser)
    {
        s_customParsers[typeof(T)] = parser;
    }

    public PropertyClass(string name, string description, PropertyCategory category)
    {
        Name = name;
        Category = category;
        Description = description ?? "";
        if (!VBProperties.PropertiesByName.TryGetValue(name, out var list))
            list = VBProperties.PropertiesByName[name] = new();
        list.Add(this);
    }

    public string Name { get; }
    public string Description { get; }
    public PropertyCategory Category { get; }
    public abstract Type PropertyType { get; }

    public abstract object? BoxedDefaultValue<TComponent>() where TComponent : IComponentClass;
    public abstract object? BoxedDefaultValue(IComponentClass componentClass);

    public bool TryParseString(string? value, out object? o)
    {
        if (value == null)
        {
            o = null;
            return true;
        }
        else if (PropertyType == typeof(string))
        {
            o = value;
            return true;
        }
        else if (PropertyType == typeof(double))
        {
            if (double.TryParse(value, out var typedValue))
            {
                o = typedValue;
                return true;
            }

            o = default;
            return false;
        }
        else if (PropertyType == typeof(int))
        {
            if (int.TryParse(value, out var typedValue))
            {
                o = typedValue;
                return true;
            }

            o = default;
            return false;
        }
        else if (PropertyType == typeof(bool))
        {
            if (bool.TryParse(value, out var typedValue))
            {
                o = typedValue;
                return true;
            }

            o = default;
            return false;
        }
        else if (PropertyType == typeof(VBColor))
        {
            if (VBColor.TryParse(value, out var typedValue))
            {
                o = typedValue;
                return true;
            }

            o = default;
            return false;
        }
        else if (s_customParsers.TryGetValue(PropertyType, out var parser))
        {
            var (success, result) = parser(value);
            o = result;
            return success;
        }
        else if (PropertyType.IsEnum)
        {
            if (int.TryParse(value, out var typedValue) && Enum.IsDefined(PropertyType, typedValue))
            {
                o = Enum.ToObject(PropertyType, typedValue);
                return true;
            }

            o = default;
            return false;
        }
        else if (PropertyType == typeof(object)) // i.e. Tag
        {
            o = value;
            return true;
        }
        throw new NotImplementedException($"Add case for propertyType '{PropertyType.FullName}'.");
    }
}

public class PropertyClass<T> : PropertyClass
{
    public override Type PropertyType => typeof(T);
    private T? defaultValue;
    private Dictionary<Type, T?>? defaultValueOverrides;

    public PropertyClass(string name,
        string description,
        PropertyCategory category,
        T? defaultValue = default) : base(name, description, category)
    {
        this.defaultValue = defaultValue;
    }

    public T? DefaultValue<TComponent>() where TComponent : IComponentClass => DefaultValue(typeof(TComponent));

    public T? DefaultValue(IComponentClass componentClass) => DefaultValue(componentClass.GetType());

    private T? DefaultValue(Type componentType)
    {
        if (defaultValueOverrides?.TryGetValue(componentType, out var @default) ?? false)
            return @default;
        return defaultValue;
    }

    public override object? BoxedDefaultValue<TComponent>() => DefaultValue(typeof(TComponent));

    public override object? BoxedDefaultValue(IComponentClass componentClass) => DefaultValue(componentClass);

    public void OverrideDefault<TComponent>(T? value)
    {
        defaultValueOverrides ??= new();
        defaultValueOverrides[typeof(TComponent)] = value;
    }
}
