using System;
using System.Collections.Generic;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public interface IModuleExecutionRoot
{
    // args carries a control-array event's leading `Index As Integer` (null for a parameterless handler).
    void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null);

    /// <summary>
    /// Whether the module has a procedure of this name. An event raised as often as MouseMove is skipped where nothing
    /// handles it, rather than dispatched to find out.
    /// </summary>
    bool Handles(string name) => true;

    /// <summary>
    /// Raise an event whose arguments are worked out as it is dispatched (#974). One HexIDE can't work out, such as a
    /// mouse position in a ScaleMode it doesn't convert, is then refused as the run's own error, reported like any other,
    /// rather than thrown out of an input handler. <paramref name="coalescing"/> says how it waits behind a running
    /// handler: a MouseMove is <see cref="EventCoalescing.Latest"/> (#766).
    /// </summary>
    void ExecuteEvent(string name, Func<IReadOnlyList<Vb6Value>> args, EventCoalescing coalescing = EventCoalescing.None) =>
        ExecuteSub(name, args());

    /// <summary>
    /// A Timer control's tick. While that Timer's own handler runs the tick is dropped, and while another handler runs
    /// at most one waits (measured, #766). A root with no queue of its own runs it as any event.
    /// </summary>
    void ExecuteTimer(string name, IReadOnlyList<Vb6Value>? args = null) => ExecuteSub(name, args);

    /// <summary>
    /// Unload the form, as VB6's <c>Unload</c> statement does (#973): QueryUnload, then Unload, either of which may cancel.
    /// <paramref name="mode"/> is VB6's UnloadMode, 1 from code. Gives back whether the form unloaded.
    /// </summary>
    System.Threading.Tasks.Task<bool> UnloadAsync(int mode) =>
        throw new NotImplementedException("Unload of a form is not implemented here");
}
