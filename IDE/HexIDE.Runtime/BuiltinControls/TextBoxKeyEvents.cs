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
        if (e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift or KeyModifiers.Control or KeyModifiers.Alt)
            && e.Key is not (Key.K or Key.A or Key.X or Key.Back or Key.Delete or Key.Left or Key.F2
                or Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt))
            return;
        // #2325's console keyboard sweep (issuecomment-6454480155), native and p-code alike.
        // Enter/Esc and Tab still need the form's Default/Cancel/navigation arbitration.
        var code = e.Key switch
        {
            >= Key.A and <= Key.Z => 65 + e.Key - Key.A,
            >= Key.D0 and <= Key.D9 => 48 + e.Key - Key.D0,
            >= Key.NumPad0 and <= Key.NumPad9 when e.KeyModifiers == KeyModifiers.None => 96 + e.Key - Key.NumPad0,
            >= Key.F1 and <= Key.F12 when e.Key != Key.F10
                && !(e.Key == Key.F4 && e.KeyModifiers.HasFlag(KeyModifiers.Alt)) => 112 + e.Key - Key.F1,
            Key.Space when !e.KeyModifiers.HasFlag(KeyModifiers.Alt) => 32,
            Key.Back => 8, Key.Insert => 45, Key.Delete => 46,
            Key.Home => 36, Key.End => 35, Key.PageUp => 33, Key.PageDown => 34,
            Key.Left => 37, Key.Up => 38, Key.Right => 39, Key.Down => 40, Key.Clear => 12,
            Key.Decimal when e.KeyModifiers == KeyModifiers.None => 110,
            Key.Add when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None => 107,
            Key.Subtract when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None => 109,
            Key.Multiply when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None => 106,
            Key.Divide when (e.KeyModifiers & ~KeyModifiers.Shift) == KeyModifiers.None => 111,
            Key.NumLock when e.KeyModifiers == KeyModifiers.None => 144,
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
