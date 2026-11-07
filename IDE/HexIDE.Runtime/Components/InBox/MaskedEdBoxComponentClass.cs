using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>MSMask's MaskEdBox (#1098), with its code-assigned text model.</summary>
public sealed class MaskedEdBoxComponentClass : InBoxComponentClass
{
    public static readonly EventClass ValidationErrorEvent = new("ValidationError",
        new EventClassArgument("InvalidText", "String"), new EventClassArgument("StartPosition", "Long"));

    private MaskedEdBoxComponentClass() : base([], [], [TextBoxComponentClass.ChangeEvent, ValidationErrorEvent, KeyDownEvent, KeyPressEvent, KeyUpEvent]) { }

    public static MaskedEdBoxComponentClass Instance { get; } = new();
    public override string Name => "MaskEdBox";
    public override string ControlClassName => "MaskEdBox";
    public override RuntimeLibrary Library => ControlLibraries.MSMask;
    public override (int X, int Y) DefaultExtent => (5292, 1984);

    public override IReadOnlyList<InBoxMember> Members { get; } =
    [
        .. new[] { "ClipMode", "PromptInclude", "AllowPrompt", "AutoTab", "MaxLength", "Mask", "PromptChar" }
            .Select(name => new InBoxMember(name, MemberKind.Property, MemberOrigin.Version6, true, true)),
        .. new[] { "Text", "ClipText", "FormattedText", "SelStart", "SelLength", "SelText" }
            .Select(name => new InBoxMember(name, MemberKind.Property, MemberOrigin.Version6, false, true)),
        .. new[] { "MousePointer", "HideSelection", "Format", "MouseIcon", "Appearance", "BackColor", "Font",
            "ForeColor", "Enabled", "BorderStyle", "OLEDragMode", "OLEDropMode" }
            .Select(name => new InBoxMember(name, MemberKind.Property, MemberOrigin.Version6, true, false)),
        new("hWnd", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("Refresh", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("OLEDrag", MemberKind.Method, MemberOrigin.Version6, false, false),
        new("Change", MemberKind.Event, MemberOrigin.Version6, false, true),
        .. new[] { "ValidationError", "KeyDown", "KeyPress", "KeyUp" }
            .Select(name => new InBoxMember(name, MemberKind.Event, MemberOrigin.Version6, false, true)),
        .. new[] { "OLEStartDrag", "OLEGiveFeedback", "OLESetData",
            "OLECompleteDrag", "OLEDragOver", "OLEDragDrop" }
            .Select(name => new InBoxMember(name, MemberKind.Event, MemberOrigin.Version6, false, false)),
    ];

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        // Saved properties are a later slice; none may disappear silently at F5.
        if (instance.UnknownRawProperties.Any(p => p.Name is not ("_ExtentX" or "_ExtentY" or "_Version")))
            throw new NotImplementedException("Loading MaskEdBox saved properties is not implemented (#1098)");
        return new VBMaskEdBox();
    }
}
