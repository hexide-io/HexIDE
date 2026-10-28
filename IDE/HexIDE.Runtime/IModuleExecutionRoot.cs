using System;
using System.Collections.Generic;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime;

public interface IModuleExecutionRoot
{
    // An editing event must return its ByRef arguments before the owner places or commits the editor.
    System.Threading.Tasks.Task ExecuteEditEventAsync(string name, IList<Vb6Value> args) =>
        throw new NotImplementedException();

    // args carries a control-array event's leading `Index As Integer` (null for a parameterless handler).
    void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null);

    // Keyboard arguments must return before the character is applied (#2325).
    void ExecuteKeyboardEvent(string name, IReadOnlyList<Vb6Value> args, int writable, Action<int> completed)
    {
        ExecuteSub(name, args);
        completed(Convert.ToInt32(args[writable].Value));
    }

    /// <summary>
    /// Raise an event that runs only if <paramref name="when"/> still holds when its turn comes, behind whatever was raised
    /// before it. A menu shortcut's item is one: its Enabled is read after its parents' Clicks have run, since they may
    /// set it (measured, #1344). A root with no queue of its own asks at once.
    /// </summary>
    void ExecuteSubIf(string name, IReadOnlyList<Vb6Value>? args, Func<bool> when)
    {
        if (when())
            ExecuteSub(name, args);
    }

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

    /// <summary>
    /// The run's event queue, which says whether a handler is running, or null for a root with none. An automation
    /// reply waits on it, so that it does not come back while the program is part way through what it did (#1232).
    /// </summary>
    EventPump? Events => null;
}
