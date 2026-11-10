using HexIDE.Runtime.Components;

namespace HexIDE.IDE;

/// <summary>A control class's place in the toolbox: the class, and the key of the icon its button shows.</summary>
public readonly record struct ToolboxEntry(IComponentClass Class, string IconKey);
