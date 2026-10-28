using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Serialization;

// Own properties stay carried and unavailable for designer editing until their value writers are modelled.
internal static class TreeViewPersistence
{
    private static readonly (string Name, object? Default)[] Own =
    [
        ("HideSelection", -1), ("Indentation", 1000), ("LabelEdit", 0), ("LineStyle", 0),
        ("PathSeparator", "\\"), ("Sorted", 0), ("Style", 0), ("Checkboxes", 0),
        ("FullRowSelect", 0), ("HotTracking", 0), ("Scroll", -1), ("SingleSel", 0),
        ("ImageList", ""), ("BorderStyle", 0), ("Appearance", null), ("MousePointer", 0),
        ("Enabled", -1), ("Font", null), ("OLEDragMode", 0), ("OLEDropMode", 0),
    ];

    internal static bool IsOwn(string name) => name.Equals("_ExtentX", StringComparison.OrdinalIgnoreCase)
        || name.Equals("_ExtentY", StringComparison.OrdinalIgnoreCase)
        || name.Equals("_Version", StringComparison.OrdinalIgnoreCase)
        || Own.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    internal static void Write(VbFrmFormatSerializer vb, ComponentInstance instance, CompanionCitations citations)
    {
        var raw = instance.UnknownRawProperties.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var (parsed, _) = new VbFrmFormatDeserializer().Deserialize(
            "Begin MSComctlLib.TreeView TreeView1\r\n" + string.Join("\r\n", instance.UnknownRawPropertyLines) + "\r\nEnd\r\n");
        void Carried(string name)
        {
            var property = raw[name];
            vb.WriteVerbatimProperty(name, property.Lines.Select(line => citations.Cite(line, property.Records)).ToList());
        }
        void Extent(string name, PropertyClass<double> axis, double? basis)
        {
            var pixels = instance.GetPropertyOrDefault(axis);
            if (basis == pixels)
            {
                // Missing extents on a loaded TreeView have not been measured; keep their absence too.
                if (raw.ContainsKey(name)) Carried(name);
                return;
            }
            vb.WriteProperty(name, typeof(int), InBoxComponentClass.Extent((int)Math.Round(pixels)));
        }
        var basis = instance.GetPropertyOrDefault(InBoxComponentClass.ExtentBasisProperty);
        Extent("_ExtentX", VBProperties.WidthProperty, basis?.Width);
        Extent("_ExtentY", VBProperties.HeightProperty, basis?.Height);
        if (raw.ContainsKey("_Version")) Carried("_Version");
        else vb.WriteProperty("_Version", typeof(int), 393217);
        foreach (var (name, defaultValue) in Own)
        {
            if (raw.ContainsKey(name))
            {
                if (defaultValue is null || !parsed.Properties.TryGetValue(name, out var value) || !Equals(value, defaultValue))
                    Carried(name);
            }
            else if (name == "Appearance") vb.WriteProperty(name, typeof(int), 1);
            else if (name == "Style" && instance.GetPropertyOrDefault(InBoxComponentClass.LoadedTypeNameProperty) is null)
                vb.WriteProperty(name, typeof(int), 7);
        }
    }
}
