using System.Collections.Generic;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.IDE;

public interface IComponentRegistry
{
    IReadOnlyList<IComponentClass> Components { get; }

    /// <summary>The controls the toolbox offers, in its order. A class an add-in registers joins the toolbox after them.</summary>
    IReadOnlyList<ToolboxEntry> Toolbox { get; }

    /// <summary>The control libraries a project can reference, which the Components dialog lists.</summary>
    IReadOnlyList<RuntimeLibrary> Libraries { get; }

    /// <summary>What those libraries offer the toolbox: one entry for each library that declares each control.</summary>
    IReadOnlyList<LibraryControl> LibraryControls { get; }

    IDisposable Register(IComponentClass component);
    event Action<IComponentClass>? ComponentRegistered;
    event Action<IComponentClass>? ComponentUnregistered;
}
