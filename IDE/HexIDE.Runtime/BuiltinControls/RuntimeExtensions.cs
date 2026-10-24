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
        if (VBProps.GetName(control) is { } name && control.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } executionRoot)
            executionRoot.ExecuteSubIf($"{name}_{eventClass.Name}", VBProps.GetIndex(control) is { } index ? [new Vb6Value(index)] : null,
                atItsTurn);
        else
            atItsTurn();
    }

    public static void ExecuteSub(this Control control, EventClass eventClass)
    {
        // The LOGICAL tree, not the visual one: a window's content joins the visual tree only once the window is first
        // shown, and Form_Load now runs before that (#822). `Text1.Text = "x"` in Load raises Text1_Change in VB6, and
        // found no window through the visual tree (#1010 review). The content is a logical child as soon as it is set.
        if (VBProps.GetName(control) is { } name &&
            control.FindLogicalAncestorOfType<IModuleExecutionRoot>() is { } executionRoot)
        {
            // A control-array element carries its Index; a shared handler is `Command1_Click(Index As Integer)`,
            // so pass the Index as the leading arg. A standalone control (no Index) dispatches with no args.
            var args = VBProps.GetIndex(control) is { } index
                ? new[] { new Vb6Value(index) }
                : null;
            if (ReferenceEquals(eventClass, TimerComponentClass.TimerEvent))
                executionRoot.ExecuteTimer($"{name}_{eventClass.Name}", args);
            else
                executionRoot.ExecuteSub($"{name}_{eventClass.Name}", args);
        }
    }
}