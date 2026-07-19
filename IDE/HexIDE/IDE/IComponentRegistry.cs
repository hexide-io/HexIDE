using System.Collections.Generic;
using HexIDE.Runtime.Components;

namespace HexIDE.IDE;

public interface IComponentRegistry
{
    IReadOnlyList<IComponentClass> Components { get; }
    void Register(IComponentClass component);
    event Action<IComponentClass>? ComponentRegistered;
}
