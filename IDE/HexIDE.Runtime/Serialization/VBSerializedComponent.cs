using System.Collections.Generic;

namespace HexIDE.Runtime.Serialization;

public class VBSerializedComponent
{
    public int BeginLine { get; set; }
    public string Type { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, object> Properties { get; } = new Dictionary<string, object>();
    // Assignments in source order, including duplicates (#4096). Properties remains the final-value bag.
    public List<KeyValuePair<string, object>> PropertyAssignments { get; } = new();
    public List<VBSerializedComponent> SubComponents { get; } = new List<VBSerializedComponent>();

    // Raw lines for each property entry, in file order.
    // Scalar: one entry with a single line. Block (BeginProperty...EndProperty): one entry with all lines.
    // Used to preserve unknown properties verbatim on save.
    public List<(string Name, List<string> RawLines)> OrderedRawProperties { get; } = new();
}