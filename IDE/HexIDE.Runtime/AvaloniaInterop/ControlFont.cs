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
    private bool standalone;

    private ControlFont(VBFont font) => this.font = font;

    // Initial values measured on VB6 SP6, native and p-code (#4781).
    internal static ControlFont CreateStandalone() => new(FontMembers.Snapped(
        new VBFont("MS Sans Serif", 8.25, charset: 0, weight: VBFont.NormalWeight))) { standalone = true };

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
            "underline" when standalone => new Vb6Value(font.Underline),
            "strikethrough" when standalone => new Vb6Value(font.Strikethrough),
            _ => throw new NotImplementedException($"Font.{name} is not implemented (#934)"),
        };
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (standalone && TrySetStandalone(name, value))
        {
            Publish();
            return true;
        }
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
        throw new NotImplementedException(standalone
            ? $"Assigning this value to StdFont.{name} is not implemented (#4926)"
            : $"Assigning this value to Font.{name} is not implemented (#934)");
    }

    private bool TrySetStandalone(string name, Vb6Value value)
    {
        // Exact standalone assignment rows: VB6 SP6 native/p-code, #4807. Do not infer other substitutions.
        var member = name.ToLowerInvariant();
        if (value.IsNull && member is "name" or "size" or "bold")
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        switch (member)
        {
            case "name" when value.Type == Vb6Value.ValueType.String && value.Value is string family:
                if (family is "No Such Font" or "")
                    return true;
                if (family is "Wingdings" or "Symbol" or "Verdana" or "Times New Roman")
                {
                    font = new VBFont(family, font.Size, italic: font.Italic,
                        charset: family is "Wingdings" or "Symbol" ? 2 : 0,
                        underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
                    return true;
                }
                if (family is "Arial" or "Courier New" or "Tahoma" or "Courier")
                {
                    font = FontMembers.Named(font, value);
                    return true;
                }
                break;
            case "name" when value.Type == Vb6Value.ValueType.Integer && value.Value is int number && number == 5:
                return true;
            case "size":
                if (value.Type == Vb6Value.ValueType.String && value.Value is string text && text is "x" or "10" or "11"
                    || value.Type == Vb6Value.ValueType.Integer && value.Value is int size && size is -1 or 0 or 1 or 9 or 10 or 12 or 14 or 20 or 2048 or 2160 or 100000
                    || value.Type == Vb6Value.ValueType.Long && value.Value is long large && large == 100000
                    || value.Type == Vb6Value.ValueType.Integer && value.Value is int seven && seven == 7 && font.FontFamilyName == "Arial"
                    || value.Type == Vb6Value.ValueType.Double && value.Value is double fraction && fraction is 7.5 or 9.75 or 10.5)
                {
                    var sized = FontMembers.Sized(font, value);
                    var asked = Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
                    if (font.FontFamilyName == "MS Sans Serif")
                    {
                        // Each measured on a fresh font: 10.5 reads Arial 10.5, where FontMembers keeps the bitmap's 9.75.
                        if (asked is 1 or 9 or 10.5 or 11 or 14 or 20)
                            sized = new VBFont("Arial", asked == 1 ? 1.5 : asked is 9 or 10.5 ? asked : asked + .25,
                                italic: font.Italic, charset: 0, underline: font.Underline,
                                strikethrough: font.Strikethrough, weight: font.Weight);
                        else if (asked == 7.5)
                            sized = new VBFont("Small Fonts", 7.5, italic: font.Italic, charset: 0,
                                underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
                    }
                    font = sized;
                    return true;
                }
                break;
            case "bold" when value.Type == Vb6Value.ValueType.Boolean
                || value.Type == Vb6Value.ValueType.Integer && value.Value is int bold && bold is -1 or 0 or 1
                || value.Type == Vb6Value.ValueType.String && value.Value is string flag && flag == "True":
                var enabled = VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Boolean, null).Value is true;
                font = FontMembers.With(font, weight: enabled ? VBFont.BoldWeight : VBFont.NormalWeight);
                // Bold sets Weight 700 or 400, and either makes MS Sans Serif read Arial: True, False, -1, 0 and 1 alike.
                if (font.FontFamilyName == "MS Sans Serif")
                    font = new VBFont("Arial", font.Size, italic: font.Italic, charset: 0,
                        underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
                return true;
            case "weight" when value.Type == Vb6Value.ValueType.Integer && value.Value is int weight
                && weight is -1 or 0 or 100 or 400 or 550 or 551 or 600 or 700 or 900 or 1000 or 32767:
                if (weight is -1 or 32767)
                    throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                font = FontMembers.With(font, weight: weight > 550 ? VBFont.BoldWeight : VBFont.NormalWeight);
                // Exactly 400 or 700 makes MS Sans Serif read Arial; 0, 100, 550, 551, 600, 900 and 1000 keep it (measured).
                if (weight is 400 or 700 && font.FontFamilyName == "MS Sans Serif")
                    font = new VBFont("Arial", font.Size, italic: font.Italic, charset: 0,
                        underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
                return true;
            case "italic" or "underline" or "strikethrough" when value.Type == Vb6Value.ValueType.Boolean && value.Value is bool set:
                font = FontMembers.With(font,
                    italic: member == "italic" ? set : null,
                    underline: member == "underline" ? set : null,
                    strikethrough: member == "strikethrough" ? set : null);
                return true;
            case "charset" when value.Type == Vb6Value.ValueType.Integer && value.Value is int charset
                && charset is -1 or 0 or 1 or 2 or 128 or 255 or 256:
                if (charset is -1 or 256)
                    throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                font = new VBFont(charset switch { 2 => "Wingdings", 128 => "MS PGothic", 255 => "Arial", _ => font.FontFamilyName },
                    font.Size, italic: font.Italic, charset: charset == 1 ? 0 : charset,
                    underline: font.Underline, strikethrough: font.Strikethrough, weight: font.Weight);
                return true;
        }
        return false;
    }

    public void Call(string method, List<Vb6Value> args) =>
        throw new NotImplementedException($"Font.{method} is not implemented (#934)");
}
