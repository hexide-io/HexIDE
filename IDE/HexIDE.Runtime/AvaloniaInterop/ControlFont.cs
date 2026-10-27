using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// A reference shared by Set, separate from the value descriptor used by the designer and .frm writer (#934).
internal sealed class ControlFont : ICSharpProxy, ICSharpPropertyBag
{
    internal static readonly AttachedProperty<bool> NotImplementedProperty =
        AvaloniaProperty.RegisterAttached<ControlFont, Control, bool>("FontNotImplemented");
    private static readonly ConditionalWeakTable<Control, ControlFont> fonts = new();
    private readonly List<WeakReference<Control>> owners = new();
    private VBFont font;
    private bool publishing;

    private ControlFont(VBFont font) => this.font = font;

    internal static bool AppliesTo(Control control) => control is IFormView or VBLabel or VBTextBox
        or VBCommandButton or VBCheckBox or VBOptionButton or VBComboBox or VBListBox or VBFrame or VBPictureBox
        or VBScrollBar or VBTimer or VBShape or VBLine or VBImage || control.GetValue(NotImplementedProperty);

    internal static ControlFont Get(Control control)
    {
        RefuseAbsent(control);
        if (fonts.TryGetValue(control, out var existing))
            return existing;
        var result = new ControlFont(FontMembers.Snapped(AttachedProperties.GetFont(control) ?? VBFont.Default));
        result.Attach(control);
        return result;
    }

    private static void RefuseAbsent(Control control)
    {
        if (control.GetValue(NotImplementedProperty))
            throw new NotImplementedException("Font on this placeholder control is not implemented (#934)");
        if (control is VBScrollBar or VBTimer or VBShape or VBLine or VBImage)
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        if (AttachedProperties.GetFont(control) is null)
            throw new NotImplementedException("Font on this control is not implemented (#934)");
    }

    internal static void Set(Control control, Vb6Value value)
    {
        VBProps.RefuseIfRemoved(control);
        RefuseAbsent(control);
        if (value.Type == Vb6Value.ValueType.Object && value.Value is null)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (value.Value is not ControlFont shared)
            throw new NotImplementedException("Set Font from this object is not implemented (#934)");
        shared.Attach(control);
    }

    private void Attach(Control control)
    {
        if (fonts.TryGetValue(control, out var previous))
        {
            if (ReferenceEquals(previous, this))
                return;
            previous.owners.RemoveAll(owner => !owner.TryGetTarget(out var target) || ReferenceEquals(target, control));
            fonts.Remove(control);
        }
        fonts.Add(control, this);
        owners.Add(new WeakReference<Control>(control));
        Publish();
    }

    internal static void DescriptorChanged(Control control, VBFont descriptor)
    {
        if (fonts.TryGetValue(control, out var reference) && !reference.publishing)
        {
            reference.font = descriptor;
            reference.Publish();
        }
    }

    internal static bool TryDescriptor(Control control, out VBFont descriptor)
    {
        if (fonts.TryGetValue(control, out var reference))
        {
            descriptor = reference.font;
            return true;
        }
        descriptor = default;
        return false;
    }

    private void Publish()
    {
        publishing = true;
        try
        {
            owners.RemoveAll(owner => !owner.TryGetTarget(out _));
            foreach (var owner in owners)
                if (owner.TryGetTarget(out var control))
                    AttachedProperties.SetFont(control, font);
        }
        finally { publishing = false; }
    }

    internal Vb6Value DefaultValue => new(font.FontFamilyName);

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "name" => DefaultValue,
            "size" => Vb6Value.NewCurrency((decimal)font.Size),
            "bold" => new Vb6Value(font.Weight > 550),
            "italic" => new Vb6Value(font.Italic),
            "weight" => new Vb6Value(font.Weight > 550 ? 700 : 400),
            "charset" => new Vb6Value(font.Charset),
            _ => throw new NotImplementedException($"Font.{name} is not implemented (#934)"),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        // The issue measured these two Size assignments. Object-member coercions and other writes are not measured.
        if (name.Equals("Size", StringComparison.OrdinalIgnoreCase)
            && value.Type == Vb6Value.ValueType.Integer && value.Value is int size && size is 9 or 12)
        {
            font = new VBFont(font.FontFamilyName, size, italic: font.Italic, charset: font.Charset,
                underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
            Publish();
            return true;
        }
        throw new NotImplementedException($"Assigning this value to Font.{name} is not implemented (#934)");
    }

    public void Call(string method, List<Vb6Value> args) =>
        throw new NotImplementedException($"Font.{method} is not implemented (#934)");
}
