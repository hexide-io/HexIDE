using System.Collections.Generic;
using HexIDE.Runtime.Components;

namespace HexIDE.IDE;

public interface IComponentRegistry
{
    IReadOnlyList<IComponentClass> Components { get; }

    /// <summary>The controls the toolbox offers, in its order. A class an add-in registers joins the toolbox after them.</summary>
    IReadOnlyList<ToolboxEntry> Toolbox { get; }

    IDisposable Register(IComponentClass component);
    event Action<IComponentClass>? ComponentRegistered;
    event Action<IComponentClass>? ComponentUnregistered;
}
