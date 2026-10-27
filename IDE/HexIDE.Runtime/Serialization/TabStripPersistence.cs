using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Serialization;

// VB6 SP6's saved blocks and load probes: oracle "In-box controls: the TabStrip" (#1089).
internal static class TabStripPersistence
{
    internal static readonly PropertyClass<VBSerializedComponent?> SavedProperty =
        new("(SavedTabs)", "The saved TabStrip property bag.", PropertyCategory.Internal);

    private static readonly (string Name, int Default, bool Boolean)[] Properties =
    [
        ("TabWidthStyle", 0, false), ("MultiRow", 0, true), ("Style", 0, false), ("ShowTips", -1, true),
        ("TabFixedWidth", 0, false), ("TabFixedHeight", 0, false), ("HotTracking", 0, true),
        ("MultiSelect", 0, true), ("Placement", 0, false), ("Separators", 0, true), ("TabMinWidth", 1000, false),
        ("TabStyle", 0, false),
    ];
    internal static bool OwnLine(string name) => Properties.Any(p => p.Name == name)
        || name is "ImageList" or "Tabs" or "MousePointer" or "Font" or "OLEDropMode";

    internal static void Validate(VBSerializedComponent component)
    {
        var pending = new Stack<VBSerializedComponent>();
        pending.Push(component);
        while (pending.TryPop(out var current))
        {
            if (current.Type.Equals("MSComctlLib.TabStrip", StringComparison.OrdinalIgnoreCase)
                && current.Properties.GetValueOrDefault("Tabs") is Dictionary<string, object> tabs
                && tabs.GetValueOrDefault("NumTabs") is int count)
                for (var i = 1; i <= Math.Min(count, tabs.Count); i++)
                    if (tabs.GetValueOrDefault($"Tab{i}") is Dictionary<string, object> tab
                        && tab.GetValueOrDefault("ImageVarType") is 8 && !tab.ContainsKey("ImageKey"))
                        throw new VBRunTimeException(VBStandardError.TypeMismatch);
            foreach (var child in current.SubComponents) pending.Push(child);
        }
    }

    internal static bool CanWrite(VBSerializedComponent saved)
    {
        foreach (var name in saved.Properties.Keys)
            if (!OwnLine(name) && name is not ("Height" or "Left" or "Top" or "Width" or "TabIndex" or "TabStop"
                or "Tag" or "Visible" or "_ExtentX" or "_ExtentY" or "_Version")) return false;
        foreach (var (name, _, boolean) in Properties)
        {
            if (!saved.Properties.TryGetValue(name, out var value)) continue;
            if (value is not int number) return false;
            if (boolean)
            {
                if (number is not (0 or -1)) return false;
                continue;
            }
            var maximum = name switch { "Placement" => 3, "TabStyle" => 1,
                "Style" or "TabWidthStyle" => 2, _ => int.MaxValue };
            if (number < 0 || number > maximum) return false;
            if ((name is "TabFixedWidth" or "TabFixedHeight") && (long)number * 1440 / 2540 > short.MaxValue) return false;
        }
        foreach (var name in new[] { "MousePointer", "OLEDropMode", "_Version" })
        {
            if (!saved.Properties.TryGetValue(name, out var value)) continue;
            if (value is not int number) return false;
            if (name == "OLEDropMode" && number is not (0 or 1)) return false;
            if (name == "MousePointer" && (number < 0 || number > 15 && number != 99)) return false;
        }
        if (saved.Properties.TryGetValue("ImageList", out var imageList)
            && (imageList is not string imageListName || imageListName.Length == 0)) return false;
        if (saved.Properties.GetValueOrDefault("Tabs") is not { } group) return true;
        if (group is not Dictionary<string, object> tabs || tabs.GetValueOrDefault("NumTabs") is not int count || count < 1)
            return false;
        if (tabs.Count != count + 1) return false;
        for (var i = 1; i <= count; i++)
        {
            if (tabs.GetValueOrDefault($"Tab{i}") is not Dictionary<string, object> tab) return false;
            foreach (var (name, value) in tab)
                if (name is "Caption" or "Key" or "Object.Tag" or "Object.ToolTipText" or "ImageKey")
                {
                    if (value is not string text) return false;
                    if ((name is "Object.Tag" or "Object.ToolTipText") && text.Length == 0) return false;
                }
                else if (name is "ImageIndex" or "ImageVarType")
                { if (value is not int) return false; }
                else if (name is not ("Selected" or "Image")) return false;
            var imageType = tab.GetValueOrDefault("ImageVarType") as int? ?? 2;
            if (imageType is not (2 or 8)
                || imageType == 2 && tab.ContainsKey("ImageKey")
                || imageType == 8 && (!tab.ContainsKey("ImageKey") || tab.ContainsKey("ImageIndex"))) return false;
        }
        return true;
    }

