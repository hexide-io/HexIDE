using System.Globalization;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Components.InBox;

/// <summary>The nonvisual MSComDlg control (#1084). Its own file properties are carried verbatim.</summary>
public sealed class CommonDialogComponentClass : InBoxComponentClass
{
    private CommonDialogComponentClass() : base([VBProperties.LeftProperty, VBProperties.TopProperty], [], [],
        hasBounds: false, hasVisible: false) { }

    static CommonDialogComponentClass()
    {
        VBProperties.WidthProperty.OverrideDefault<CommonDialogComponentClass>(32);
        VBProperties.HeightProperty.OverrideDefault<CommonDialogComponentClass>(32);
    }

    public static CommonDialogComponentClass Instance { get; } = new();
    public override string Name => "CommonDialog";
    public override string ControlClassName => "CommonDialog";
    public override RuntimeLibrary Library => ControlLibraries.MSComDlg;
    public override bool IsVisual => false;
    public override (int Width, int Height) AddedSize => (480, 480);
    public override (int X, int Y) DefaultExtent => (847, 847);
    public override (int X, int Y) ExtentsToWrite(ComponentInstance instance, int? readX, int? readY) =>
        (readX ?? DefaultExtent.X, readY ?? DefaultExtent.Y);
    public override IReadOnlyList<InBoxMember> Members =>
    [
        .. CarriedPropertyOrder.Select(name => new InBoxMember(name, MemberKind.Property, MemberOrigin.Version6,
            IsFrmProperty: true, Implemented: name != "HelpContext")),
        new("FileTitle", MemberKind.Property, MemberOrigin.Version6, false, true),
        new("hDC", MemberKind.Property, MemberOrigin.Version6, false, false),
        new("Action", MemberKind.Property, MemberOrigin.Version6, false, false),
        .. new[] { "ShowOpen", "ShowSave", "ShowFont", "ShowColor", "ShowPrinter", "ShowHelp", "AboutBox" }
            .Select(name => new InBoxMember(name, MemberKind.Method, MemberOrigin.Version6, false,
                name is "ShowOpen" or "ShowSave" or "ShowFont")),
    ];

    public override IReadOnlyList<string> CarriedPropertyOrder { get; } =
    [
        "CancelError", "Color", "Copies", "DefaultExt", "DialogTitle", "FileName", "Filter", "FilterIndex", "Flags",
        "FontBold", "FontItalic", "FontName", "FontSize", "FontStrikeThru", "FontUnderLine", "FromPage", "HelpCommand",
        "HelpContext", "HelpFile", "HelpKey", "InitDir", "Max", "Min", "MaxFileSize", "PrinterDefault", "ToPage", "Orientation",
    ];

    public override bool WriteCarriedProperty(ComponentInstance.UnknownRawProperty property)
    {
        if (property.Lines.Count != 1)
            return true;
        var line = property.Lines[0];
        var equals = line.IndexOf('=');
        if (equals < 0)
            return true;
        var value = line[(equals + 1)..].Trim();
        var name = property.Name.ToLowerInvariant();
        if (name is "defaultext" or "dialogtitle" or "filename" or "filter" or "fontname" or "helpfile" or "helpkey" or "initdir")
            return value != "\"\"";
        var comment = value.IndexOf('\'');
        if (comment >= 0)
            value = value[..comment].TrimEnd();
        var defaultValue = name switch
        {
            "copies" or "orientation" => 1,
            "fontsize" => 8,
            "maxfilesize" => 260,
            "printerdefault" => -1,
            _ => 0,
        };
        return !decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            || number != defaultValue;
    }

    public override Control InstantiateForDesigner(ComponentInstance instance) =>
        PlaceholderComponentClass.ForType(VBTypeName).Instantiate(instance);

    protected override Control InstantiateInternal(ComponentInstance instance)
    {
        if (instance.ContainedControls.Count > 0
            || instance.UnknownRawProperties.Any(p => p.Name.Equals("Index", StringComparison.OrdinalIgnoreCase)))
            throw new NotImplementedException("A CommonDialog control array or nested control is not implemented (#1084)");
        var dialog = new VBCommonDialog(null!);
        foreach (var property in instance.UnknownRawProperties)
        {
            if (property.Name.StartsWith('_') || property.Name.Equals("Index", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var line in property.Lines)
            {
                var equals = line.IndexOf('=');
                if (equals >= 0)
                    dialog.SetDesignTime(property.Name, VBLoader.DesignTimeValue(line[(equals + 1)..].Trim()));
            }
        }
        return dialog;
    }
}
