using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

public sealed class SliderComponentClass : InBoxComponentClass
{
    private SliderComponentClass() : base([TabIndexProperty, TabStopProperty, ToolTipTextProperty], [],
        [ChangeEvent, ScrollEvent, ClickEvent, KeyDownEvent, KeyUpEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent]) { }

    public static readonly EventClass ChangeEvent = new("Change");
    public static readonly EventClass ScrollEvent = new("Scroll");
    public static SliderComponentClass Instance { get; } = new();
    public override string Name => "Slider";
    public override string ControlClassName => "Slider";
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];
    public override (int Width, int Height) AddedSize => (1500, 675);
    // Carrying loaded lines does not yet make designer property edits saveable (#1091).
    public override bool CanSaveFaithfully => false;
    public override (int X, int Y) DefaultExtent => throw new NotImplementedException("Slider missing extents are not measured (#1091).");

    // Own properties remain read-only in the designer until property editing is modelled.
    public override IReadOnlyList<InBoxMember> Members { get; } =
        OwnMembers("Min", "Max", "Value", "SmallChange", "LargeChange", "TickStyle", "TickFrequency", "GetNumTicks",
            "Orientation", "SelectRange", "SelStart", "SelLength", "Text", "TextPosition", "BorderStyle", "Enabled",
            "MousePointer", "OLEDropMode")
        .Concat(new[] { "ClearSel", "Refresh" }.Select(n => new InBoxMember(n, MemberKind.Method, MemberOrigin.Version6, false, true)))
        .Concat(new[] { "hWnd", "MouseIcon" }.Select(n => new InBoxMember(n, MemberKind.Property, MemberOrigin.Version6, false, false)))
        .Append(new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false))
        .Concat(new[] { "Change", "Scroll", "Click", "KeyDown", "KeyPress", "KeyUp", "MouseDown", "MouseMove", "MouseUp",
            "OLEStartDrag", "OLEGiveFeedback", "OLESetData", "OLECompleteDrag", "OLEDragOver", "OLEDragDrop" }
            .Select(n => new InBoxMember(n, MemberKind.Event, MemberOrigin.Version6, false, n is "Change" or "Scroll" or "Click" or "KeyDown" or "KeyUp" or "MouseDown" or "MouseMove" or "MouseUp"))).ToArray();

    private static IEnumerable<InBoxMember> OwnMembers(params string[] names) =>
        names.Select(n => new InBoxMember(n, MemberKind.Property, MemberOrigin.Version6, n is not ("Text" or "GetNumTicks"), true));

    internal static readonly string[] FileProperties =
        ["BorderStyle", "MousePointer", "Enabled", "OLEDropMode", "LargeChange", "SmallChange", "Min", "Max",
         "SelectRange", "SelStart", "SelLength", "TickStyle", "TickFrequency", "Orientation", "Value", "TextPosition"];

    private static long FileDefault(string name) => name switch
    {
        "Max" => 10, "SmallChange" or "TickFrequency" => 1, "LargeChange" => 5, "Enabled" => -1, _ => 0,
    };

    private Dictionary<string, long> ReadFileProperties(ComponentInstance instance)
    {
        if (LibraryOf(instance) != ControlLibraries.MSComctlLib)
            throw new NotImplementedException("ComctlLib Slider behaviour is not implemented (#1091): unmeasured.");
        var values = FileProperties.ToDictionary(n => n, FileDefault, StringComparer.OrdinalIgnoreCase);
        foreach (var raw in instance.UnknownRawProperties)
        {
            if (raw.Name.Equals("Text", StringComparison.OrdinalIgnoreCase) || raw.Name is "_ExtentX" or "_ExtentY" or "_Version")
                continue;
            if (!values.ContainsKey(raw.Name) || raw.Lines.Count != 1)
                throw new NotImplementedException($"Slider file property {raw.Name} is not implemented (#1091).");
            var text = raw.Lines[0];
            var value = text[(text.IndexOf('=') + 1)..].Split('\'')[0].Trim();
            if (!long.TryParse(value, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                throw new NotImplementedException($"Slider file property {raw.Name} is not implemented (#1091): unmeasured representation.");
            var measured = n is >= int.MinValue and <= int.MaxValue && (raw.Name.ToUpperInvariant() switch
            {
                "ENABLED" or "SELECTRANGE" => n is 0 or -1,
                "TICKSTYLE" => n is >= 0 and <= 3 or 7,
                "ORIENTATION" or "TEXTPOSITION" or "BORDERSTYLE" or "OLEDROPMODE" => n is 0 or 1,
                "SMALLCHANGE" or "LARGECHANGE" or "TICKFREQUENCY" => n >= 0,
                "MOUSEPOINTER" => n is >= 0 and <= 4 or 99,
                _ => true,
            });
            if (!measured)
                throw new NotImplementedException($"Slider file property {raw.Name} value is not implemented (#1091): unmeasured.");
            values[raw.Name] = n;
        }
        // File ranges bypass code validation; selection is loaded only after the range and Value.
        values["Value"] = System.Math.Max(values["Min"], System.Math.Min(values["Max"], values["Value"]));
        if (values["SelectRange"] == 0)
        {
            values["SelLength"] = 0;
        }
        else
        {
            if (values["SelStart"] < 0 || values["SelStart"] < values["Min"] || values["SelStart"] > values["Max"] || values["SelLength"] < 0)
                throw new NotImplementedException("Slider file selection is not implemented (#1091): unmeasured.");
            values["SelLength"] = System.Math.Min(values["SelLength"], values["Max"] - values["SelStart"]);
        }
        if (values["TickStyle"] == 7) values["TickStyle"] = 0;
        return values;
    }

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var values = ReadFileProperties(instance);
        // Orientation from a file preserves Width and Height.
        var slider = new VBSlider
        {
            Min = values["Min"], Max = values["Max"], Value = values["Value"],
            SmallChange = values["SmallChange"], LargeChange = values["LargeChange"],
            TickFrequency = values["TickFrequency"], TickStyle = values["TickStyle"],
            Orientation = values["Orientation"], TextPosition = values["TextPosition"],
            BorderStyle = values["BorderStyle"], OLEDropMode = values["OLEDropMode"],
            IsEnabled = values["Enabled"] != 0, SelectRange = values["SelectRange"] != 0,
            SelectionStart = values["SelectRange"] != 0 ? values["SelStart"] : 0, SelectionLength = values["SelLength"],
        };
        AttachedProperties.SetMousePointer(slider, (BuiltinTypes.VBCursorType)values["MousePointer"]);
        return slider;
    }

    public override Control InstantiateForDesigner(ComponentInstance instance) => Instantiate(instance);
}
