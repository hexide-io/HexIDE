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

    private ControlFont(VBFont font) => this.font = font;

    internal static bool AppliesTo(Control control) => FormRoot.View(control) is not null || control is VBLabel or VBTextBox
        or VBCommandButton or VBCheckBox or VBOptionButton or VBComboBox or VBListBox or VBFrame or VBPictureBox
        or VBScrollBar or VBTimer or VBShape or VBLine or VBImage || control.GetValue(NotImplementedProperty);

    internal static ControlFont Get(Control control)
    {
        RefuseAbsent(control);
        if (fonts.TryGetValue(control, out var existing))
            return existing;
        var result = new ControlFont(FontMembers.Snapped(AttachedProperties.GetFont(control) ?? VBFont.Default));
        result.Attach(control, notify: false);
        return result;
    }

    /// <summary>The controls VB6 gives no Font at all, so reading or writing one raises 438 (#934).</summary>
    internal static bool IsAbsent(Control control) => control is VBScrollBar or VBTimer or VBShape or VBLine or VBImage;

    private static void RefuseAbsent(Control control)
    {
        if (control.GetValue(NotImplementedProperty))
            throw new NotImplementedException("Font on this placeholder control is not implemented (#934)");
        if (IsAbsent(control))
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

    private void Attach(Control control, bool notify = true)
    {
        if (fonts.TryGetValue(control, out var previous))
        {
            if (ReferenceEquals(previous, this))
            {
                if (notify)
                    Publish();
                return;
            }
            previous.owners.RemoveAll(owner => !owner.TryGetTarget(out var target) || ReferenceEquals(target, control));
            fonts.Remove(control);
        }
        fonts.Add(control, this);
        owners.Add(new WeakReference<Control>(control));
        Publish(notify);
    }

    internal static bool DescriptorChanged(Control control, VBFont descriptor)
    {
        if (fonts.TryGetValue(control, out var reference))
        {
            reference.font = descriptor;
            reference.Publish();
            return true;
        }
        return false;
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

    private void Publish(bool notify = true)
    {
        owners.RemoveAll(owner => !owner.TryGetTarget(out _));
        var targets = new List<Control>();
        foreach (var owner in owners)
            if (owner.TryGetTarget(out var control))
                targets.Add(control);
        foreach (var control in targets)
            control.SetValue(AttachedProperties.FontProperty, font);
        // Publish every owner's descriptor before calling code which may itself change the shared font.
        if (notify)
            foreach (var control in targets)
                FormAmbients.Changed(control, "Font");
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
        // #4448 measured Bold = True, including a StdFont shared with Set Font.
        if (name.Equals("Bold", StringComparison.OrdinalIgnoreCase)
            && value.Type == Vb6Value.ValueType.Boolean && value.Value is true)
        {
            font = new VBFont(font.FontFamilyName, font.Size, italic: font.Italic, charset: font.Charset,
                underline: font.Underline, strikethrough: font.Strikethrough, weight: VBFont.BoldWeight);
            Publish();
            return true;
        }
        // Only the issue's measured Size assignments; other object-member coercions and writes remain unmeasured.
        if (name.Equals("Size", StringComparison.OrdinalIgnoreCase)
            && value.Type == Vb6Value.ValueType.Integer && value.Value is int size
            && (size is 9 or 12 || size == 14 && font.FontFamilyName == "Arial"))
        {
            font = new VBFont(font.FontFamilyName, size == 14 ? 14.25 : size, italic: font.Italic, charset: font.Charset,
                underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
            Publish();
            return true;
        }
        throw new NotImplementedException($"Assigning this value to Font.{name} is not implemented (#934)");
    }

    public void Call(string method, List<Vb6Value> args) =>
        throw new NotImplementedException($"Font.{method} is not implemented (#934)");
}
