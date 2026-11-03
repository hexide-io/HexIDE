using Avalonia.Controls;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

// Runtime registration only; the designer carries the block until the file-writing slice (#1095 C).
public sealed class MonthViewComponentClass : InBoxComponentClass
{
    private MonthViewComponentClass() : base([TabIndexProperty, ToolTipTextProperty], [],
        [GotFocusEvent, LostFocusEvent, MouseDownEvent, MouseUpEvent, ClickEvent, DblClickEvent, DateClickEvent, DateDblClickEvent, SelChangeEvent, GetDayBoldEvent]) { }

    public static readonly EventClass DateClickEvent = new("DateClick", new EventClassArgument("DateClicked", "Date"));
    public static readonly EventClass DateDblClickEvent = new("DateDblClick", new EventClassArgument("DateDblClicked", "Date"));
    public static readonly EventClass SelChangeEvent = new("SelChange", new EventClassArgument("StartDate", "Date"),
        new EventClassArgument("EndDate", "Date"), new EventClassArgument("Cancel", "Integer"));
    public static readonly EventClass GetDayBoldEvent = new("GetDayBold", new EventClassArgument("StartDate", "Date"),
        new EventClassArgument("Count", "Integer"), new EventClassArgument("State()", "Boolean"));

    public static MonthViewComponentClass Instance { get; } = new();
    public override string Name => "MonthView";
    public override string ControlClassName => "MonthView";
    public override RuntimeLibrary Library => ControlLibraries.MSComCtl2;
    public override (int X, int Y) DefaultExtent => (4763, 4180);
    public override (int Width, int Height) AddedSize => (2700, 2370);

    private static InBoxMember Property(string name, bool implemented = true) =>
        new(name, MemberKind.Property, MemberOrigin.Version6, false, implemented);

    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        Property("Value"), Property("MinDate"), Property("MaxDate"), Property("SelStart"), Property("SelEnd"),
        Property("MultiSelect"), Property("MaxSelCount"), Property("MonthRows"), Property("MonthColumns"),
        Property("StartOfWeek"), Property("ShowToday"), Property("ShowWeekNumbers"), Property("ScrollRate"),
        Property("Year"), Property("Month"), Property("Day"), Property("DayOfWeek"), Property("Week"),
        Property("Appearance"), Property("BorderStyle"), Property("Enabled"), Property("MousePointer"), Property("OLEDropMode"),
        Property("BackColor", false), Property("ForeColor", false), Property("MonthBackColor", false),
        Property("TitleBackColor", false), Property("TitleForeColor", false), Property("TrailingForeColor", false),
        Property("Font", false), Property("MouseIcon", false), Property("hWnd", false),
        Property("DayBold", false), Property("VisibleDays"),
        new("ComputeControlSize", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("HitTest", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, false, true),
    ];

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    internal static ComponentInstance? ReadForRuntime(string block)
    {
        var begin = block.TrimStart().Split('\n', 2)[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (begin.Length < 3 || !begin[1].Equals(Instance.VBTypeName, StringComparison.OrdinalIgnoreCase)) return null;
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n" + block + "\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n",
            new NullSink(), extraComponents: new Dictionary<string, ComponentBaseClass> { [begin[1]] = Instance });
        return definition?.Components.FirstOrDefault(c => c.BaseClass is MonthViewComponentClass);
    }

    private static int FileInteger(object value) => value is int number ? number : throw VBMonthView.Pending("saved value conversion");
    private static bool FileBoolean(object value) => FileInteger(value) switch
    {
        0 => false, -1 => true, _ => throw VBMonthView.Pending("saved Boolean conversion"),
    };
    private static double FileDate(object value) => value switch
    {
        int number => number, double number => number, _ => throw VBMonthView.Pending("saved Date conversion"),
    };

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var control = new VBMonthView();
        var lines = instance.UnknownRawProperties.SelectMany(p => p.Lines);
        var (bag, _) = new VbFrmFormatDeserializer().Deserialize(
            "VERSION 5.00\r\nBegin MSComCtl2.MonthView MonthView1\r\n" + string.Join("\r\n", lines) + "\r\nEnd\r\n");
        // Bounds must be installed before CurrentDate, independent of the file's line order.
        foreach (var name in new[] { "MaxDate", "MinDate" })
            if (bag.Properties.TryGetValue(name, out var bound))
                control.TrySetProperty(name, new Vb6Value(new VbDate(FileDate(bound))));
        foreach (var (name, value) in bag.Properties)
        {
            switch (name.ToLowerInvariant())
            {
                case "_extentx": case "_extenty": case "_version": case "mindate": case "maxdate": break;
                case "currentdate":
                    control.TrySetProperty("Value", new Vb6Value(new VbDate(FileDate(value)))); break;
                case "multiselect": case "showtoday": case "showweeknumbers":
                    control.TrySetProperty(name, new Vb6Value(FileBoolean(value))); break;
                case "startofweek":
                    control.TrySetProperty(name, new Vb6Value(FileInteger(value) & 0xffff)); break;
                default:
                    if (Instance.Member(name) is { Implemented: false }) throw VBMonthView.Pending(name);
                    if (!control.TrySetProperty(name, new Vb6Value(FileInteger(value))))
                        throw VBMonthView.Pending(name);
                    break;
            }
        }
        return control;
    }
}
