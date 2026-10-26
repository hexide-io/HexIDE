using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// Which handlers a control's event reaches (#1556): its own, by its name, and every <c>WithEvents</c> variable holding it.
/// Measured with vb6.exe, native and p-code alike (the oracle's "Controls.Add and Controls.Remove"):
/// <list type="bullet">
/// <item>The control's own handler runs first, then each variable holding it, the one set last first: <c>Command1_Click</c>,
/// then <c>b_Click</c>; with <c>b</c> and then <c>b2</c> set to one control, <c>b2_Click</c> and then <c>b_Click</c>.</item>
/// <item>A control added with <c>Controls.Add</c> has no handler of its own, whatever its name: a form's <c>a0_Click</c>
/// doesn't run for a control added as <c>a0</c>. Only a variable holding it hears it.</item>
/// <item>A control array element can't be held (459, at the Set).</item>
/// </list>
/// </summary>
internal static class ControlEvents
{
    /// <summary>A <c>WithEvents</c> variable holding the control: the form instance it belongs to, and that form's root.</summary>
    private sealed record Sink(VbObject Listener, IModuleExecutionRoot Root, string VarName);

    private static readonly ConditionalWeakTable<Control, List<Sink>> sinks = new();

    /// <summary>One handler an event reaches: the root that runs it, its name, and whether it is the control's own.</summary>
    internal readonly record struct Handler(IModuleExecutionRoot Root, string Name, bool Own);

    /// <summary><c>Set b = control</c>, where <c>b</c> is a <c>WithEvents</c> variable of the form <paramref name="listener"/>.</summary>
    internal static void Advise(Control control, VbObject listener, IModuleExecutionRoot root, string varName) =>
        sinks.GetOrCreateValue(control).Insert(0, new Sink(listener, root, varName));

    /// <summary>The variable lets go of the control: it is set to something else, or to Nothing.</summary>
    internal static void Unadvise(Control control, VbObject listener, string varName)
    {
        if (sinks.TryGetValue(control, out var list))
            list.RemoveAll(sink => ReferenceEquals(sink.Listener, listener)
                && string.Equals(sink.VarName, varName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Every handler an event of <paramref name="control"/> reaches, in the order they run. <paramref name="own"/> is the
    /// root of the form the control is on, or null where it is on none.
    /// </summary>
    internal static List<Handler> Handlers(Control control, IModuleExecutionRoot? own, string eventName)
    {
        var handlers = new List<Handler>();
        if (own is not null && !VBProps.GetAddedAtRunTime(control) && VBProps.GetName(control) is { } name)
            handlers.Add(new Handler(own, $"{name}_{eventName}", true));
        if (sinks.TryGetValue(control, out var list))
            handlers.AddRange(list.Where(sink => !sink.Listener.Terminated)
                .Select(sink => new Handler(sink.Root, $"{sink.VarName}_{eventName}", false)));
        return handlers;
    }
}
