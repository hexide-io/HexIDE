namespace HexIDE.Office;

public enum VbaModuleKind
{
    Standard,
    Class,
    Document,
    UserForm
}
public enum VbaReferenceKind
{
    Registered,
    Project,
    Control
}

public sealed record VbaReference(string? Name, VbaReferenceKind Kind, string Libid,
    string? RelativeLibid = null, uint? MajorVersion = null, ushort? MinorVersion = null,
    string? OriginalLibid = null, string? ExtendedLibid = null, string? ExtendedName = null,
    Guid? OriginalTypeLibrary = null, uint? Cookie = null);

public sealed record VbaProjectInformation(uint SystemKind, uint? CompatibilityVersion, uint Lcid,
    uint LcidInvoke, int DeclaredCodePage, string Name, string DocString, string HelpFile,
    uint HelpContext, uint LibraryFlags, uint MajorVersion, ushort MinorVersion, string Constants);

public sealed record VbaModule(string Name, string StreamName, VbaModuleKind Kind, ushort ModuleType,
    uint SourceOffset, bool IsReadOnly, bool IsPrivate, string DocString, uint HelpContext,
    string? Source, CompoundEntry? DesignerStorage);

/// <summary>Designer entries borrow the locator's lifetime. Keep its result open while using them.</summary>
public sealed record VbaProject(VbaProjectInformation Information, int CodePageUsed, bool IsLocked,
    VbaProjectProperties Properties, IReadOnlyList<VbaReference> References, IReadOnlyList<VbaModule> Modules)
{
    public string Name => Information.Name;
    public uint SystemKind => Information.SystemKind;
    public int DeclaredCodePage => Information.DeclaredCodePage;
}
