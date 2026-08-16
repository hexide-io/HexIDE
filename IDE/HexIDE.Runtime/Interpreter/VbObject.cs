using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>One event connection (Phase 5): a listener class instance whose <c>WithEvents</c> variable
/// <see cref="VarName"/> is currently bound to a source object. The source dispatches <c>RaiseEvent Foo</c> to
/// the listener's <c>{VarName}_Foo</c> method (run on the listener instance). Not a counted reference — the
/// counted direction is the listener's <c>WithEvents</c> field holding the source; keeping the back-edge
/// uncounted stops events from manufacturing an uncollectable cycle.</summary>
public readonly record struct EventSink(Vb6Value Listener, string VarName);

/// <summary>
/// A class-module <em>instance</em> — a REFERENCE type (the inverse of <see cref="VbUdt"/>). It carries the
/// class definition (<see cref="ModuleInfo"/> — its methods are <c>PrePass.Procedures</c>, its module-level
/// <c>Dim</c>s are the instance-field template) and this instance's OWN field storage
/// (<see cref="InstanceEnv"/>: a per-instance analogue of a Standard module's shared <c>ModuleEnv</c>, with
/// fresh slots allocated per <c>New</c>).
///
/// Reference semantics are deliberate and load-bearing: <c>VbObject</c> does NOT override
/// <c>Equals</c>/<c>GetHashCode</c>, so two <c>Vb6Value</c>s wrapping the same instance compare equal by
/// reference (that is how <c>a Is b</c> works), and <see cref="BasicInterpreter.CopyIfValueType"/> never clones
/// it (so <c>Set b = a</c> shares one instance). <c>Nothing</c> is a <c>Vb6Value</c> of the Object type with a
/// null <c>Value</c> — not a <c>VbObject</c> at all.
/// </summary>
public sealed class VbObject
{
    public ModuleInfo ClassDef { get; }
    public ExecutionEnvironment InstanceEnv { get; }

    public string ClassName => ClassDef.Name;

    /// <summary>Live reference count — how many named-storage holds (variable/field slots) + transient holders
    /// (<c>Me</c> during a method, <c>ByVal</c> object params, statement temporaries) currently reference this
    /// instance. Maintained incrementally by <see cref="BasicInterpreter.AddRef"/>/<c>ReleaseRef</c> as the
    /// program runs (never inferred by scanning slots — see spec E2). <c>Class_Terminate</c> fires the instant it
    /// reaches zero. Reference cycles never reach zero, so they leak — faithful to VB6.</summary>
    public int RefCount;

    /// <summary>One-shot guard: set true immediately BEFORE dispatching <c>Class_Terminate</c>, so the hook's own
    /// transient <c>Me</c> bind can't re-enter termination and the object is never terminated twice.</summary>
    public bool Terminated;

    // Event connections currently bound to THIS object as their source (Phase 5), in attach order — VB6 events
    // are multicast (a per-source observer list). Lazily allocated (most objects are never event sources).
    private List<EventSink>? sinks;

    /// <summary>The sinks bound to this source, in attach order (empty if none). Callers that dispatch must
    /// snapshot (copy) first — a handler may attach/detach mid-dispatch.</summary>
    public IReadOnlyList<EventSink> Sinks => sinks ?? (IReadOnlyList<EventSink>)Array.Empty<EventSink>();

    /// <summary>Register a listener's <c>WithEvents</c> connection to this source (append = attach order).</summary>
    public void AddSink(EventSink sink) => (sinks ??= new List<EventSink>()).Add(sink);

    /// <summary>Remove a listener instance's connection for a given <c>WithEvents</c> var name (on unbind/rebind).</summary>
    public void RemoveSink(VbObject listener, string varName)
        => sinks?.RemoveAll(s => ReferenceEquals(s.Listener.Value, listener)
                                 && string.Equals(s.VarName, varName, StringComparison.OrdinalIgnoreCase));

    public VbObject(ModuleInfo classDef, ExecutionEnvironment instanceEnv)
    {
        ClassDef = classDef;
        InstanceEnv = instanceEnv;
    }
}
