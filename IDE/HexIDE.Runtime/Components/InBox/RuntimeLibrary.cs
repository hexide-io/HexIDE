using System.Text.Json;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// A library HexIDE implements, identified by a project's Object= or Reference= line. Control libraries carry their
/// control catalogue and constants; class libraries supply managed factories, GlobalMultiUse members and enums.
/// </summary>
/// <remarks>
/// The in-box control libraries' GUIDs, versions, control lists and constants were read from their type libraries on
/// the oracle VM (the oracle's "In-box controls: the ProgressBar"). Nothing is hosted through COM.
/// </remarks>
public sealed class RuntimeLibrary
{
    private readonly string? constantsResource;
    private readonly Lazy<Constants> constants;

    internal RuntimeLibrary(string name, string description, string guid, string version, string file, int persistedVersion,
        IReadOnlyDictionary<string, int> controls, string? constantsResource)
    {
        Name = name;
        Description = description;
        Guid = guid;
        Version = version;
        File = file;
        PersistedVersion = persistedVersion;
        Controls = controls;
        this.constantsResource = constantsResource;
        constants = new Lazy<Constants>(Load, isThreadSafe: true);
    }

    public RuntimeLibrary(string name, string guid, string version,
        IReadOnlyList<LibraryClass> classes, IReadOnlyDictionary<string, IReadOnlyDictionary<string, long>> enums)
    {
        Name = name;
        Description = name;
        Guid = guid;
        Version = version;
        File = "";
        Controls = new Dictionary<string, int>();
        Classes = classes;
        constants = new Lazy<Constants>(() =>
        {
            var bare = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
            var containers = new Dictionary<string, Dictionary<string, Vb6Value>>(StringComparer.OrdinalIgnoreCase);
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (container, entries) in enums)
            {
                var members = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
                foreach (var (member, value) in entries)
                    bare.TryAdd(member, members[member] = new Vb6Value(value));
                containers[container] = members;
                names[container] = container;
            }
            return new Constants(bare, containers, names);
        });
    }

    public IReadOnlyList<LibraryClass> Classes { get; } = [];

    public bool IsNamedBy(VbReference reference) =>
        Guid.Equals(reference.Guid, StringComparison.OrdinalIgnoreCase) && Version == reference.Version;

    /// <summary>
    /// What VB6's Components dialog lists the library as: its type library's registry description, which the dialog shows
    /// as it is (measured, #1103). The service pack is part of it: "Microsoft Windows Common Controls 6.0 (SP6)".
    /// </summary>
    public string Description { get; }

    /// <summary>The <c>.vbp</c> line that references the library, as VB6's Save Project writes it (measured, #1103).</summary>
    public string ProjectLine => $"Object={Guid}#{Version}#0; {File}";

    /// <summary>The form header line that declares the library, as VB6's designer writes it when one is placed (#1082).</summary>
    public string FormLine => $"Object = \"{Guid}#{Version}#0\"; \"{File}\"";

    /// <summary>The library's name, which qualifies its controls' type names and its constants: <c>MSComctlLib</c>.</summary>
    public string Name { get; }

    /// <summary>Its type library's GUID, with braces.</summary>
    public string Guid { get; }

    /// <summary>Its type library's version: 2.0.</summary>
    public string Version { get; }

    /// <summary>The control file: MSCOMCTL.OCX.</summary>
    public string File { get; }

    /// <summary>
    /// The <c>_Version</c> each of its controls writes in a <c>.frm</c>: 393216 (&amp;H60000) for the 6.0 library and 327682
    /// (&amp;H50002) for 5.0 (measured: VB6's save writes it where the file had none).
    /// </summary>
    public int PersistedVersion { get; }

    /// <summary>Every control the library declares, by class name, with the issue that implements it.</summary>
    public IReadOnlyDictionary<string, int> Controls { get; }

    /// <summary>A control's type name in this library: <c>MSComctlLib.ProgressBar</c>.</summary>
    public string TypeName(string className) => $"{Name}.{className}";

    /// <summary>Whether <paramref name="reference"/> names this library, whatever version it asks for.</summary>
    public bool IsNamedBy(ObjectReference reference) => reference.Names(Guid);

    /// <summary>A constant of this library by its bare name: <c>ccScrollingSmooth</c>.</summary>
    internal bool TryBare(string name, out Vb6Value value) => constants.Value.Bare.TryGetValue(name, out value);

    /// <summary>Whether <paramref name="name"/> is one of this library's enums: <c>ScrollingConstants</c>.</summary>
    internal bool IsContainer(string name) => constants.Value.Containers.ContainsKey(name);

    /// <summary>A member of one of this library's enums, which is strict: a member of another enum is not found.</summary>
    internal bool TryInContainer(string container, string member, out Vb6Value value)
    {
        value = default;
        return constants.Value.Containers.TryGetValue(container, out var members) && members.TryGetValue(member, out value);
    }

    /// <summary>The enum's name as the library spells it, for <paramref name="name"/> in any case.</summary>
    internal string? ContainerName(string name) =>
        constants.Value.Containers.TryGetValue(name, out _) ? constants.Value.ContainerNames[name] : null;

    private Constants Load()
    {
        if (constantsResource is null) return new Constants(new(StringComparer.OrdinalIgnoreCase),
            new(StringComparer.OrdinalIgnoreCase), new(StringComparer.OrdinalIgnoreCase));
        var asm = typeof(RuntimeLibrary).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith(constantsResource!, StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(resource)!;
        using var doc = JsonDocument.Parse(stream);
        var bare = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
        var containers = new Dictionary<string, Dictionary<string, Vb6Value>>(StringComparer.OrdinalIgnoreCase);
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var container in doc.RootElement.GetProperty("containers").EnumerateObject())
        {
            var members = new Dictionary<string, Vb6Value>(StringComparer.OrdinalIgnoreCase);
            foreach (var member in container.Value.GetProperty("members").EnumerateObject())
            {
                // Every one is a Long, as the in-box libraries' constants are (measured: TypeName(ccScrollingSmooth)).
                var value = new Vb6Value((long)member.Value.GetInt64());
                members[member.Name] = value;
                bare.TryAdd(member.Name, value);
            }
            containers[container.Name] = members;
            names[container.Name] = container.Name;
        }
        return new Constants(bare, containers, names);
    }

    private sealed record Constants(
        Dictionary<string, Vb6Value> Bare,
        Dictionary<string, Dictionary<string, Vb6Value>> Containers,
        Dictionary<string, string> ContainerNames);
}

