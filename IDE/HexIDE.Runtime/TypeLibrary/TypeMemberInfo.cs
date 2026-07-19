namespace HexIDE.Runtime.TypeLibrary;

public record TypeMemberInfo(
    string Name,
    MemberKind Kind,
    string Signature,
    string? Documentation,
    bool IsReadOnly);

public enum MemberKind { Method, PropertyGet, PropertyLet, PropertySet, Event, Constant }
