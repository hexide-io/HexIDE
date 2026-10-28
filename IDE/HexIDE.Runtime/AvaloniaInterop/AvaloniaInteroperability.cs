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
        Register<Window, double>(VBProperties.LeftProperty, w => FormPosition.Read(w, false) / VBScaleModeExtensions.PixelToTwips,
            (w, value) => FormPosition.Write(w, false, value));
        Register<Window, double>(VBProperties.TopProperty, w => FormPosition.Read(w, true) / VBScaleModeExtensions.PixelToTwips,
            (w, value) => FormPosition.Write(w, true, value));
        Register<Control, double>(VBProperties.LeftProperty, w => Canvas.GetLeft(w).OrZero(), Canvas.SetLeft);
        Register<Control, double>(VBProperties.TopProperty, w => Canvas.GetTop(w).OrZero(), Canvas.SetTop);
        Register<Control, double>(VBProperties.WidthProperty, Layoutable.WidthProperty);
        Register<Control, VBCursorType>(VBProperties.MousePointerProperty, AttachedProperties.GetMousePointer, (control, value) =>
        {
            control.Cursor = value.ToCursor();
            AttachedProperties.SetMousePointer(control, value);
        });
        Register<Control, bool, FlowDirection>(VBProperties.RightToLeftProperty, Visual.FlowDirectionProperty, x => x ? FlowDirection.RightToLeft : FlowDirection.LeftToRight, x => x == FlowDirection.RightToLeft);
        Register<Control, double>(VBProperties.HeightProperty, Layoutable.HeightProperty);
        // Not Visual.IsVisibleProperty: hiding a CONTAINER that way unrealises everything inside it, and an
        // unrealised control dispatches no events at all. See VBVisibility.
        Register<Control, bool>(VBProperties.KeyPreviewProperty, AttachedProperties.GetKeyPreview, AttachedProperties.SetKeyPreview);
        Register<Control, bool>(VBProperties.VisibleProperty, VBVisibility.Get, VBVisibility.Set);
        // A disabled container masks the read, but the setter keeps the control's own value (#2245, measured).
        Register<Control, bool>(VBProperties.EnabledProperty, VBEnabled.Get, (c, value) => c.IsEnabled = value);
        Register<Control, object?>(VBProperties.TagProperty, Control.TagProperty);
        // ToolTipText, on the ten controls HexIDE runs that VB6 gives it, and on no other: a scroll bar, Shape, Line,
        // Timer, menu or form has none (measured, #1403). The tip shown is the value, and "" where none was given.
        RegisterToolTipText<VBCommandButton>();
        RegisterToolTipText<VBTextBox>();
        RegisterToolTipText<VBComboBox>();
        RegisterToolTipText<VBListBox>();
        RegisterToolTipText<VBCheckBox>();
        RegisterToolTipText<VBOptionButton>();
        RegisterToolTipText<VBFrame>();
        RegisterToolTipText<VBPictureBox>();
        RegisterToolTipText<VBLabel>();
        RegisterToolTipText<VBImage>();
        RegisterToolTipText<VBStatusBar>();
        // TabIndex and TabStop, on the controls VB6 gives them (measured, #1974). A TabIndex set renumbers the form.
        RegisterTabOrder<VBCommandButton>(tabStop: true);
        RegisterTabOrder<VBTextBox>(tabStop: true);
        RegisterTabOrder<VBComboBox>(tabStop: true);
        RegisterTabOrder<VBListBox>(tabStop: true);
        RegisterTabOrder<VBCheckBox>(tabStop: true);
        RegisterTabOrder<VBOptionButton>(tabStop: true);
        RegisterTabOrder<VBPictureBox>(tabStop: true);
        RegisterTabOrder<VBScrollBar>(tabStop: true);
        RegisterTabOrder<VBFrame>(tabStop: false);
        RegisterTabOrder<VBLabel>(tabStop: false);
        RegisterTabOrder<VBProgressBar>(tabStop: false);
        RegisterTabOrder<VBUpDown>(tabStop: true);
        RegisterTabOrder<VBStatusBar>(tabStop: false);
        RegisterTabOrder<VBSSTab>(tabStop: true);
        // A Label paints its own BackColor (VBLabel.Render), not the attached one every other control's template
        // draws, so it is bound first: a run-time assignment then paints, and a read gives the form's colour (#795).
        Register<VBLabel, VBColor>(VBProperties.BackColorProperty, VBLabel.BackColorProperty);
        Register<TemplatedControl, VBColor, VBColor?>(VBProperties.BackColorProperty, AttachedProperties.BackColorProperty, x => x, x=> x ?? default);
        Register<TemplatedControl, VBColor, VBColor?>(VBProperties.ForeColorProperty, AttachedProperties.ForeColorProperty, x => x, x=> x ?? default);
        Register<TemplatedControl, VBFont, VBFont?>(VBProperties.FontProperty, AttachedProperties.FontProperty, x => x, x=> x ?? default);
        Register<Window, string>(VBProperties.CaptionProperty, (window) => window.Title ?? "", (window, value) => window.Title = value);

        Register<VBCheckBox, VBAppearance>(VBProperties.AppearanceProperty, VBCheckBox.AppearanceProperty);
        // A Label's BackStyle can be set by a program, as VB6 measured (#795): opaque, then a BackColor paints. Bound,
        // because without it the assignment was a trappable 461 that On Error Resume Next swallowed, and the
        // transparent-Label rule then dropped the BackColor that followed, a wrong value with no error.
        Register<VBLabel, BackStyles>(VBProperties.BackStyleProperty, VBLabel.BackStyleProperty);
        Register<VBLabel, VBAppearance>(VBProperties.AppearanceProperty, VBLabel.AppearanceProperty);
        Register<VBCheckBox, VBCheckValue>(VBProperties.CheckValueProperty, VBCheckBox.ValueProperty);

        // The registration whose absence was #95: without a line here `Option1.Value` resolves to nothing and
        // raises error 461, which is the whole interface of an option button unreachable from VB6 code.
        Register<VBOptionButton, VBAppearance>(VBProperties.AppearanceProperty, VBOptionButton.AppearanceProperty);
        Register<VBOptionButton, bool>(VBProperties.OptionValueProperty, VBOptionButton.ValueProperty);

        Register<VBCommandButton, bool>(VBProperties.CommandValueProperty, _ => false, (button, value) =>
        {
            // Only the True assignment is measured (#2065); keep refusing False until it is measured.
            if (!value)
                throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "Value");
            button.ExecuteSub(ComponentBaseClass.ClickEvent);
        });

        Register<VBLabel, string, string?>(VBProperties.CaptionProperty, VBLabel.TextProperty, x => x, x => x ?? "");
        Register<VBCheckBox, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");
        Register<VBOptionButton, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");
        Register<VBCommandButton, string, object?>(VBProperties.CaptionProperty, ContentControl.ContentProperty, x => x, x => x as string ?? "");

        // Setting Text from code puts the caret at the start (measured, #1150); typing, which sets it too, does not come here.
        Register<VBTextBox, string>(VBProperties.TextProperty, box => box.Text ?? "", (box, text) =>
        {
            box.Text = text;
            TextBoxMembers.TextSet(box);
        });

        Register<VBScrollBar, int>(VBProperties.ValueProperty, bar => bar.VbValue, (bar, value) => bar.VbValue = value);

        Register<VBTimer, int>(VBProperties.IntervalProperty, VBTimer.IntervalProperty);

        Register<ComboBox, int>(VBProperties.ListIndexProperty, SelectingItemsControl.SelectedIndexProperty);
        // A multi-select list's ListIndex is its focused item, not a selected one (#1413).
        Register<ListBox, int>(VBProperties.ListIndexProperty, l => l is VBListBox v ? v.ListIndex : l.SelectedIndex,
            (l, index) => l.SelectedIndex = index);
        // Read-only to a program: TrySet refuses it before it gets here.
        Register<VBListBox, VBMultiSelect>(VBProperties.MultiSelectProperty, l => l.MultiSelect,
            (_, _) => throw new VBCompileErrorException("Can't assign to read-only property"));
        Register<VBComboBox, VBComboStyle>(VBProperties.ComboStyleProperty, c => c.ComboStyle,
            (_, _) => throw new VBCompileErrorException("Can't assign to read-only property"));

        // A Line's members are read and written by LineMembers, which types and checks them as VB6 does (#163). They are
        // bound here as well so that the Locals window lists them, as it lists every bound property.
        Register<VBLine, double>(VBProperties.X1Property, l => l.Read(VBLine.X1), (l, v) => l.Write(VBLine.X1, v));
        Register<VBLine, double>(VBProperties.Y1Property, l => l.Read(VBLine.Y1), (l, v) => l.Write(VBLine.Y1, v));
        Register<VBLine, double>(VBProperties.X2Property, l => l.Read(VBLine.X2), (l, v) => l.Write(VBLine.X2, v));
        Register<VBLine, double>(VBProperties.Y2Property, l => l.Read(VBLine.Y2), (l, v) => l.Write(VBLine.Y2, v));
        Register<VBLine, double>(VBProperties.BorderWidthProperty, l => l.BorderWidth, (l, v) => l.BorderWidth = (int)v);
        Register<VBLine, BorderStyles>(VBProperties.ShapeBorderStyleProperty, VBLine.BorderStyleProperty);
        Register<VBLine, VBColor>(VBProperties.BorderColorProperty, VBLine.BorderColorProperty);
        Register<VBLine, int>(VBProperties.DrawModeProperty, VBLine.DrawModeProperty);
    }

    private static VBColor ToVbColor(IBrush? brush)
    {
        if (brush is not SolidColorBrush solidColorBrush)
            return VBColor.Black;

        return VBColorAvaloniaExtensions.FromAvaloniaColor(solidColorBrush.Color);
    }

    private static void RegisterTabOrder<TControl>(bool tabStop) where TControl : Control
    {
        Register<TControl, int>(VBProperties.TabIndexProperty, c => TabOrder.Get(c), (c, place) => TabOrder.Set(c, place));
        if (tabStop)
            Register<TControl, bool>(VBProperties.TabStopProperty, c => TabOrder.GetTabStop(c), (c, stop) => TabOrder.SetTabStop(c, stop));
    }

    private static void RegisterToolTipText<TControl>() where TControl : Control =>
        Register<TControl, string?>(VBProperties.ToolTipTextProperty, c => ToolTip.GetTip(c) as string ?? "",
            (c, tip) => ToolTip.SetTip(c, string.IsNullOrEmpty(tip) ? null : tip));

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
        VBProps.RefuseIfRemoved(control);
        if (control is VBTimer && !TimerMembers.HasProperty(property.Name))
            return false;
        RefuseUnmeasuredColour(control, property);
        if (control is VBTimer && ControlPositions.IsPosition(property))
            throw IdentityMembers.ReadOnlyAtRunTime(property.Name);
        if (control is VBLine line && LineMembers.TrySet(line, property, value) is { } lineMember)
            return lineMember;
        if (MenuMembers.IsMenu(control) && MenuMembers.TrySet(control, property, value) is { } menuMember)
            return menuMember;

        // VB6 refuses both at compile time, whatever the value (measured, #1413 and #1428).
        if (ReferenceEquals(property, VBProperties.MultiSelectProperty) || ReferenceEquals(property, VBProperties.ComboStyleProperty))
            throw new VBCompileErrorException("Can't assign to read-only property");

        if (bindingsByProperty.TryGetValue(property, out var props))
        {
            foreach (var prop in props)
            {
                if (!prop.IsValidControl(control))
                    continue;

                var converted = ConvertForAssignment(property, value);
                if (ControlPositions.IsPosition(property) && converted is double assigned)
                    converted = ControlPositions.ToPixels(control, property, assigned);
                if (control is VBScrollBar bar && ReferenceEquals(property, VBProperties.ValueProperty) && converted is int position)
                    CheckScrollBarValue(bar, position);
                // ListIndex takes -1 (nothing selected) to ListCount - 1, and 380 outside it, from a String too, in a
                // ListBox and a ComboBox alike (measured). Avalonia would drop the value instead.
                if (control is ItemsControl items && ReferenceEquals(property, VBProperties.ListIndexProperty)
                    && converted is int index && (index < -1 || index >= items.ItemCount))
                    throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                // A ListBox raises Click when code moves its selection (measured, #1374).
                if (control is VBListBox list && ReferenceEquals(property, VBProperties.ListIndexProperty) && converted is int listIndex)
                {
                    list.SetListIndexFromCode(listIndex);
                    return true;
                }
                // And so does a ComboBox (measured, #1429).
                if (control is VBComboBox combo && ReferenceEquals(property, VBProperties.ListIndexProperty) && converted is int comboIndex)
                {
                    combo.SetListIndexFromCode(comboIndex);
                    return true;
                }
                // A transparent Label keeps the BackColor it has: VB6 drops one assigned at run time, without an
                // error, so it reads back the old colour and shows that colour if the label is later made opaque
                // (measured, #795). Only once the label is opaque does an assignment take. The value is converted
                // first, so one VB6 refuses is still refused.
                if (control is VBLabel { BackStyle: BackStyles.Transparent } && ReferenceEquals(property, VBProperties.BackColorProperty))
                    return true;
                // A VB size assignment raises Resize inside the setter; native window notifications settle together (#2389).
                using (control is VBFormRuntime runtime && (ReferenceEquals(property, VBProperties.WidthProperty)
                    || ReferenceEquals(property, VBProperties.HeightProperty)) ? runtime.ResizeOnce() : null)
                    prop.SetUntyped(control, converted);
                // VB6 resets BackColor with a Label's Appearance (measured, #796).
                if (control is VBLabel label && ReferenceEquals(property, VBProperties.AppearanceProperty))
                    label.BackColor = VBColor.FromSystemColor(label.Appearance == VBAppearance._3D
                        ? VbSystemColor.Btnface : VbSystemColor.Window);
                return true;
            }
        }

        return false;
    }

    /// <summary>A scroll bar's Value outside either ordering of Min..Max is 380 in VB6, from a String too.</summary>
    private static void CheckScrollBarValue(VBScrollBar bar, int position)
    {
        if (position < Math.Min(bar.VbMin, bar.VbMax) || position > Math.Max(bar.VbMin, bar.VbMax))
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    /// <summary>
    /// What an assignment to <paramref name="property"/> stores: the right-hand side converted to the property's
    /// VB6 type, by the rule that converts a value stored into a declared variable of that type.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured on vb6.exe (docs/vb6-fidelity-oracle/assigning-to-a-control-property.md, "Assigning to a control property"). A String property
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

        // A colour read from a control is a Long in VB6, so copying one (`Text1.BackColor = Label1.BackColor`),
        // restoring one saved in a Variant, or showing one (`Caption = BackColor`) takes it as that Long.
        if (value.Value is VBColor color)
            value = new Vb6Value((long)color.ToOle());

        // A control, an object or an array on the right is not a value to convert. For a control VB6 reads its
        // default property (`Label1.Caption = Text1` takes Text1.Text), which HexIDE does not implement, and
        // converting the reference itself would store a .NET type name as the caption. So it fails clearly instead
        // of answering wrong, for a Boolean property too: there it used to give a trappable 13, which On Error
        // Resume Next would step over. What VB6 does with each of these was not measured for this change.
        if (!IsScalar(value))
            throw new NotImplementedException(
                $"Assigning {value.Type} to the {property.Name} property is not implemented");

        // Tag keeps what it is given, for now. It is a String in VB6 (`Label1.Tag = 5` reads back "5", measured),
        // but storing it as one here broke two idioms that work today: `If Command1.Tag = 1` needs the String-to-
        // number comparison #211 has not landed, and `Tag + 1` needs String arithmetic. Tag becomes a String with
        // #211. An unset Tag reads back "" (TryGet). Null is 94, as for any String property. It comes after the
        // guard, so a control is refused here too rather than stored as a reference that reads as a type name.
        if (ReferenceEquals(property, VBProperties.TagProperty))
            return value.IsNull ? throw new VBRunTimeException(VBStandardError.InvalidUseOfNull)
                // Empty, or a Variant never assigned, is "" to a String, which Tag is in VB6. Passed through, its
                // missing value reached the binding and ended the run as Internal error 51.
                : value.Type == Vb6Value.ValueType.EmptyVariant ? ""
                : value.Value;

        // Non-zero is True (0.4 included: it is "non-zero", not "rounds to 1"), "False" and "1" parse, Empty is
        // False (measured: `Command1.Enabled = Empty` is False, no error), "banana" is 13 and Null is 94. CBool is
        // the interpreter's own, already pinned, so the boundary agrees with `CBool(x)` written out longhand.
        if (type == typeof(bool))
            return value.Value is bool ? value.Value : VB6BuiltIns.CBool(value).Value;

        // ToolTipText takes Null as 13, Type mismatch, on every control that has it, where Caption, Text and Tag take it as
        // 94 (measured, #1403). A ComboBox's Text is 13 as well (#926), and is refused in ListMembers before this.
        if (ReferenceEquals(property, VBProperties.ToolTipTextProperty) && value.IsNull)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);

        // As CStr writes it: a Date in the program's locale, day first under en-GB (#777).
        if (type == typeof(string))
            return VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.String, null).Value;

        if (type == typeof(VBColor))
            return VBColor.FromOle(OleColor(value));

        if (type.IsEnum)
            return value.Value?.GetType() == type ? value.Value : Enum.ToObject(type, EnumValue(type, value));

        if (type == typeof(double))
        {
            // A position or size is a whole number of twips in a Long, though it reads back as a Single (measured).
            // ControlPositions rounds it, in twips, and checks the Long range there, where the unit is known: `Left =
            // 1E+20`, which a Single could hold, is Overflow (6). Any other double property keeps the Long check here,
            // so one registered later, such as BorderWidth, does not lose it.
            if (!ControlPositions.IsPosition(property))
                Coerce(value, Vb6Value.ValueType.Long);
            return System.Convert.ToDouble(Coerce(value, Vb6Value.ValueType.Double));
        }

        if (type == typeof(int))
        {
            // ListIndex, a scroll bar's Value and TabIndex are Integers in VB6 (each reads back with TypeName Integer,
            // measured), so a value past 32767 is Overflow, 6, before any range check: `ListIndex = 40000` is 6, and so is
            // `TabIndex = 40000` (#1974). The rest of the int properties are Longs, `Interval` among them.
            var integer = ReferenceEquals(property, VBProperties.ListIndexProperty) || ReferenceEquals(property, VBProperties.ValueProperty)
                || ReferenceEquals(property, VBProperties.TabIndexProperty);
            var number = System.Convert.ToInt32(Coerce(value, integer ? Vb6Value.ValueType.Integer : Vb6Value.ValueType.Long));
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
    /// each kind has its own range. 0x00 is an RGB value, any low 24 bits. 0x80 is a system colour, 0 to &amp;H18:
    /// &amp;H19 to &amp;H20 were each probed and refused. Every other high byte is refused here; five were probed,
    /// &amp;H03, &amp;H04, &amp;H7F, &amp;HC0 and &amp;HFF, and VB6 refused each, so the rest are inferred. A negative
    /// number that is not a system colour is refused too (`BackColor = True` is -1). The palette kinds, 0x01 (an index, 0 to
    /// &amp;HFFFF) and 0x02 (palette-relative RGB, any), VB6 accepts and keeps as given; HexIDE has no colour that
    /// can hold one, so they are refused as not implemented rather than stored as the RGB of their low bytes (#786).
    /// </summary>
    private static long OleColor(Vb6Value value)
    {
        var ole = System.Convert.ToInt64(Coerce(value, Vb6Value.ValueType.Long));
        var bits = (uint)ole;
        var low = bits & 0xFFFFFF;
        var valid = (bits >> 24) switch
        {
            0x00 => true,
            0x01 => low <= 0xFFFF,
            0x02 => true,
            0x80 => low <= 0x18,
            _ => false,
        };
        if (!valid)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if ((bits >> 24) is 0x01 or 0x02)
            throw new NotImplementedException(
                $"A palette colour (&H{bits:X8}) is not implemented (#786)");
        return ole;
    }

    /// <summary>
    /// The enum value a value names, converted as an Integer, or 380 when it is not one of the property's values.
    /// </summary>
    internal static int EnumValue(Type type, Vb6Value value)
    {
        var number = System.Convert.ToInt32(Coerce(value, Vb6Value.ValueType.Integer));
        // MousePointer's members are exactly what VB6 accepts, 0 to 15 and 99, measured (#768, #120).
        return Enum.IsDefined(type, number) ? number : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    private static void RefuseUnmeasuredColour(Control control, PropertyClass property)
    {
        if (control is VBScrollBar && (ReferenceEquals(property, VBProperties.BackColorProperty)
                                      || ReferenceEquals(property, VBProperties.ForeColorProperty)))
            throw new NotImplementedException($"A scroll bar's {property.Name} is not measured (#801)");
    }

    public static bool TryGet(Control c, PropertyClass property, out Vb6Value value)
    {
        VBProps.RefuseIfRemoved(c);
        if (c is VBTimer && !TimerMembers.HasProperty(property.Name))
        {
            value = default;
            return false;
        }
        RefuseUnmeasuredColour(c, property);
        if (c is VBTimer timer && ReferenceEquals(property, VBProperties.IntervalProperty))
        {
            value = new Vb6Value((long)timer.Interval);
            return true;
        }
        // Only the read is measured for Shape (#2070); keep its unmeasured assignment behaviour unchanged.
        if (c is VBShape shape && ReferenceEquals(property, VBProperties.BackColorProperty))
        {
            value = new Vb6Value((long)shape.BackColor.ToOle());
            return true;
        }
        if (c is VBLine line && LineMembers.TryGet(line, property, out value) is { } lineMember)
            return lineMember;
        if (MenuMembers.IsMenu(c) && MenuMembers.TryGet(c, property, out value) is { } menuMember)
            return menuMember;
        // A Frame keeps its caption in Header; only the read is measured (#2063).
        if (c is VBFrame frame && ReferenceEquals(property, VBProperties.CaptionProperty))
        {
            value = new Vb6Value(frame.Header as string ?? "");
            return true;
        }
        // A form a modal form holds reads Enabled False (measured, #822). The hold is apart from its own Enabled, which is
        // what it reads once the modal form has gone.
        if (c is IFormView { HeldBy: not null } && ReferenceEquals(property, VBProperties.EnabledProperty))
        {
            value = new Vb6Value(false);
            return true;
        }
        if (bindingsByProperty.TryGetValue(property, out var props))
        {
            foreach (var prop in props)
            {
                if (!prop.IsValidControl(c))
                    continue;

                var raw = prop.GetUntyped(c);
                // A position or size is twips, or the container's ScaleMode, as a Single (#933), where HexIDE holds pixels.
                if (ControlPositions.IsPosition(property) && raw is double pixels)
                {
                    value = ControlPositions.Read(c, property, pixels);
                    return true;
                }
                // A colour is a Long to a VB6 program (measured: `Text1.BackColor` reads back 255, TypeName Long), so
                // it prints as a number and compares with one: `If Label1.BackColor = vbRed` is True (#789). An unset
                // Tag is "" in VB6, so `X.Tag = Y.Tag` copies an empty string rather than raising 94.
                value = raw switch
                {
                    VBColor colour => new Vb6Value((long)colour.ToOle()),
                    null when ReferenceEquals(property, VBProperties.TagProperty) => new Vb6Value(""),
                    _ => FromObject(raw),
                };
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>Enumerate the VB6 properties readable on a live control (name → current value), NAME-SORTED — the
    /// Locals property surface (P8/D7). A property is included only when a registered binding applies to this
    /// control's runtime members; duplicates by name are collapsed.</summary>
    /// <remarks>
    /// A property whose read raises is listed with the raised error's own description and no value, rather than ending
    /// the read (#2289). A Timer's designer positions and absent members aren't listed by VB6's Locals (#801).
    /// </remarks>
    public static IReadOnlyList<(string Name, Vb6Value Value, string? Error)> ReadProperties(Control c)
    {
        var byName = new SortedDictionary<string, (Vb6Value Value, string? Error)>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in bindingsByProperty.Keys)
        {
            if (byName.ContainsKey(property.Name))
                continue;
            if (c is VBTimer && (!TimerMembers.HasProperty(property.Name)
                                 || ReferenceEquals(property, VBProperties.LeftProperty)
                                 || ReferenceEquals(property, VBProperties.TopProperty)))
                continue;
            if (c is VBScrollBar && (ReferenceEquals(property, VBProperties.BackColorProperty)
                                    || ReferenceEquals(property, VBProperties.ForeColorProperty)))
                continue;
            try
            {
                if (TryGet(c, property, out var value))
                    byName[property.Name] = (value, null);
            }
            catch (VBRunTimeException ex)
            {
                byName[property.Name] = (default, ex.Error.Description);
            }
            // Not a raise VB6 would make, but a fault in one getter shouldn't hide the rest, as a watch shows its message.
            catch (Exception ex)
            {
                byName[property.Name] = (default, ex.Message.ReplaceLineEndings(" ").Trim());
            }
        }
        var list = new List<(string, Vb6Value, string?)>(byName.Count);
        foreach (var kv in byName)
            list.Add((kv.Key, kv.Value.Value, kv.Value.Error));
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
            return new Vb6Value(VbDate.FromDateTime(dt));
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