/// <summary>The in-box control libraries HexIDE knows (#1082), and which of them a project references.</summary>
public static class ControlLibraries
{
    /// <summary>MSCOMCTL.OCX, Microsoft Windows Common Controls 6.0.</summary>
    public static readonly RuntimeLibrary MSComctlLib = new("MSComctlLib", "Microsoft Windows Common Controls 6.0 (SP6)",
        "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}", "2.0",
        "MSCOMCTL.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TabStrip"] = 1089, ["Toolbar"] = 1088, ["StatusBar"] = 1087, ["ProgressBar"] = 1082, ["TreeView"] = 1086,
            ["ListView"] = 1085, ["ImageList"] = 1083, ["Slider"] = 1091, ["ImageCombo"] = 1092,
        },
        "mscomctl-constants.json");

    /// <summary>COMCTL32.OCX, Microsoft Windows Common Controls 5.0: the same controls but ImageCombo, which 6.0 added.</summary>
    public static readonly RuntimeLibrary ComctlLib = new("ComctlLib", "Microsoft Windows Common Controls 5.0 (SP2)",
        "{6B7E6392-850A-101B-AFC0-4210102A8DA7}", "1.3",
        "COMCTL32.OCX", 327682,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TabStrip"] = 1089, ["Toolbar"] = 1088, ["StatusBar"] = 1087, ["ProgressBar"] = 1082, ["TreeView"] = 1086,
            ["ListView"] = 1085, ["ImageList"] = 1083, ["Slider"] = 1091,
        },
        "comctl32-constants.json");

    public static readonly RuntimeLibrary TabDlg = new("TabDlg", "TabDlg",
        "{BDC217C8-ED16-11CD-956C-0000C04E4C0A}", "1.1", "TABCTL32.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["SSTab"] = 1090 }, "tabdlg-constants.json");

    /// <summary>COMDLG32.OCX, Microsoft Common Dialog Control 6.0.</summary>
    public static readonly RuntimeLibrary MSComDlg = new("MSComDlg", "Microsoft Common Dialog Control 6.0",
        "{F9043C88-F6F2-101A-A3C9-08002B2F49FB}", "1.2", "COMDLG32.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["CommonDialog"] = 1084 },
        "comdlg32-constants.json");

    /// <summary>MSCOMCT2.OCX, Microsoft Windows Common Controls-2 6.0 (SP6), from VB6's UpDown save (#1093).</summary>
    public static readonly RuntimeLibrary MSComCtl2 = new("MSComCtl2", "Microsoft Windows Common Controls-2 6.0 (SP6)",
        "{86CF1D34-0C5F-11D2-A9FC-0000F8754DA1}", "2.0",
        "MSCOMCT2.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["UpDown"] = 1093, ["DTPicker"] = 1081, ["MonthView"] = 1081, ["Animation"] = 1081, ["FlatScrollBar"] = 1081,
        }, null);

    /// <summary>MSMASK32.OCX: identity and constants measured in the MaskedEdBox oracle (#1098).</summary>
    public static readonly RuntimeLibrary MSMask = new("MSMask", "MSMask",
        "{C932BA88-4374-101B-A56C-00AA003668DC}", "1.1", "MSMASK32.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["MaskEdBox"] = 1098 },
        "msmask32-constants.json");

    // Its registry description is unmeasured; the dialog uses the recorded library name.
    public static readonly RuntimeLibrary RichTextLib = new("RichTextLib", "RichTextLib",
        "{3B7C8863-D78F-101B-B9B5-04021C009402}", "1.2", "RICHTX32.OCX", 393217,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["RichTextBox"] = 1100 }, null);

    /// <summary>Every in-box control library HexIDE knows.</summary>
    public static IReadOnlyList<RuntimeLibrary> All { get; } = [MSComctlLib, ComctlLib, TabDlg, MSComDlg, MSComCtl2, MSMask, RichTextLib];

    /// <summary>The library <paramref name="reference"/> names, or null for one HexIDE doesn't know.</summary>
    public static RuntimeLibrary? For(ObjectReference reference) => All.FirstOrDefault(l => l.IsNamedBy(reference));

    /// <summary>The library a control's type name is in, by its prefix: <c>MSComctlLib.ListView</c>'s is MSComctlLib.</summary>
    public static RuntimeLibrary? ForTypeName(string typeName)
    {
        var dot = typeName.IndexOf('.');
        return dot < 0 ? null : All.FirstOrDefault(l => typeName.AsSpan(0, dot).Equals(l.Name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The libraries <paramref name="project"/> references: its <c>.vbp</c>'s <c>Object=</c> lines, then any a form's
    /// header names that the project doesn't, each once, in that order.
    /// </summary>
    /// <remarks>VB6 writes both lines for a form that holds the control, and a form copied in from another project may
    /// bring its header line alone; the CommonDialog's reference is read the same way (#1084).</remarks>
    public static IReadOnlyList<RuntimeLibrary> ReferencedBy(ProjectDefinition project)
    {
        var referenced = new List<RuntimeLibrary>();
        foreach (var reference in project.ObjectReferences.Concat(project.Forms.SelectMany(f => f.ObjectReferences)))
            if (For(reference) is { } library && !referenced.Contains(library))
                referenced.Add(library);
        return referenced;
    }
}

/// <summary>A managed class supplied by a library. Members lists the names it exposes without an instance.</summary>
public sealed record LibraryClass(string Name, Func<ICSharpProxy> Factory,
    IReadOnlyList<string> Members, bool GlobalMultiUse = false);

public static class RuntimeLibraries
{
    public static IReadOnlyList<RuntimeLibrary> All { get; } = ControlLibraries.All;

    public static IReadOnlyList<RuntimeLibrary> ReferencedBy(ProjectDefinition project,
        IReadOnlyList<RuntimeLibrary>? known = null)
    {
        var result = new List<RuntimeLibrary>();
        foreach (var reference in project.References)
            foreach (var library in known ?? All)
                if (library.IsNamedBy(reference) && !result.Contains(library))
                    result.Add(library);
        foreach (var library in ControlLibraries.ReferencedBy(project))
            if (!result.Contains(library)) result.Add(library);
        return result;
    }
}
