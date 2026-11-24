using System.Collections.Generic;
using HexIDE.Runtime.Components;

namespace HexIDE.IDE;

public interface IComponentRegistry
{
    /// <summary>The classes the designer knows, in the order their owners registered them (#5594).</summary>
    IReadOnlyList<IComponentClass> Components { get; }

    /// <summary>The controls the toolbox offers, in the order their owners put them there.</summary>
    IReadOnlyList<ToolboxEntry> Toolbox { get; }

    /// <summary>The control libraries a project can reference, which the Components dialog lists, as their owners registered them.</summary>
    IReadOnlyList<IControlLibrary> Libraries { get; }

    /// <summary>What those libraries offer the toolbox: one entry for each library that declares each control.</summary>
    IReadOnlyList<LibraryControl> LibraryControls { get; }

    event Action<IComponentClass>? ComponentRegistered;
    event Action<IComponentClass>? ComponentUnregistered;

    /// <summary>An owner put a class in the toolbox, after the items already there (#5594).</summary>
    event Action<ToolboxEntry>? ToolboxItemAdded;

    /// <summary>A toolbox item was taken out, by its owner or with its class.</summary>
    event Action<ToolboxEntry>? ToolboxItemRemoved;

    /// <summary>A control library was registered or taken out (#5599), so what a project's references offer may differ.</summary>
    event Action? LibrariesChanged;
}
