using System.Collections.Generic;
using Avalonia.Controls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>A VB6 control array — N controls sharing one <see cref="Name"/> with distinct integer indices
/// (<c>Command1(0)</c>, <c>Command1(1)</c>, …). Bound to the shared name in module scope as a
/// <see cref="Vb6Value"/> (<c>CSharpProxyObject</c>), so the array name is a first-class object: <c>Command1(i)</c>
/// indexes it (the resolution sites read <see cref="TryGetElement"/>) and <c>.Count</c>/<c>.LBound</c>/<c>.UBound</c>
/// are <see cref="ICSharpPropertyBag"/> reads (all oracle-verified against vb6.exe). Backed by a sparse
/// <see cref="SortedDictionary{TKey,TValue}"/> because VB6 indices can be sparse and are grown dynamically by
/// <c>Load</c>/<c>Unload</c>.</summary>
public sealed class ControlArrayGroup : ICSharpProxy, ICSharpPropertyBag
{
    private readonly SortedDictionary<int, Control> elements = new();
    // Indices present at load time (from the .frm) — VB6 refuses to Unload a design-time element (Err 362).
    private readonly HashSet<int> designTimeIndices = new();
    // The lowest-index design-time component — Load clones it into the same container it came from.
    private ComponentInstance? template;
    private int templateIndex = int.MaxValue;
    // The canvas each element actually sits on, by index. Not one canvas for the whole group: a VB6 control
    // array genuinely spans containers — ODBC Log In.frm has a Frame that is itself array element 0, and
    // Treeview Listview Splitter.frm puts a two-element lblTitle array entirely inside one picTitles — so a
    // single field would clone a new element into whichever container happened to be registered last.
    private readonly Dictionary<int, Canvas> hosts = new();

    public ControlArrayGroup(string name) => Name = name;

    public string Name { get; }

    public int Count => elements.Count;

    /// <summary>The elements, index-ordered (ascending) — the enumeration order the Locals tree uses.</summary>
    public IReadOnlyDictionary<int, Control> Elements => elements;

    /// <summary>Register a design-time element (from the .frm). Tracks the lowest-index component as the Load
    /// template, and remembers which canvas each element sits on, so a later <see cref="Load"/> clones into
    /// the container the template came from rather than onto the form.</summary>
    public void AddDesignTimeElement(int index, Control control, ComponentInstance component, Canvas parentCanvas)
    {
        elements[index] = control;
        designTimeIndices.Add(index);
        hosts[index] = parentCanvas;
        if (index < templateIndex)
        {
            templateIndex = index;
            template = component;
        }
    }

    public bool TryGetElement(int index, out Control control) => elements.TryGetValue(index, out control!);

    /// <summary>VB6 <c>Load Command1(i)</c> — create a new element by cloning the lowest-index element's properties,
    /// forced to <c>Visible=False</c> (oracle: a loaded control starts hidden). Err 360 if the index already
    /// exists.</summary>
    public void Load(int index)
    {
        if (elements.ContainsKey(index))
            throw new VBRunTimeException(VBStandardError.ObjectAlreadyLoaded);            // 360
        if (template is not { } tmpl || !hosts.TryGetValue(templateIndex, out var parent))
            throw new VBRunTimeException(VBStandardError.CantLoadOrUnloadThisObject);      // 361 (defensive)

        // The clone is the control alone: if the template is itself a container, its contents are NOT
        // cloned with it. Pinned as a test rather than asserted as correct — what VB6 does when you Load a
        // new element of a Frame array is an open oracle question (see the change's open list).
        var control = ((ComponentBaseClass)tmpl.BaseClass).Instantiate(tmpl);
        Canvas.SetLeft(control, tmpl.GetPropertyOrDefault(VBProperties.LeftProperty));
        Canvas.SetTop(control, tmpl.GetPropertyOrDefault(VBProperties.TopProperty));
        control.Width = tmpl.GetPropertyOrDefault(VBProperties.WidthProperty);
        control.Height = tmpl.GetPropertyOrDefault(VBProperties.HeightProperty);
        control.IsVisible = false;                                                        // oracle: loaded → hidden
        VBProps.SetName(control, Name);
        VBProps.SetIndex(control, index);
        parent.Children.Add(control);
        elements[index] = control;
        hosts[index] = parent;
    }

    /// <summary>VB6 <c>Unload Command1(i)</c> — remove a runtime-loaded element. Err 340 if the index doesn't
    /// exist, Err 362 if it is a design-time element (which can't be unloaded).</summary>
    public void Unload(int index)
    {
        if (!elements.TryGetValue(index, out var control))
            throw new VBRunTimeException(VBStandardError.ControlArrayElementDoesntExist);            // 340
        if (designTimeIndices.Contains(index))
            throw new VBRunTimeException(VBStandardError.CantUnloadControlsCreatedAtDesignTime);      // 362
        if (hosts.TryGetValue(index, out var host))
            host.Children.Remove(control);
        elements.Remove(index);
        hosts.Remove(index);
    }

    // VB6: a control array's LBound/UBound are the lowest/highest indices present. An empty array (every element
    // Unloaded) is a degenerate case VB6 barely reaches; report 0/-1 rather than throwing.
    private int LBound()
    {
        foreach (var key in elements.Keys)
            return key;
        return 0;
    }

    private int UBound()
    {
        int last = -1;
        foreach (var key in elements.Keys)
            last = key;
        return last;
    }

    // ICSharpPropertyBag — the array's read-only metadata (`n = Command1.Count`). Names are matched
    // case-insensitively, as VB6 members are.
    public bool TryGetProperty(string name, out Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "count": value = new Vb6Value(Count); return true;
            case "lbound": value = new Vb6Value(LBound()); return true;
            case "ubound": value = new Vb6Value(UBound()); return true;
        }
        value = default;
        return false;
    }

    // The array's metadata is read-only (`Command1.Count = 3` is not valid VB6).
    public bool TrySetProperty(string name, Vb6Value value) => false;

    // ICSharpProxy is required to store the group as a Vb6Value. A control array exposes no void methods on the
    // group itself (indexing goes through the resolution sites, not Call), so this is intentionally a no-op.
    public void Call(string method, List<Vb6Value> args) { }
}
