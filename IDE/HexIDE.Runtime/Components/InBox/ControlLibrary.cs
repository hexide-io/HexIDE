using System.Text.Json;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>
/// A library of the ActiveX controls VB6 shipped in the box (#1082): MSCOMCTL.OCX's <c>MSComctlLib</c>, and the 5.0
/// library before it, COMCTL32.OCX's <c>ComctlLib</c>. Its identity is its type library's, as a project's <c>Object=</c>
/// line names it, and it lists every control the type library declares, HexIDE's or not, so a control it doesn't
/// implement yet can be named with the issue that tracks it.
/// </summary>
/// <remarks>
/// The GUIDs, versions, control lists and constants were read from the type libraries themselves on the oracle VM
/// (the oracle's "In-box controls: the ProgressBar"): a coclass flagged as a control is listed here, and an object a
/// control hands out (a <c>ListItem</c>, a <c>Panel</c>) is not. Nothing is hosted through COM: each control HexIDE has is
/// its own implementation, registered under each library's type name.
/// </remarks>
public sealed class ControlLibrary
{
    private readonly string constantsResource;
    private readonly Lazy<Constants> constants;

    internal ControlLibrary(string name, string guid, string version, string file, int persistedVersion,
        IReadOnlyDictionary<string, int> controls, string constantsResource)
    {
        Name = name;
        Guid = guid;
        Version = version;
        File = file;
        PersistedVersion = persistedVersion;
        Controls = controls;
        this.constantsResource = constantsResource;
        constants = new Lazy<Constants>(Load, isThreadSafe: true);
    }

    /// <summary>The library's name, which qualifies its controls' type names and its constants: <c>MSComctlLib</c>.</summary>
    public string Name { get; }

    /// <summary>Its type library's GUID, with braces, as an <c>Object=</c> line writes it.</summary>
    public string Guid { get; }

    /// <summary>Its type library's version, as an <c>Object=</c> line writes it: 2.0.</summary>
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
        var asm = typeof(ControlLibrary).Assembly;
        var resource = asm.GetManifestResourceNames().Single(n => n.EndsWith(constantsResource, StringComparison.Ordinal));
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
    public static readonly ControlLibrary MSComctlLib = new("MSComctlLib", "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}", "2.0",
        "MSCOMCTL.OCX", 393216,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TabStrip"] = 1089, ["Toolbar"] = 1088, ["StatusBar"] = 1087, ["ProgressBar"] = 1082, ["TreeView"] = 1086,
            ["ListView"] = 1085, ["ImageList"] = 1083, ["Slider"] = 1091, ["ImageCombo"] = 1092,
        },
        "mscomctl-constants.json");

    /// <summary>COMCTL32.OCX, Microsoft Windows Common Controls 5.0: the same controls but ImageCombo, which 6.0 added.</summary>
    public static readonly ControlLibrary ComctlLib = new("ComctlLib", "{6B7E6392-850A-101B-AFC0-4210102A8DA7}", "1.3",
        "COMCTL32.OCX", 327682,
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["TabStrip"] = 1089, ["Toolbar"] = 1088, ["StatusBar"] = 1087, ["ProgressBar"] = 1082, ["TreeView"] = 1086,
            ["ListView"] = 1085, ["ImageList"] = 1083, ["Slider"] = 1091,
        },
        "comctl32-constants.json");

    /// <summary>Every in-box control library HexIDE knows.</summary>
    public static IReadOnlyList<ControlLibrary> All { get; } = [MSComctlLib, ComctlLib];

    /// <summary>The library <paramref name="reference"/> names, or null for one HexIDE doesn't know.</summary>
    public static ControlLibrary? For(ObjectReference reference) => All.FirstOrDefault(l => l.IsNamedBy(reference));

    /// <summary>The library a control's type name is in, by its prefix: <c>MSComctlLib.ListView</c>'s is MSComctlLib.</summary>
    public static ControlLibrary? ForTypeName(string typeName)
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
    public static IReadOnlyList<ControlLibrary> ReferencedBy(ProjectDefinition project)
    {
        var referenced = new List<ControlLibrary>();
        foreach (var reference in project.ObjectReferences.Concat(project.Forms.SelectMany(f => f.ObjectReferences)))
            if (For(reference) is { } library && !referenced.Contains(library))
                referenced.Add(library);
        return referenced;
    }
}
