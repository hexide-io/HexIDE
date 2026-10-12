using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Utils;

namespace HexIDE.Runtime.AvaloniaInterop;

public static class AvaloniaInteroperability
{
    private static List<IAvaloniaBinding> bindings = new();

    private static Dictionary<PropertyClass, List<IAvaloniaBinding>> bindingsByProperty = new();

    // VB6 coerces any numeric to a numeric property (e.g. `Command1.Left = 100` assigns an Integer to the Double
    // Left). The CLR box types must be converted before a `is TProperty` check, which is exact.
    private static readonly HashSet<Type> NumericTypes = new()
    {
        typeof(byte), typeof(sbyte), typeof(short), typeof(ushort), typeof(int), typeof(uint),
        typeof(long), typeof(ulong), typeof(float), typeof(double), typeof(decimal),
    };
    private static bool IsNumeric(Type t) => NumericTypes.Contains(t);

    private interface IAvaloniaBinding
    {
        public PropertyClass UntypedProperty { get; }

        public object? GetUntyped(Control control);

        public void SetUntyped(Control control, object? val);

        public bool IsValidControl(Control control);
    }

    private class AvaloniaBinding<TControl, TProperty> : IAvaloniaBinding
    {
        public PropertyClass<TProperty> Property { get; }
        public Func<TControl, TProperty> Getter { get; }
        public Action<TControl, TProperty> Setter { get; }

        public AvaloniaBinding(PropertyClass<TProperty> property,
            Func<TControl, TProperty> getter,
            Action<TControl, TProperty> setter)
        {
            Property = property;
            Getter = getter;
            Setter = setter;
        }

        public TProperty Get(TControl control) => Getter(control);

        public void Set(TControl control, TProperty val) => Setter(control, val);

        public PropertyClass UntypedProperty => Property;

        public object? GetUntyped(Control control)
        {
            if (control is TControl t)
                return Get(t);
            throw new Exception("Invalid type for getting " + Property.Name + ", got " + control.GetType() +
                                " expected " + typeof(TControl));
        }

        public void SetUntyped(Control control, object? val)
        {
            if (control is not TControl t)
                throw new Exception("Invalid type for setting " + Property.Name + ", got " + control.GetType() +
                                    " expected " + typeof(TControl));
            if (val is int && typeof(TProperty).IsEnum)
                val = Enum.ToObject(typeof(TProperty), val);
            // Re-box across numeric CLR types before the exact type check: `Command1.Left = 100` hands a boxed Int32
            // to a Double property. Without this the assignment throws, the caller swallows it, and the control never
            // moves/resizes — the ubiquitous integer-literal case. (Overflow on a narrowing convert is left to the caller.)
            else if (val is not null && IsNumeric(val.GetType()) && IsNumeric(typeof(TProperty))
                     && val.GetType() != typeof(TProperty))
                val = Convert.ChangeType(val, typeof(TProperty));
            if (val is not TProperty v)
                throw new Exception("Invalid value type for setting " + Property.Name + ", got " + val?.GetType() +
                                    " expected " + typeof(TProperty));
            Set(t, v);
        }

        public bool IsValidControl(Control control)
        {
            return control is TControl;
        }
    }

