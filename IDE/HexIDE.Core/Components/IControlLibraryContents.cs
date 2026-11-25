using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>A type a control library declares, as the Object Browser lists it when no type library answers (#5610).</summary>
/// <param name="Name">The type's name in the library: <c>ListView</c>, or a class such as <c>ListItem</c>.</param>
/// <param name="Component">Its component class, for a control HexIDE models, whether or not the toolbox offers it.</param>
/// <param name="DeclaredMembers">For a type with no component class, the names of the members it declares.</param>
public sealed record LibraryType(string Name, IComponentClass? Component, IReadOnlyList<string> DeclaredMembers);

/// <summary>
/// Everything a control library declares, for the Object Browser to list when no type library describes it (#5610).
/// Optional: a library without it is listed by the controls it registered.
/// </summary>
public interface IControlLibraryContents
{
    /// <summary>Its types, each once, in the library's own order. The Object Browser sorts them for showing.</summary>
    IReadOnlyList<LibraryType> Types { get; }
}
