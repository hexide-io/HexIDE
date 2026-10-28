using System.Globalization;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Serialization;
using static HexIDE.Runtime.Components.VBProperties;

namespace HexIDE.Runtime.Components;

public sealed class RichTextBoxComponentClass : InBoxComponentClass
{
    private RichTextBoxComponentClass() : base([TabIndexProperty, TabStopProperty, ToolTipTextProperty], [],
        [TextBoxComponentClass.ChangeEvent, SelChangeEvent, KeyDownEvent, KeyPressEvent, KeyUpEvent,
            ClickEvent, DblClickEvent, MouseDownEvent, MouseMoveEvent, MouseUpEvent]) { }

    public override string Name => "RichTextBox";
    public override (int Width, int Height) AddedSize => (1500, 750);
    public override string ControlClassName => "RichTextBox";
    public override RuntimeLibrary Library => ControlLibraries.RichTextLib;
    public override IReadOnlyList<InBoxMember> Members => [];
    public override (int X, int Y) DefaultExtent =>
        throw new NotImplementedException("RichTextBox unsized default extents are not measured (#1100)");

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        var box = new VBRichTextBox
        {
            MultiLine = VBLoader.RawDouble(instance, "MultiLine") is not 0,
            InputLocked = VBLoader.RawDouble(instance, "ReadOnly") is { } readOnly && readOnly != 0,
            ScrollBars = VBLoader.RawNumber(instance, "ScrollBars") ?? 0,
            IsEnabled = VBLoader.RawDouble(instance, "Enabled") is not 0,
            Appearance = VBLoader.RawNumber(instance, "Appearance") ?? 1,
            BorderStyle = VBLoader.RawNumber(instance, "BorderStyle") ?? 1,
            SavedBackColor = ReadBackColor(instance)
        };
        box.SetDocument(RichTextBoxPersistence.ReadDocument(instance), false);
        box.InputMaxLength = (int)(VBLoader.RawDouble(instance, "MaxLength") ?? 0);
        return box;
    }

    private static VBColor ReadBackColor(ComponentInstance instance)
    {
        foreach (var property in instance.UnknownRawProperties)
        {
            if (!property.Name.Equals("BackColor", StringComparison.OrdinalIgnoreCase)) continue;
            if (property.Lines.Count > 0)
            {
                var line = property.Lines[0];
                var value = line[(line.IndexOf('=') + 1)..].Split('\'')[0].Trim();
                if (VBColor.TryParse(value, out var color)) return color;
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ole)
                    && VBColor.TryParse($"&H{ole:X8}&", out color)) return color;
            }
            throw new NotImplementedException("RichTextBox saved BackColor conversion is not implemented (#1100)");
        }
        return VBColor.FromSystemColor(VbSystemColor.Window);
    }

    // The designer must still open a carried, unsupported RTF document.
    public override Control InstantiateForDesigner(ComponentInstance instance) =>
        PlaceholderComponentClass.ForType(VBTypeName).Instantiate(instance);

    public static readonly EventClass SelChangeEvent = new("SelChange");
    public static RichTextBoxComponentClass Instance { get; } = new();
}