    internal static void Write(VbFrmFormatSerializer vb, ComponentInstance instance, CompanionCitations citations)
    {
        var saved = instance.GetPropertyOrDefault(SavedProperty);
        if (saved is not null && !CanWrite(saved))
            throw TabStripMembers.Refused("Tabs");
        var properties = saved?.Properties ?? new Dictionary<string, object>();
        foreach (var (name, defaultValue, boolean) in Properties)
            if (properties.GetValueOrDefault(name) is int value && value != defaultValue)
                vb.WriteProperty(name, boolean ? typeof(bool) : typeof(int), boolean ? value != 0 : value);
        if (properties.GetValueOrDefault("ImageList") is string imageList && imageList.Length > 0)
            vb.WriteProperty("ImageList", typeof(string), imageList);
        vb.WriteProperty("_Version", typeof(int), properties.GetValueOrDefault("_Version") ?? 393216);
        vb.BeginProperty("Tabs", "{1EFB6598-857C-11D1-B16A-00C0F0283628}");
        var tabs = properties.GetValueOrDefault("Tabs") as Dictionary<string, object>;
        var count = tabs?.GetValueOrDefault("NumTabs") as int? ?? 1;
        vb.WriteProperty("NumTabs", typeof(int), count);
        for (var i = 1; i <= count; i++)
        {
            var tab = tabs?.GetValueOrDefault($"Tab{i}") as Dictionary<string, object>;
            vb.BeginProperty($"Tab{i}", "{1EFB659A-857C-11D1-B16A-00C0F0283628}");
            foreach (var name in new[] { "Caption", "Key", "Object.Tag", "Object.ToolTipText" })
                if (tab?.GetValueOrDefault(name) is string text && text.Length > 0)
                    vb.WriteProperty(name == "Object.Tag" ? "Object.Tag             " : name == "Object.ToolTipText" ? "Object.ToolTipText     " : name,
                        typeof(string), text);
            var imageType = tab?.GetValueOrDefault("ImageVarType") as int? ?? 2;
            vb.WriteProperty("ImageVarType", typeof(int), imageType);
            if (imageType == 8 && tab?.GetValueOrDefault("ImageKey") is string key)
                vb.WriteProperty("ImageKey", typeof(string), key);
            else if (imageType == 2 && tab?.GetValueOrDefault("ImageIndex") is int index && index != 0)
                vb.WriteProperty("ImageIndex", typeof(int), index);
            vb.EndProperty();
        }
        vb.EndProperty();
        if (properties.GetValueOrDefault("MousePointer") is int pointer && pointer != 0)
            vb.WriteProperty("MousePointer", typeof(int), pointer);
        if (instance.UnknownRawProperties.FirstOrDefault(p => p.Name == "Font") is { Name: "Font" } font)
            vb.WriteVerbatimProperty(font.Name, font.Lines.Select(line => citations.Cite(line, font.Records)).ToList());
        if (properties.GetValueOrDefault("OLEDropMode") is int drop && drop != 0)
            vb.WriteProperty("OLEDropMode", typeof(int), drop);
    }

    internal static void Load(VBTabStrip strip, VBSerializedComponent saved)
    {
        if (!CanWrite(saved))
            throw TabStripMembers.Refused("Tabs");
        foreach (var (name, _, boolean) in Properties)
            if (saved.Properties.GetValueOrDefault(name) is int value)
                strip.Values[name] = boolean ? new Vb6Value(value != 0)
                    : name is "TabFixedWidth" or "TabFixedHeight" ? new Vb6Value((int)(value * 1440.0 / 2540))
                    : name == "TabMinWidth" ? new Vb6Value((float)(value * 1440.0 / 2540)) : new Vb6Value((long)value);
        if ((long)strip.Values["Placement"].Value! == 2) strip.Values["MultiRow"] = new Vb6Value(true);
        foreach (var name in new[] { "MousePointer", "OLEDropMode" })
            if (saved.Properties.GetValueOrDefault(name) is int value) strip.Values[name] = new Vb6Value((long)value);
        if (saved.Properties.GetValueOrDefault("Tabs") is Dictionary<string, object> tabs)
        {
            strip.Model.Clear();
            var count = tabs.GetValueOrDefault("NumTabs") as int? ?? 1;
            for (var i = 1; i <= count; i++)
            {
                var tab = tabs.GetValueOrDefault($"Tab{i}") as Dictionary<string, object> ?? [];
                if (tab.GetValueOrDefault("ImageVarType") is 8 || tab.GetValueOrDefault("ImageIndex") is int image && image != 0)
                    throw TabStripMembers.Refused("Tab.Image");
                var added = strip.Model.Add(caption: tab.GetValueOrDefault("Caption") as string ?? "", key: tab.GetValueOrDefault("Key") as string ?? "");
                var wrapper = strip.Tabs.Wrap(added);
                if (tab.GetValueOrDefault("Object.Tag") is string tag) wrapper.TrySetProperty("Tag", new Vb6Value(tag));
                if (tab.GetValueOrDefault("Object.ToolTipText") is string tip) wrapper.TrySetProperty("ToolTipText", new Vb6Value(tip));
            }
        }
    }
}
