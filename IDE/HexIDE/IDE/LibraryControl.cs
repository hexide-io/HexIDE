using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.IDE;

/// <summary>A control a library offers, which the toolbox shows while a project references that library.</summary>
/// <param name="Class">The control's class.</param>
/// <param name="Library">The library it is placed from.</param>
/// <param name="ControlName">Its name in the library: <c>ProgressBar</c>.</param>
/// <param name="TypeName">The type name a control placed from this library takes: <c>ComctlLib.ProgressBar</c>.</param>
/// <param name="IconKey">The key of the icon its button shows.</param>
public sealed record LibraryControl(IComponentClass Class, RuntimeLibrary Library, string ControlName, string TypeName, string IconKey);