    static AvaloniaInteroperability()
    {
        Register<Control, double>(VBProperties.LeftProperty, w => Canvas.GetLeft(w).OrZero(), Canvas.SetLeft);
        Register<Control, double>(VBProperties.TopProperty, w => Canvas.GetTop(w).OrZero(), Canvas.SetTop);
        Register<Control, double>(VBProperties.WidthProperty, Layoutable.WidthProperty);
        Register<Control, VBCursorType, Cursor?>(VBProperties.MousePointerProperty, InputElement.CursorProperty, x => x.ToCursor(), x => VBCursorType.Default);
        Register<Control, bool, FlowDirection>(VBProperties.RightToLeftProperty, Visual.FlowDirectionProperty, x => x ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, x => x == FlowDirection.RightToLeft);
        Register<Control, double>(VBProperties.HeightProperty, Layoutable.HeightProperty);
        // Not Visual.IsVisibleProperty: hiding a CONTAINER that way unrealises everything inside it, and an
        // unrealised control dispatches no events at all. See VBVisibility.
        Register<Control, bool>(VBProperties.VisibleProperty, VBVisibility.Get, VBVisibility.Set);
        Register<Control, bool>(VBProperties.EnabledProperty, InputElement.IsEnabledProperty);
        Register<Control, object?>(VBProperties.TagProperty, Control.TagProperty);
        Register<TemplatedControl, VBColor, VBColor?>(VBProperties.BackColorProperty, AttachedProperties.BackColorProperty, x => x, x=> x ?? default);
        Register<TemplatedControl, VBColor, VBColor?>(VBProperties.ForeColorProperty, AttachedProperties.ForeColorProperty, x => x, x=> x ?? default);
        Register<TemplatedControl, VBFont, VBFont?>(VBProperties.FontProperty, AttachedProperties.FontProperty, x => x, x=> x ?? default);
        Register<Window, string>(VBProperties.CaptionProperty, (window) => window.Title ?? "", (window, value) => window.Title = value);

        Register<VBCheckBox, VBAppearance>(VBProperties.AppearanceProperty, VBCheckBox.AppearanceProperty);
        Register<VBCheckBox, VBCheckValue>(VBProperties.CheckValueProperty, VBCheckBox.ValueProperty);

        // The registration whose absence was #95: without a line here `Option1.Value` resolves to nothing and
        // raises error 461, which is the whole interface of an option button unreachable from VB6 code.
        Register<VBOptionButton, VBAppearance>(VBProperties.AppearanceProperty, VBOptionButton.AppearanceProperty);
        Register<VBOptionButton, bool>(VBProperties.OptionValueProperty, VBOptionButton.ValueProperty);

        Register<VBLabel, string, string?>(VBProperties.CaptionProperty, VBLabel.TextProperty, x => x, x => x ?? "");
        Register<VBCheckBox, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");
        Register<VBOptionButton, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");
        Register<VBCommandButton, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");

        Register<VBTextBox, string, string?>(VBProperties.TextProperty, TextBox.TextProperty, x => x, x => x ?? "");

        Register<VBScrollBar, int, double>(VBProperties.ValueProperty, RangeBase.ValueProperty, x => x, x => (int)x);

        Register<VBTimer, int>(VBProperties.IntervalProperty, VBTimer.IntervalProperty);

        Register<ComboBox, int>(VBProperties.ListIndexProperty, SelectingItemsControl.SelectedIndexProperty);
        Register<ListBox, int>(VBProperties.ListIndexProperty, SelectingItemsControl.SelectedIndexProperty);
    }

    private static VBColor ToVbColor(IBrush? brush)
    {
        if (brush is not SolidColorBrush solidColorBrush)
            return VBColor.Black;

        return VBColorAvaloniaExtensions.FromAvaloniaColor(solidColorBrush.Color);
    }

    private static void Register<TControl, TProperty>(PropertyClass<TProperty> property,
        Func<TControl, TProperty> getter,
        Action<TControl, TProperty> setter)
    {
        var binding = new AvaloniaBinding<TControl, TProperty>(property, getter, setter);
        bindings.Add(binding);
        if (!bindingsByProperty.TryGetValue(property, out var prop))
            prop = bindingsByProperty[property] = new();
        prop.Add(binding);
    }

    private static void Register<TControl, TProperty>(PropertyClass<TProperty> vbProperty, AvaloniaProperty<TProperty> avaloniaProperty) where TControl : Control
    {
        Register<TControl, TProperty>(vbProperty, w =>
        {
            if (w.GetValue(avaloniaProperty) is TProperty prop)
                return prop;
            return default!;
        }, (w, v) => w.SetValue(avaloniaProperty, v));
    }

    private static void Register<TControl, TProperty, TAvaProperty>(PropertyClass<TProperty> vbProperty,
        AvaloniaProperty<TAvaProperty> avaloniaProperty,
        Func<TProperty, TAvaProperty> propertyToAva,
        Func<TAvaProperty, TProperty> avaToProperty) where TControl : Control
    {
        Register<TControl, TProperty>(vbProperty, w =>
        {
            if (w.GetValue(avaloniaProperty) is TAvaProperty prop)
                return avaToProperty(prop);
            return default!;
        }, (w, v) => w.SetValue(avaloniaProperty, propertyToAva(v)));
    }

