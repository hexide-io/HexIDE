using System;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

internal static class RuntimeExtensions
{
    /// <summary>
    /// An event raised as <see cref="ExecuteSub"/> raises it, behind whatever was raised before it, with
    /// <paramref name="atItsTurn"/> asked when its turn comes: false runs nothing. Without a handler it is still asked.
    /// </summary>
    public static void ExecuteSubIf(this Control control, EventClass eventClass, Func<bool> atItsTurn)
    {
        var handlers = ControlEvents.Handlers(control, control.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name);
        if (handlers.Count == 0)
        {
            atItsTurn();
            return;
        }
        // Asked once, at the first handler's turn, and what it answered holds for those behind it.
        bool? answered = null;
        bool Once() => answered ??= atItsTurn();
        foreach (var handler in handlers)
            handler.Root.ExecuteSubIf(handler.Name, ArgsFor(control, handler), Once);
    }

    public static void ExecuteSub(this Control control, EventClass eventClass)
    {
        // The LOGICAL tree, not the visual one: a window's content joins the visual tree only once the window is first
        // shown, and Form_Load now runs before that (#822). `Text1.Text = "x"` in Load raises Text1_Change in VB6, and
        // found no window through the visual tree (#1010 review). The content is a logical child as soon as it is set.
        foreach (var handler in ControlEvents.Handlers(control, control.FindLogicalAncestorOfType<IModuleExecutionRoot>(), eventClass.Name))
        {
            if (ReferenceEquals(eventClass, TimerComponentClass.TimerEvent))
                handler.Root.ExecuteTimer(handler.Name, ArgsFor(control, handler));
            else
                handler.Root.ExecuteSub(handler.Name, ArgsFor(control, handler));
        }
    }

    // A control-array element carries its Index; a shared handler is `Command1_Click(Index As Integer)`, so its own
    // handler is passed the Index as the leading arg. A standalone control, and a WithEvents variable, get no args.
    private static Vb6Value[]? ArgsFor(Control control, ControlEvents.Handler handler) =>
        handler.Own && VBProps.GetIndex(control) is { } index ? [new Vb6Value(index)] : null;
}