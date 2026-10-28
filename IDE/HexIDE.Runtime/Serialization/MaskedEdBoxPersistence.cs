using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Serialization;

// The saved-file table in the MaskedEdBox oracle (#1098). These are carried properties, not designer editors.
internal static class MaskedEdBoxPersistence
{
    internal static readonly PropertyClass<VBSerializedComponent?> SavedProperty =
        new("(MaskEdBoxSaved)", "", PropertyCategory.Internal);

    private static readonly (string Name, object Default)[] Scalars =
    [
        ("BorderStyle", 1), ("ClipMode", 0), ("MousePointer", 0), ("Appearance", 1),
        ("BackColor", -2147483643), ("ForeColor", -2147483640),
        ("PromptInclude", true), ("AllowPrompt", false), ("AutoTab", false),
        ("HideSelection", true), ("Enabled", true), ("MaxLength", 64),
        ("OLEDragMode", 0), ("OLEDropMode", 0),
    ];

    internal static bool OwnLine(string name) => Scalars.Any(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
        || new[] { "Font", "Format", "Mask", "PromptChar", "Text" }.Contains(name, StringComparer.OrdinalIgnoreCase);

    internal static bool CanWrite(VBSerializedComponent saved) => saved.Properties.Keys.All(name =>
        MaskedEdBoxComponentClass.Instance.Member(name) is not { IsFrmProperty: true } || OwnLine(name));

    internal static void Loaded(ComponentInstance instance, VBSerializedComponent saved)
    {
        instance.SetProperty(SavedProperty, saved);
        if (!saved.Properties.ContainsKey("Width")) instance.SetProperty(VBProperties.WidthProperty, 200);
        if (!saved.Properties.ContainsKey("Height")) instance.SetProperty(VBProperties.HeightProperty, 75);
    }

    internal static void Write(VbFrmFormatSerializer vb, ComponentInstance instance)
    {
        var saved = instance.GetPropertyOrDefault(SavedProperty);
        if (saved is not null && !CanWrite(saved))
            throw new NotImplementedException("MaskEdBox persistence is not implemented (#1098)");
        void Scalar(string name, object fallback)
        {
            var value = saved?.Properties.GetValueOrDefault(name) ?? fallback;
            if (fallback is bool && value is int number) value = number != 0;
            if (!Equals(value, fallback)) vb.WriteProperty(name, fallback.GetType(), value);
        }
        foreach (var (name, fallback) in Scalars) Scalar(name, fallback);
        if (instance.UnknownRawProperties.FirstOrDefault(p => p.Name.Equals("Font", StringComparison.OrdinalIgnoreCase)) is { Lines: not null } font)
            vb.WriteVerbatimProperty("Font", font.Lines);
        Scalar("Format", "");
        Scalar("Mask", "");
        vb.WriteProperty("PromptChar", typeof(string), saved?.Properties.GetValueOrDefault("PromptChar") ?? "_");
        // VB6 writes the two non-default OLE modes a second time, after PromptChar.
        Scalar("OLEDragMode", 0);
        Scalar("OLEDropMode", 0);
    }
}
