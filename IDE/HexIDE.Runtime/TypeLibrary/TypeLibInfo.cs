using System.Collections.Generic;

namespace HexIDE.Runtime.TypeLibrary;

public record TypeLibInfo(
    string Name,
    string? Documentation,
    IReadOnlyList<TypeInfo> Types);

public record TypeInfo(
    string Name,
    TypeKind Kind,
    string? Documentation,
    IReadOnlyList<TypeMemberInfo> Members);

public enum TypeKind { Class, Module, Enum, Interface, Alias, Union }