    public static bool TrySet(Control control, PropertyClass property, Vb6Value value)
    {
        if (bindingsByProperty.TryGetValue(property, out var props))
        {
            foreach (var prop in props)
            {
                if (!prop.IsValidControl(control))
                    continue;

                prop.SetUntyped(control, ConvertForAssignment(property, value));
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// What an assignment to <paramref name="property"/> stores: the right-hand side converted to the property's
    /// VB6 type, by the rule that converts a value stored into a declared variable of that type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on vb6.exe (docs/vb6-fidelity-oracle.md, "Assigning to a control property"). A String property
    /// takes CStr of anything (<c>Label1.Caption = 5</c> is "5", <c>= True</c> is "True", Empty is ""). A numeric
    /// property parses a String and raises 13 when it cannot, and rounds half to even. Null raises 94 everywhere.
    /// Each of these raises before the property changes. That is exactly <see cref="VbNumeric.CoerceOnStore"/>,
    /// so the boundary uses it rather than growing a second opinion. Before, a number into a String property threw
    /// a plain CLR exception, which ended the run as an internal error (#754) and was silent before that (#768).
    /// </para>
    /// <para>
    /// What a declared variable does not do is the range check. An enum or colour property, and
    /// <c>Timer.Interval</c>, raises 380 for a value VB6 refuses, which is <c>Check1.Value = True</c> as much as
    /// <c>Check1.Value = 3</c>: True is -1. The value is first converted as an Integer or a Long, so a value too
    /// big for that is Overflow (6), and garbage is Type mismatch (13), before the range is looked at.
    /// </para>
    /// </remarks>
    internal static object? ConvertForAssignment(PropertyClass property, Vb6Value value)
    {
        var type = property.PropertyType;

        // Non-zero is True (0.4 included: it is "non-zero", not "rounds to 1"), "False" and "1" parse, Empty is
        // False, "banana" is 13 and Null is 94. CBool is the interpreter's own, already pinned, so the boundary
        // agrees with `CBool(x)` written out longhand.
        if (type == typeof(bool))
            return value.Value is bool ? value.Value : VB6BuiltIns.CBool(value).Value;

        // A control, an object or an array on the right is not a value to convert. For a control VB6 reads its
        // default property (`Label1.Caption = Text1` takes Text1.Text), which HexIDE does not implement, and
        // converting the reference itself would store a .NET type name as the caption. So it fails clearly instead
        // of answering wrong. What VB6 does with each of these was not measured for this change.
        if (!IsScalar(value))
            throw new NotImplementedException(
                $"Assigning {value.Type} to the {property.Name} property is not implemented");

        // Tag is a String in VB6, though HexIDE declares it as an object: `Label1.Tag = 5` reads back "5".
        if (type == typeof(string) || ReferenceEquals(property, VBProperties.TagProperty))
            return VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value;

        if (type == typeof(VBColor))
            return value.Value is VBColor ? value.Value : VBColor.FromOle(OleColor(value));

        if (type.IsEnum)
            return value.Value?.GetType() == type ? value.Value : Enum.ToObject(type, EnumValue(type, value));

        if (type == typeof(double))
        {
            // Measured: VB6 holds a position or size as a whole number of twips in a Long, though it reads back as
            // a Single. So `Left = 1.5` reads back 2, and `Left = 1E+20`, which a Single could hold, is Overflow (6).
            // The check here is that Long. The value itself is kept as given, because HexIDE does not hold
            // positions in twips; the rounding is recorded as a divergence in docs/vb6-fidelity-oracle.md.
            Coerce(value, Vb6Value.ValueType.Long);
            return System.Convert.ToDouble(Coerce(value, Vb6Value.ValueType.Double));
        }

        if (type == typeof(int))
        {
            var number = System.Convert.ToInt32(Coerce(value, Vb6Value.ValueType.Long));
            if (ReferenceEquals(property, VBProperties.IntervalProperty) && number is < 0 or > 65535)
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            return number;
        }

        return value.Value;
    }

    private static bool IsScalar(Vb6Value value) =>
        value.Type == Vb6Value.ValueType.EmptyVariant || value.IsNull || VbNumeric.IsDeclarableScalar(value.Type);

    private static object Coerce(Vb6Value value, Vb6Value.ValueType target) =>
        VbNumeric.CoerceOnStore(value, target, null).Value!;

    /// <summary>
    /// The OLE_COLOR a value names, or 380 when VB6 would refuse it. Measured: the high byte selects the kind, and
    /// each kind has its own range. 0x00 is an RGB value and 0x02 a palette-relative one, any low 24 bits. 0x01 is
    /// a palette index, 0 to &amp;HFFFF. 0x80 is a system colour, 0 to &amp;H18. Every other high byte is refused,
    /// and so is a negative number that is not a system colour (`BackColor = True` is -1).
    /// </summary>
    private static long OleColor(Vb6Value value)
    {
        var ole = System.Convert.ToInt64(Coerce(value, Vb6Value.ValueType.Long));
        var bits = (uint)ole;
        var low = bits & 0xFFFFFF;
        var valid = (bits >> 24) switch
        {
            0x00 or 0x02 => true,
            0x01 => low <= 0xFFFF,
            0x80 => low <= 0x18,
            _ => false,
        };
        return valid ? ole : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    /// <summary>
    /// The enum value a value names, converted as an Integer, or 380 when it is not one of the property's values.
    /// </summary>
    private static int EnumValue(Type type, Vb6Value value)
    {
        var number = System.Convert.ToInt32(Coerce(value, Vb6Value.ValueType.Integer));
        // MousePointer accepts every value from 0 to 15, and 99 (measured). VBCursorType leaves out 4, 5, 13 and 14
        // (#120), so its own members cannot say which are valid.
        var valid = type == typeof(VBCursorType)
            ? number is >= 0 and <= 15 or 99
            : Enum.IsDefined(type, number);
        return valid ? number : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    public static bool TryGet(Control c, PropertyClass property, out Vb6Value value)
    {
        if (bindingsByProperty.TryGetValue(property, out var props))
        {
            foreach (var prop in props)
            {
                if (!prop.IsValidControl(c))
                    continue;

                value = FromObject(prop.GetUntyped(c));
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>Enumerate the VB6 properties readable on a live control (name → current value), NAME-SORTED — the
    /// Locals property surface (P8/D7). A property is included only when a registered binding applies to this
    /// control's type (the same rule <see cref="TryGet"/> uses); duplicates by name are collapsed.</summary>
    public static IReadOnlyList<(string Name, Vb6Value Value)> ReadProperties(Control c)
    {
        var byName = new SortedDictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in bindingsByProperty.Keys)
            if (!byName.ContainsKey(property.Name) && TryGet(c, property, out var value))
                byName[property.Name] = value;
        var list = new List<(string, Vb6Value)>(byName.Count);
        foreach (var kv in byName)
            list.Add((kv.Key, kv.Value));
        return list;
    }

    private static Vb6Value FromObject(object? untyped)
    {
        if (untyped is null)
            return Vb6Value.Null;
        if (untyped is Vb6Value already)          // a property already exposing a VB6 value — pass it straight through
            return already;
        if (untyped is int i)
            return new Vb6Value(i);
        if (untyped is long l)
            return new Vb6Value(l);
        if (untyped is short sh)
            return new Vb6Value((int)sh);         // fits Integer (magnitude rule keeps it Integer, not Long)
        if (untyped is byte by)
            return new Vb6Value(by);
        if (untyped is string s)
            return new Vb6Value(s);
        if (untyped is float f)
            return new Vb6Value(f);
        if (untyped is double d)
            return new Vb6Value(d);
        if (untyped is decimal m)
            return new Vb6Value((double)m);       // no Currency ctor here — nearest numeric mapping
        if (untyped is bool b)
            return new Vb6Value(b);
        if (untyped is DateTime dt)
            return new Vb6Value(dt);
        if (untyped is VBColor col)
            return new Vb6Value(col);
        if (untyped.GetType().IsEnum)
            return new Vb6Value((int)untyped);
        // A control property of a type we don't map yet (a novel Avalonia struct) must not crash a running program
        // with an uncatchable NotImplementedException — render it as a String instead. Approximation-only (a
        // long/Currency-valued property previously threw; see docs/interpreter-gaps.md).
        return new Vb6Value(untyped.ToString());
    }
}