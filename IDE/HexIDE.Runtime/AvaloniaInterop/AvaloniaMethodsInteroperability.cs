using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

public static class AvaloniaMethodsInteroperability
{
    public static Vb6Value Call(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        VBProps.RefuseIfRemoved(c);
        if (c is VBUpDown && (method.Equals("OLEDrag", StringComparison.OrdinalIgnoreCase)
                              || method.Equals("AboutBox", StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException($"UpDown {method} is not implemented (#1093)");
        if (c is VBMaskEdBox && Components.InBox.MaskedEdBoxComponentClass.Instance.Member(method) is not null)
            throw VBMaskEdBox.Refused(method);
        if (c is VBTimer)
            TimerMembers.RefuseMethod(method);

        if (c is VBTabStrip strip && TabStripMembers.IsOwn(method))
        {
            if (!method.Equals("DeselectAll", StringComparison.OrdinalIgnoreCase))
                throw TabStripMembers.Refused(method);
            strip.Model.DeselectAll((int)(long)strip.Values["Style"].Value!);
            return Vb6Value.Nothing;
        }
        if (c is VBToolbar)
            VBToolbar.RefusePending(method);
        // An in-box control's own methods, and those it lacks, by its member table (#1082). One it lacks, such as a
        // ProgressBar's SetFocus, is refused as a property it lacks is: VB6's compile error on the early-bound call, which
        // is 438 late-bound (measured).
        RichTextBoxMembers.RefuseMethod(c, method);
        if (c is VBSSTab && Components.InBox.SSTabComponentClass.Instance.Member(method) is { Implemented: false })
            throw new NotImplementedException($"SSTab {method} is not implemented");
        if ((StatusBarMembers.TryCall(c, method) ?? ProgressBarMembers.TryCall(c, method)) is { } inBox)
            return inBox ? Vb6Value.Nothing : throw new VBMethodOrDataMemberNotFoundException(method, Vb6Value.ValueType.Control);
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
                ListMembers.AddItem(itemsControl, args);
                return Vb6Value.Nothing;
            }
            if (method.Equals("removeitem", StringComparison.OrdinalIgnoreCase))
            {
                ListMembers.RemoveItem(itemsControl, args);
                return Vb6Value.Nothing;
            }
            else if (method.Equals("clear", StringComparison.OrdinalIgnoreCase))
            {
                itemsControl.Items.Clear();
                return Vb6Value.Nothing;
            }
        }

        if (method.Equals("move", StringComparison.OrdinalIgnoreCase))
            return c is Window form ? MoveForm(form, args) : Move(c, args);

        if (method.Equals("zorder", StringComparison.OrdinalIgnoreCase) && c is not Window)
            return ZOrder(c, args);

        if (method.Equals("setfocus", StringComparison.OrdinalIgnoreCase) && args.Count == 0 && (c is IFormView || c.Focusable))
            return SetFocus(c);

        if (c is VBTreeView)
            throw new NotImplementedException($"TreeView.{method} (#1086)");

        // A method VB6 has and HexIDE does not, such as Refresh, SetFocus or Cls. This was a
        // plain Exception, which the IDE reports as an internal fault. It is HexIDE's gap, not the program's, so
        // it says so, and it is never trappable: On Error Resume Next stepping over a Move that never happened
        // would leave the program believing the control had moved (#754). A member VB6 itself lacks is a
        // trappable 438 in VB6, and telling the two apart needs each control's VB6 method list.
        var target = VBProps.GetName(c) ?? c.GetType().Name;
        throw new NotImplementedException($"The {method} method is not implemented for {target}");
    }

    /// <summary>
    /// SetFocus (#1061), as measured on vb6.exe, native and p-code alike, on the console ("SetFocus" in the oracle):
    /// <list type="bullet">
    /// <item>on a form that isn't shown it is 5, Invalid procedure call: in its Form_Load, on a control or on the form
    /// itself, and on a control of a form loaded but not shown;</item>
    /// <item>on a disabled or invisible control it is 5, and nothing changes;</item>
    /// <item>otherwise the control has the focus at once, and its form is activated if another one had it. The old
    /// control's LostFocus and the new one's GotFocus come after the handler that called SetFocus has returned, as
    /// messages Windows posts;</item>
    /// <item>it raises no Validate, and on the control that already has the focus it raises nothing.</item>
    /// </list>
    /// A control that can't take the focus, such as a Label, has no SetFocus in VB6, and is refused as not implemented
    /// before this is reached.
    /// </summary>
    private static Vb6Value SetFocus(Control c)
    {
        var view = c as IFormView ?? c.GetLogicalAncestors().OfType<IFormView>().FirstOrDefault();
        if (view is not { IsShown: true } || c is not IFormView && (!c.IsEffectivelyVisible || !c.IsEffectivelyEnabled))
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        view.ActivateForm();
        if (c is not IFormView)
        {
            using var posting = view.Context.Events.Posting();
            c.Focus();
        }
        return Vb6Value.Nothing;
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
    /// A form's Move (#1569), as measured on vb6.exe ("A form's Move" in the oracle):
    /// <list type="bullet">
    /// <item>Left, then Top, Width and Height where they are given, always in twips: a form has no container, and its own
    /// ScaleMode doesn't apply. A half twip rounds to even.</item>
    /// <item>Every value is checked before any is set: a negative size is 5, a value past a Long in twips 6, and Null 94,
    /// with the form left where it was.</item>
    /// <item>A change of size raises one Resize, with both new, where assigning Width and then Height raises two.</item>
    /// <item><c>Move</c> with nothing, or with Left left out, is "Argument not optional", and five arguments "Wrong number of
    /// arguments", both compile errors. <c>Move 600, , 4800</c> compiles, and raises 448 with nothing moved.</item>
    /// </list>
    /// Left and Top go where an assignment to them puts them, which doesn't yet move the window (#852).
    /// </summary>
    private static Vb6Value MoveForm(Window form, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count == 0 || args[0].IsMissing)
            throw new VBCompileErrorException("Argument not optional");
        if (args.Count > 4)
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        if (args.Any(a => a.IsMissing))
            throw new VBRunTimeException(VBStandardError.NamedArgumentNotFound);
        // Session 0 ignores a WindowState, so this was not measured: the console is where VB6 can be asked.
        if (form.WindowState != WindowState.Normal)
            throw new NotImplementedException("A form's Move while it is minimized or maximized is not implemented: what VB6 "
                + "does then was not measured (#1569)");
        for (var i = 0; i < args.Count; i++)
        {
            if (AvaloniaInteroperability.ConvertForAssignment(MoveOrder[i], args[i]) is not double twips)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            if (i >= 2 && twips < 0)
                throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
            ControlPositions.ToPixels(form, MoveOrder[i], twips);   // 6 past a Long, before anything moves
        }
        using (form is VBFormRuntime runtime ? runtime.ResizeOnce() : null)
        {
            for (var i = 0; i < args.Count; i++)
                if (!AvaloniaInteroperability.TrySet(form, MoveOrder[i], args[i]))
                    throw new NotImplementedException($"The Move method is not implemented for {VBProps.GetName(form) ?? form.GetType().Name}");
        }
        return Vb6Value.Nothing;
    }

    /// <summary>
    /// A control's ZOrder (#1503), as measured on vb6.exe at run time ("What ZOrder does at run time" in the oracle):
    /// <list type="bullet">
    /// <item>0, the default, puts it over every control of its plane in its container, and 1 under them. A Label never
    /// rises over a windowed control, nor a windowed control sinks under a Label.</item>
    /// <item>A container takes its contents with it, and an array element moves alone.</item>
    /// <item>The position is an Integer: 2 and -1 are 5, a half goes to even, 32768 is 6, and "x" is 13. So is Null,
    /// where CInt gives 94.</item>
    /// </list>
    /// </summary>
    private static Vb6Value ZOrder(Control c, IReadOnlyList<Vb6Value> args)
    {
        if (args.Count > 1)
            throw new NotImplementedException("The ZOrder method with more than one argument is not implemented");
        var position = 0;
        if (args.Count == 1 && !args[0].IsMissing)
        {
            if (args[0].IsNull)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            position = Convert.ToInt32(VB6BuiltIns.NarrowNum(args[0], Vb6Value.ValueType.Integer).Value);
        }
        if (position is not (0 or 1))
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        if (c.Parent is Panel container)
        {
            var windowless = ControlStacking.IsWindowless(c);
            if (position == 0)
                ControlStacking.PlaceOver(c, windowless, container);
            else
                ControlStacking.PlaceUnder(c, windowless, container);
        }
        return Vb6Value.Nothing;
    }

    /// <summary>
    /// <see cref="Call"/>, for a statement, which can wait: <c>Form2.Show vbModal</c> returns once Form2 has gone (#822).
    /// </summary>
    public static async ValueTask<Vb6Value> CallAsync(this Control c, string method, IReadOnlyList<Vb6Value> args)
    {
        VBProps.RefuseIfRemoved(c);
        if (c is VBTreeView tree && method.Equals("StartLabelEdit", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Count != 0) throw new NotImplementedException();
            await tree.StartLabelEditAsync();
            return Vb6Value.Nothing;
        }
        // A CommonDialog's ShowOpen and the rest wait for the dialog (#1084).
        if (c is VBCommonDialog dialog && VBCommonDialog.Handles(method))
            return await dialog.CallAsync(method, args);
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