using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

public static class AvaloniaMethodsInteroperability
{
    public static Vb6Value Call(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        // A form's Show and Hide, on a window the program drives (#822). Its load came first, where the member was looked
        // up. A modal Show waits for the form, so only a statement, through CallAsync, can make one.
        if (c is IFormView view)
        {
            if (method.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                if (IsModal(args))
                    throw new NotImplementedException("Show vbModal is not implemented here, only as a statement (#822).");
                if (view.Context.Program is { } program)
                    program.Show(view);
                else
                    view.ShowForm();
                return Vb6Value.Nothing;
            }
            if (method.Equals("hide", StringComparison.OrdinalIgnoreCase))
            {
                if (view.Context.Program is { } program)
                    program.Hide(view);
                else
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

        if (method.Equals("move", StringComparison.OrdinalIgnoreCase) && c is not Window)
            return Move(c, args);

        // A method VB6 has and HexIDE does not, such as Refresh, SetFocus or Cls. This was a
        // plain Exception, which the IDE reports as an internal fault. It is HexIDE's gap, not the program's, so
        // it says so, and it is never trappable: On Error Resume Next stepping over a Move that never happened
        // would leave the program believing the control had moved (#754). A member VB6 itself lacks is a
        // trappable 438 in VB6, and telling the two apart needs each control's VB6 method list.
        var target = VBProps.GetName(c) ?? c.GetType().Name;
        // SetFocus is refused on every control, its own form's included; #1061 tracks it.
        var tracked = method.Equals("SetFocus", StringComparison.OrdinalIgnoreCase) ? " (#1061)" : "";
        throw new NotImplementedException($"The {method} method is not implemented for {target}{tracked}");
    }

    private static readonly PropertyClass[] MoveOrder =
        [VBProperties.LeftProperty, VBProperties.TopProperty, VBProperties.WidthProperty, VBProperties.HeightProperty];

    /// <summary>
    /// A control's Move (#1071): Left, then Top, Width and Height where they are given, each as its property takes it, in
    /// the container's ScaleMode. Measured: <c>Move 100</c> sets Left alone and <c>Move 200, 300</c> Left and Top, 450.5
    /// reads 450, and a negative width is 5 with nothing moved, Left included. So the check comes before any is set.
    /// </summary>
    private static Vb6Value Move(Control c, IReadOnlyList<Vb6Value> args)
    {
        // An omitted argument, `Move 1, , 3`, and Move with none at all, are not measured.
        if (args.Count is 0 or > 4 || args.Any(a => a.IsMissing))
            throw new NotImplementedException($"The Move method with {args.Count} arguments, or with one omitted, is not implemented");
        for (var i = 2; i < args.Count; i++)
            if (AvaloniaInteroperability.ConvertForAssignment(MoveOrder[i], args[i]) is double size && size < 0)
                throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        for (var i = 0; i < args.Count; i++)
            if (!AvaloniaInteroperability.TrySet(c, MoveOrder[i], args[i]))
                throw new NotImplementedException($"The Move method is not implemented for {VBProps.GetName(c) ?? c.GetType().Name}");
        return Vb6Value.Nothing;
    }

    /// <summary>
    /// <see cref="Call"/>, for a statement, which can wait: <c>Form2.Show vbModal</c> returns once Form2 has gone (#822).
    /// </summary>
    public static async ValueTask<Vb6Value> CallAsync(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        if (c is IFormView view && method.Equals("show", StringComparison.OrdinalIgnoreCase) && IsModal(args))
        {
            var program = view.Context.Program
                          ?? throw new NotImplementedException("Show vbModal needs a program of forms, which this host doesn't run (#822).");
            await program.ShowModal(view);
            return Vb6Value.Nothing;
        }
        return c.Call(method, args);
    }

    // Show's style: 0 is modeless and 1 modal, converted as CInt converts it, so 0.6 is modal and 1.5 is 2. Anything else is
    // 5, "Invalid procedure call or argument", after the form has loaded (measured, #822).
    private static bool IsModal(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count == 0 || args[0].IsMissing)
            return false;
        var style = Convert.ToInt32(VB6BuiltIns.NarrowNum(args[0], Vb6Value.ValueType.Integer).Value,
            System.Globalization.CultureInfo.InvariantCulture);
        return style switch
        {
            0 => false,
            1 => true,
            _ => throw new VBRunTimeException(VBStandardError.InvalidProcedureCall),
        };
    }
}