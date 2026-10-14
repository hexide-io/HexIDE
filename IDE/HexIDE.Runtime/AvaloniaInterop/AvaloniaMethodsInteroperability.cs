using System;
using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

public static class AvaloniaMethodsInteroperability
{
    public static Vb6Value Call(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        // A form's Show and Hide, on a window the program drives (#822). Its load came first, where the member was looked
        // up. Show modally waits for the form, which is #822's next slice.
        if (c is IFormView view)
        {
            if (method.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Count > 0 && args[0].Type != Vb6Value.ValueType.EmptyVariant && !args[0].IsMissing
                    && Convert.ToDouble(VB6BuiltIns.ToNum(args[0]), System.Globalization.CultureInfo.InvariantCulture) != 0)
                    throw new NotImplementedException("Show vbModal is not implemented yet (#822).");
                view.ShowForm();
                return Vb6Value.Nothing;
            }
            if (method.Equals("hide", StringComparison.OrdinalIgnoreCase))
            {
                view.HideForm();
                return Vb6Value.Nothing;
            }
        }
        if (c is ItemsControl itemsControl)
        {
            if (method.Equals("additem", StringComparison.OrdinalIgnoreCase))
            {
                // AddItem's item is required; calling it with no argument is a trappable "Argument not optional"
                // (Err 449), not an uncatchable IndexOutOfRangeException off an empty args list.
                if (args.Count < 1)
                    throw new VBRunTimeException(VBStandardError.ArgumentNotOptionalOrInvalidPropertyAssignment);
                // AddItem's Item is a String, so the list holds CStr's text of it: `List1.AddItem 1 / 3` lists 0.333333333333333.
                itemsControl.Items.Add(VbDisplay.ToText(args[0]));
                return Vb6Value.Nothing;
            }
            else if (method.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                itemsControl.Items.Clear();
                return Vb6Value.Nothing;
            }
        }

        // A method VB6 has and HexIDE does not, such as Move, Refresh, SetFocus, Cls or a form's Hide. This was a
        // plain Exception, which the IDE reports as an internal fault. It is HexIDE's gap, not the program's, so
        // it says so, and it is never trappable: On Error Resume Next stepping over a Move that never happened
        // would leave the program believing the control had moved (#754). A member VB6 itself lacks is a
        // trappable 438 in VB6, and telling the two apart needs each control's VB6 method list.
        var target = VBProps.GetName(c) ?? c.GetType().Name;
        // SetFocus is refused on every control, its own form's included; #1061 tracks it.
        var tracked = method.Equals("SetFocus", StringComparison.OrdinalIgnoreCase) ? " (#1061)" : "";
        throw new NotImplementedException($"The {method} method is not implemented for {target}{tracked}");
    }
}