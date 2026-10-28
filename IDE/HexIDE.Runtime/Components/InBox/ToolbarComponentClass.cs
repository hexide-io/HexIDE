using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>The MSComctlLib Toolbar's runtime model, drawing, events and property-bag loading (#1088).</summary>
public sealed class ToolbarComponentClass : InBoxComponentClass
{
    private ToolbarComponentClass() : base([TabIndexProperty, ToolTipTextProperty], [], [ClickEvent, MouseDownEvent, MouseUpEvent, ButtonClickEvent, ButtonDropDownEvent, ButtonMenuClickEvent]) { }

    public static readonly EventClass ButtonClickEvent = new("ButtonClick", new EventClassArgument("Button", "MSComctlLib.Button"));
    public static readonly EventClass ButtonDropDownEvent = new("ButtonDropDown", new EventClassArgument("Button", "MSComctlLib.Button"));
    public static readonly EventClass ButtonMenuClickEvent = new("ButtonMenuClick", new EventClassArgument("ButtonMenu", "MSComctlLib.ButtonMenu"));

    public static ToolbarComponentClass Instance { get; } = new();
    public override string Name => "Toolbar";
    public override string ControlClassName => "Toolbar";
    public override (int X, int Y) DefaultExtent => (10583, 1164);
    public override (int Width, int Height) AddedSize => (6000, 660);
    public override IEnumerable<RuntimeLibrary> Libraries => [ControlLibraries.MSComctlLib];

    private static InBoxMember Property(string name, bool implemented = true) =>
        new(name, MemberKind.Property, MemberOrigin.Version6, IsFrmProperty: false, Implemented: implemented);

    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        Property("Appearance"), Property("AllowCustomize"), Property("BorderStyle"), Property("ButtonHeight"),
        Property("ButtonWidth"), Property("Enabled"), Property("ShowTips"), Property("Style"), Property("TextAlignment"),
        Property("Wrappable"), Property("HelpContextID"), Property("HelpFile"), Property("MousePointer"), Property("OLEDropMode"),
        Property("Buttons"), Property("Controls", false), Property("ImageList"),
        Property("DisabledImageList"), Property("HotImageList"), Property("MouseIcon", false), Property("hWnd", false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("Customize", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("SaveToolbar", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("RestoreToolbar", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
    ];

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    internal static ComponentInstance? ReadForRuntime(string block)
    {
        var begin = block.TrimStart().Split('\n', 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (begin.Length < 3 || !begin[1].Equals(Instance.VBTypeName, StringComparison.OrdinalIgnoreCase))
            return null;
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n" + block + "\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n",
            new NullSink(), extraComponents: new Dictionary<string, ComponentBaseClass> { [begin[1]] = Instance });
        return definition?.Components.FirstOrDefault(c => c.BaseClass is ToolbarComponentClass);
    }

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var toolbar = new VBToolbar { Focusable = false };
        var lines = instance.UnknownRawProperties.SelectMany(p => p.Lines);
        var (bag, _) = new VbFrmFormatDeserializer().Deserialize(
            "VERSION 5.00\r\nBegin MSComctlLib.Toolbar Toolbar1\r\n" + string.Join("\r\n", lines) + "\r\nEnd\r\n");
        foreach (var (name, value) in bag.Properties)
        {
            switch (name.ToLowerInvariant())
            {
                case "align":
                    var align = ToolbarItems.FileInteger(value);
                    if (align is not (1 or 2)) throw ToolbarItems.Unmeasured();
                    toolbar.LoadedAlign = align;
                    break;
                case "buttons": toolbar.Buttons.Load(value as Dictionary<string, object> ?? throw ToolbarItems.Unmeasured()); break;
                case "tooltips": toolbar.TrySetProperty("ShowTips", new Vb6Value(ToolbarItems.FileBoolean(value))); break;
                case "allowcustomize": case "wrappable": case "enabled":
                    toolbar.TrySetProperty(name, new Vb6Value(ToolbarItems.FileBoolean(value))); break;
                case "appearance": case "borderstyle": case "style": case "textalignment":
                case "helpcontextid": case "mousepointer": case "oledropmode":
                    toolbar.TrySetProperty(name, new Vb6Value((long)ToolbarItems.FileInteger(value))); break;
                case "helpfile": toolbar.TrySetProperty(name, new Vb6Value(value is string text ? text : throw ToolbarItems.Unmeasured())); break;
                // Resolve the saved name after every form control has been created (#2334).
                case "imagelist":
                    toolbar.SavedImageListName = value is string listName ? listName : throw ToolbarItems.Unmeasured();
                    break;
                // Saved sizes are HIMETRIC, not runtime assignments. Contents determine runtime geometry.
                case "buttonwidth": case "buttonheight": break;
                default: VBToolbar.RefusePending(name); break;
            }
        }
        toolbar.RefreshButtons();
        return toolbar;
    }
}
