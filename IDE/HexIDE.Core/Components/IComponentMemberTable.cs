using System.Collections.Generic;

namespace HexIDE.Runtime.Components;

/// <summary>What kind of member a component class declares.</summary>
public enum ComponentMemberKind
{
    Property,
    Method,
    Event,
}

/// <summary>A member a component class declares, as the Object Browser lists it.</summary>
/// <param name="Name">The member's name.</param>
/// <param name="Kind">Whether it's a property, a method or an event.</param>
/// <param name="HexIdeOnly">Whether it's HexIDE's own, which no VB6 library has, so a program using it won't run in VB6.</param>
public sealed record ComponentMember(string Name, ComponentMemberKind Kind, bool HexIdeOnly);

/// <summary>
/// The members a component class declares, for a class that keeps a table of them (#5610). Optional: a class without one is
/// described by its properties and events instead, and so is one whose table is empty for the class as registered.
/// </summary>
public interface IComponentMemberTable
{
    /// <summary>
    /// The members a control of the class has in <paramref name="library"/>, in their table's order. The same class can be
    /// offered by two libraries with different members, as VB6's Common Controls 5.0 and 6.0 are; HexIDE's own members are
    /// in every library. Null asks for the class as registered: every member it declares.
    /// </summary>
    IReadOnlyList<ComponentMember> MembersIn(IControlLibrary? library);
}
