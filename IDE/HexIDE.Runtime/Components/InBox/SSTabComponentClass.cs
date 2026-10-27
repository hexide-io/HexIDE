using System.Diagnostics.CodeAnalysis;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components.InBox;

public sealed class SSTabComponentClass : InBoxComponentClass
{
    private SSTabComponentClass() : base(
        [EnabledProperty, TabIndexProperty, TabStopProperty, ToolTipTextProperty], [],
        [new EventClass("Click", new EventClassArgument("PreviousTab", "Integer")),
            MouseDownEvent, MouseUpEvent, MouseMoveEvent, DblClickEvent, GotFocusEvent, LostFocusEvent]) { }

    public static SSTabComponentClass Instance { get; } = new();
    public override string Name => "SSTab";
    public override string ControlClassName => "SSTab";
    public override RuntimeLibrary Library => ControlLibraries.TabDlg;
    public override (int Width, int Height) AddedSize => (1845, 750);
    public override (int X, int Y) DefaultExtent =>
        throw new NotImplementedException("SSTab extents without a saved basis are not measured");
    public override IReadOnlyList<InBoxMember> Members { get; } =
        new[] { "Tab", "Tabs", "TabsPerRow", "Rows", "Caption", "TabCaption", "TabEnabled", "TabVisible",
                "Style", "TabOrientation", "TabHeight", "TabMaxWidth", "WordWrap", "ShowFocusRect", "Enabled" }
            .Select(n => new InBoxMember(n, MemberKind.Property, MemberOrigin.Version6, false, true))
            .Concat(new[] { "Picture", "TabPicture", "BackColor", "ForeColor", "Font", "hWnd", "MousePointer", "MouseIcon", "OLEDropMode" }
                .Select(n => new InBoxMember(n, MemberKind.Property, MemberOrigin.Version6, false, false)))
            .Concat(new[] { "OLEDrag", "AboutBox" }
                .Select(n => new InBoxMember(n, MemberKind.Method, MemberOrigin.Version6, false, false)))
            .Concat(new[] { "Click", "MouseDown", "MouseUp", "MouseMove", "DblClick", "GotFocus", "LostFocus" }
                .Select(n => new InBoxMember(n, MemberKind.Event, MemberOrigin.Version6, false, true))).ToArray();

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var tab = new VBSSTab(CreateChildHost());
        instance.GetPropertyOrDefault(SSTabSavedState.Property)?.Apply(tab);
        return tab;
    }

    public override Control InstantiateForDesigner(ComponentInstance instance) => new VBSSTab(CreateChildHost());

    public override bool TryGetChildHost(Control control, [NotNullWhen(true)] out Canvas? host)
    {
        host = (control as VBSSTab)?.ChildHost;
        return host is not null;
    }
}
