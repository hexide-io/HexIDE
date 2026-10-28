using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

internal static class TextBoxKeyEvents
{
    internal static void Attach()
    {
        InputElement.KeyDownEvent.AddClassHandler<VBTextBox>(
            (box, e) => Raise(box, e, ComponentBaseClass.KeyDownEvent), RoutingStrategies.Tunnel);
        InputElement.KeyUpEvent.AddClassHandler<VBTextBox>(
            (box, e) => Raise(box, e, ComponentBaseClass.KeyUpEvent), RoutingStrategies.Tunnel);
    }

    private static void Raise(VBTextBox box, KeyEventArgs e, EventClass ev)
    {
        if (e.Handled || !AttachedEvents.OwnsThePointerEvent(box, e.Source))
            return;
        // First slice of #2325: only codes measured by its real-key probe. Enter/Esc and Tab need the form's
        // Default/Cancel/navigation arbitration before any key event; they are deliberately left to that path.
        var code = e.Key switch
        {
            Key.K => 75, Key.A => 65, Key.X => 88,
            Key.Back => 8, Key.Delete => 46, Key.Left => 37, Key.F2 => 113,
            Key.LeftShift or Key.RightShift => 16,
            Key.LeftCtrl or Key.RightCtrl => 17,
            Key.LeftAlt or Key.RightAlt => 18,
            _ => 0,
        };
        if (code == 0)
            return;
        var shift = (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 1 : 0)
                    | (e.KeyModifiers.HasFlag(KeyModifiers.Control) ? 2 : 0)
                    | (e.KeyModifiers.HasFlag(KeyModifiers.Alt) ? 4 : 0);
        Deliver(box, ev, code, shift, _ => { });
    }

    internal static void Deliver(Control box, EventClass ev, int code, int? shift, Action<int> completed)
    {
        var root = box.FindLogicalAncestorOfType<IModuleExecutionRoot>();
        void Control(int value)
        {
            if (value == 0)
            {
                completed(0);
                return;
            }
            var handlers = ControlEvents.Handlers(box, root, ev.Name);
            void Next(int i, int current)
            {
                if (i == handlers.Count)
                {
                    completed(current);
                    return;
                }
                var handler = handlers[i];
                if (!handler.Root.Handles(handler.Name))
                {
                    Next(i + 1, current);
                    return;
                }
                var args = new List<Vb6Value>();
                if (handler.Own && VBProps.GetIndex(box) is { } index)
                    args.Add(new Vb6Value(index));
                var writable = args.Count;
                args.Add(new Vb6Value(current));
                if (shift is { } mask)
                    args.Add(new Vb6Value(mask));
                handler.Root.ExecuteKeyboardEvent(handler.Name, args, writable, result => Next(i + 1, result));
            }
            Next(0, value);
        }
        if (root is AvaloniaObject form && AttachedProperties.GetKeyPreview(form))
        {
            IReadOnlyList<Vb6Value> args = shift is { } mask
                ? [new Vb6Value(code), new Vb6Value(mask)] : [new Vb6Value(code)];
            root.ExecuteKeyboardEvent($"Form_{ev.Name}", args, 0,
                result => Control(ev == ComponentBaseClass.KeyPressEvent || ev == ComponentBaseClass.KeyDownEvent && result == 0
                    ? result : code));
        }
        else
            Control(code);
    }
}
