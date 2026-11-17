using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// #2325: measured buttons, lists and empty forms share the TextBox's form-first delivery path.
internal static class ControlKeyEvents
{
    private static readonly HashSet<TextInputEventArgs> Forwarded = [];
    private static readonly ConditionalWeakTable<Control, HashSet<Key>> UnmeasuredArrows = new();
    private sealed class SpaceKey
    {
        public bool Suppressed;
    }
    private static readonly ConditionalWeakTable<Control, SpaceKey> Spaces = new();

    internal static void Attach<T>() where T : Control
    {
        InputElement.KeyDownEvent.AddClassHandler<T>((control, e) => RaiseKey(control, e, false), RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<T>((control, e) => RaiseKey(control, e, true), RoutingStrategies.Tunnel);
        InputElement.TextInputEvent.AddClassHandler<T>((control, e) => Text(control, e), RoutingStrategies.Tunnel);
    }

    private static bool Eligible(Control control) => control is not VBComboBox { IsSimpleCombo: true };
    private static bool Button(Control control) => control is VBCommandButton or VBCheckBox or VBOptionButton;
    private static int Shift(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
        | (modifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
        | (modifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);

    private static void RaiseKey(Control control, KeyEventArgs e, bool up)
    {
        // A combo's popup is on its routed input path, though it is not a visual child.
        if (e.Handled || !Eligible(control) || control is not VBComboBox && !AttachedEvents.OwnsThePointerEvent(control, e.Source))
            return;
        if (!up && control is VBComboBox { IsEditable: true, IsDropDownOpen: false, SelectedIndex: < 0 } combo
            && !string.IsNullOrEmpty(combo.Text) && e.Key is Key.Up or Key.Down or Key.Left or Key.Right)
        {
            UnmeasuredArrows.GetOrCreateValue(control).Add(e.Key);
            return;
        }
        if (up && UnmeasuredArrows.TryGetValue(control, out var arrows) && arrows.Remove(e.Key))
            return;
        // Buttons give arrows to focus navigation. A combo that receives their release still raises KeyUp.
        if (e.Key == Key.Tab
            || Button(control) && e.Key is Key.Left or Key.Up or Key.Right or Key.Down)
            return;
        var code = TextBoxKeyEvents.KeyCode(e);
        if (code == 0)
            return;
        TextBoxKeyEvents.Deliver(control, up ? ComponentBaseClass.KeyUpEvent : ComponentBaseClass.KeyDownEvent,
            code, Shift(e.KeyModifiers), _ => { });
        if (!up && e.Key is Key.Enter or Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            TextBoxKeyEvents.Deliver(control, ComponentBaseClass.KeyPressEvent, code, null, _ => { });
            if (control is VBComboBox { IsDropDownOpen: true } openCombo)
                openCombo.SetCurrentValue(ComboBox.IsDropDownOpenProperty, false);
            e.Handled = true;
        }
        if (Button(control) && e.Key == Key.Space)
        {
            if (!up)
                Spaces.GetOrCreateValue(control).Suppressed = false;
            else if (Spaces.TryGetValue(control, out var space))
            {
                // Avalonia clicks on Space's release even when its press was swallowed.
                e.Handled |= space.Suppressed;
                Spaces.Remove(control);
            }
        }
        if (!up && (Button(control) && e.Key == Key.Space || e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control))
        {
            var keyAscii = e.Key == Key.Space ? 32 : 1;
            TextBoxKeyEvents.Deliver(control, ComponentBaseClass.KeyPressEvent, keyAscii, null,
                ascii =>
                {
                    var replacement = ascii != 0 && ascii != keyAscii;
                    var suppressed = ascii == 0 || ascii is < 0 or > 127 || replacement;
                    if (Button(control) && e.Key == Key.Space)
                        Spaces.GetOrCreateValue(control).Suppressed = suppressed;
                    e.Handled |= suppressed;
                    CheckAscii(ascii);
                    if (replacement)
                        throw new NotImplementedException("Changing KeyAscii for a synthesized Space or Ctrl+A is not implemented (#2325)");
                });
        }
    }

    private static void Text(Control control, TextInputEventArgs e)
    {
        if (e.Handled || Forwarded.Contains(e) || !Eligible(control)
            || control is not VBComboBox && !AttachedEvents.OwnsThePointerEvent(control, e.Source) || string.IsNullOrEmpty(e.Text))
            return;
        if (Button(control) && e.Text == " ")
        {
            e.Handled = true; // Space's KeyPress precedes the button's release-time Click.
            return;
        }
        foreach (var character in e.Text)
            if (character > 127 || character is '\r' or '\x1b' or '\t')
                return; // Incoming ANSI conversion is unmeasured, as on the TextBox.
        e.Handled = true;
        foreach (var character in e.Text)
            TextBoxKeyEvents.Deliver(control, ComponentBaseClass.KeyPressEvent, character, null, ascii =>
            {
                CheckAscii(ascii);
                if (ascii == 0)
                    return;
                var forwarded = new TextInputEventArgs
                {
                    RoutedEvent = InputElement.TextInputEvent, Text = ((char)ascii).ToString(),
                };
                Forwarded.Add(forwarded);
                try
                {
                    (e.Source as InputElement ?? control).RaiseEvent(forwarded);
                }
                finally
                {
                    Forwarded.Remove(forwarded);
                }
            });
    }

    private static void CheckAscii(int ascii)
    {
        if (ascii is < 0 or > 127)
            throw new NotImplementedException("Non-ASCII KeyAscii conversion is not implemented (#2325)");
    }

    internal static void AttachEmptyForm(VBFormRuntime form)
    {
        bool Empty() => form.Context.FormInstance?.Controls is { Count: 0 };
        void Deliver(EventClass ev, int code, int? shift = null)
        {
            IReadOnlyList<Vb6Value> args = shift is { } mask
                ? [new Vb6Value(code), new Vb6Value(mask)] : [new Vb6Value(code)];
            form.ExecuteKeyboardEvent($"Form_{ev.Name}", args, 0, _ => { });
        }
        form.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Handled || !Empty()) return;
            var code = e.Key switch
            {
                Key.Enter when e.KeyModifiers == KeyModifiers.None => 13,
                Key.Escape when e.KeyModifiers == KeyModifiers.None => 27,
                Key.Tab when e.KeyModifiers == KeyModifiers.None => 9,
                _ => TextBoxKeyEvents.KeyCode(e),
            };
            if (code == 0) return;
            Deliver(ComponentBaseClass.KeyDownEvent, code, Shift(e.KeyModifiers));
            if (e.Key is Key.Enter or Key.Escape or Key.Tab || e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
                Deliver(ComponentBaseClass.KeyPressEvent, e.Key == Key.A ? 1 : code);
        }, RoutingStrategies.Tunnel);
        form.AddHandler(InputElement.KeyUpEvent, (_, e) =>
        {
            if (e.Handled || !Empty() || e.Key == Key.Tab) return;
            var code = e.Key switch
            {
                Key.Enter when e.KeyModifiers == KeyModifiers.None => 13,
                Key.Escape when e.KeyModifiers == KeyModifiers.None => 27,
                _ => TextBoxKeyEvents.KeyCode(e),
            };
            if (code != 0) Deliver(ComponentBaseClass.KeyUpEvent, code, Shift(e.KeyModifiers));
        }, RoutingStrategies.Tunnel);
        form.AddHandler(InputElement.TextInputEvent, (_, e) =>
        {
            if (e.Handled || !Empty() || string.IsNullOrEmpty(e.Text)) return;
            foreach (var character in e.Text)
                if (character is >= ' ' and <= '\x7f') Deliver(ComponentBaseClass.KeyPressEvent, character);
        }, RoutingStrategies.Tunnel);
    }
}
