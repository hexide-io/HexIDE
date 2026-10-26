namespace HexIDE.Runtime.Components.InBox;

/// <summary>Where a member of an in-box control comes from (#1082): which of its libraries declare it, or HexIDE.</summary>
/// <remarks>Flags, because the two questions are apart: a 5.0 save reads which versions have the member, and a strict VB6
/// mode, which doesn't exist yet and belongs with profiles, would read whether it is HexIDE's own.</remarks>
[Flags]
public enum MemberOrigin
{
    None = 0,
    /// <summary>The 5.0 library declares it: COMCTL32.OCX's <c>ComctlLib</c>.</summary>
    Version5 = 1,
    /// <summary>The 6.0 library declares it: MSCOMCTL.OCX's <c>MSComctlLib</c>.</summary>
    Version6 = 2,
    /// <summary>HexIDE's own, which no VB6 library has.</summary>
    HexIde = 4,
}

/// <summary>What kind of member it is.</summary>
public enum MemberKind
{
    Property,
    Method,
    Event,
}

/// <summary>
/// One of an in-box control's own members, as its type libraries declare it (#1082): not the extender's, which VB adds to
/// every control.
/// </summary>
/// <param name="IsFrmProperty">Whether its value is written in a <c>.frm</c>. A HexIDE member never is, or the form would stop
/// being one VB6 can open.</param>
/// <param name="Implemented">Whether HexIDE implements it. One it doesn't is refused as not implemented, where a member the
/// control lacks is VB6's own error.</param>
public sealed record InBoxMember(string Name, MemberKind Kind, MemberOrigin Origin, bool IsFrmProperty, bool Implemented)
{
    /// <summary>Whether a control of <paramref name="library"/> has the member.</summary>
    public bool IsIn(RuntimeLibrary library) =>
        (Origin & (library == ControlLibraries.ComctlLib ? MemberOrigin.Version5 : MemberOrigin.Version6)) != 0;
}
