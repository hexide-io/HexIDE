using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.Serialization;

public static class RichTextBoxPersistence
{
    private static readonly Encoding Ansi = CodePagesEncodingProvider.Instance.GetEncoding(1252)!;
    private static readonly PropertyClass<string?> SeedRtf = new("(RichTextRTF)", "", PropertyCategory.Internal);
    private static readonly HashSet<string> BooleanProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        "Enabled", "HideSelection", "MultiLine", "ReadOnly", "DisableNoScroll", "AutoVerbMenu"
    };
    private static readonly string[] OwnOrder =
    [
        "BackColor", "BorderStyle", "Enabled", "HideSelection", "MultiLine", "ReadOnly", "ScrollBars", "MousePointer",
        "DisableNoScroll", "MaxLength", "BulletIndent", "Appearance", "RightMargin", "AutoVerbMenu", "OLEDragMode",
        "OLEDropMode", "TextRTF", "Font"
    ];

    // Saved own properties are carried, not offered as editable designer properties until modelled faithfully.
    internal static bool IsOwnProperty(string name) => OwnOrder.Contains(name, StringComparer.OrdinalIgnoreCase)
        || name.Equals("Locked", StringComparison.OrdinalIgnoreCase);

    public static void Seed(ComponentInstance instance, string text) => instance.SetProperty(SeedRtf, RtfDocument.FromText(text).Write());

    private static ComponentInstance.UnknownRawProperty? Raw(ComponentInstance instance, string name) =>
        instance.UnknownRawProperties.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is
            { Name: not null } raw ? raw : null;

    private static string Source(ComponentInstance instance)
    {
        if (instance.GetPropertyOrDefault(SeedRtf) is { } seed) return seed;
        if (Raw(instance, "TextRTF") is not { } raw) return RtfDocument.Empty.Write();
        var line = raw.Lines.Single();
        if (FrxDeserializer.Citation(line) is { } citation)
        {
            if (raw.Records is null || !raw.Records.TryGetValue(citation.Offset, out var bytes)
                || bytes.Length < 4 || BinaryPrimitives.ReadInt32LittleEndian(bytes) != bytes.Length - 4)
                throw new NotImplementedException("RichTextBox TextRTF record framing is not implemented (#1100)");
            return Ansi.GetString(bytes, 4, bytes.Length - 4);
        }
        var value = line[(line.IndexOf('=') + 1)..].Trim();
        if (value.Length < 2 || value[0] != '"' || value[^1] != '"')
            throw new NotImplementedException("RichTextBox TextRTF saved value is not implemented (#1100)");
        return value[1..^1].Replace("\"\"", "\"");
    }

    internal static RtfDocument ReadDocument(ComponentInstance instance)
    {
        var document = RtfDocument.Read(Source(instance));
        return VBLoader.RawDouble(instance, "MultiLine") is 0 ? FirstParagraph(document) : document;
    }

    internal static RtfDocument FirstParagraph(RtfDocument document)
    {
        var end = document.Text.IndexOfAny(['\r', '\n']);
        if (end == 0)
            throw new NotImplementedException("RichTextBox empty first-paragraph RTF output is not measured (#1100)");
        return end < 0 ? document : document.WithRuns(document.Slice(0, end));
    }

    internal static string RtfToWrite(ComponentInstance instance)
    {
        var source = Source(instance);
        var raw = Raw(instance, "TextRTF");
        if (raw is null || FrxDeserializer.Citation(raw.Value.Lines[0]) is null
            || VBLoader.RawDouble(instance, "MultiLine") is 0 && ReadDocument(instance).Text != RtfDocument.Read(source).Text)
            return ReadDocument(instance).Write();
        return source;
    }

    private static bool IsDefault(ComponentInstance.UnknownRawProperty raw)
    {
        if (raw.Name.Equals("Font", StringComparison.OrdinalIgnoreCase))
        {
            var fontDefaults = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Name"] = "\"MS Sans Serif\"", ["Size"] = "8.25", ["Charset"] = "0", ["Weight"] = "400",
                ["Underline"] = "0", ["Italic"] = "0", ["Strikethrough"] = "0"
            };
            foreach (var line in raw.Lines.Skip(1).SkipLast(1))
            {
                var equals = line.IndexOf('=');
                if (equals < 0 || !fontDefaults.TryGetValue(line[..equals].Trim(), out var expectedFontValue)
                    || line[(equals + 1)..].Split('\'')[0].Trim() != expectedFontValue) return false;
            }
            return true;
        }
        if (raw.Lines.Count != 1) return false;
        var value = raw.Lines[0][(raw.Lines[0].IndexOf('=') + 1)..].Split('\'')[0].Trim();
        if (raw.Name.Equals("BackColor", StringComparison.OrdinalIgnoreCase))
            return VBColor.TryParse(value, out var color) && color == VBColor.FromSystemColor(VbSystemColor.Window);
        var defaults = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase)
        {
            ["BorderStyle"] = 1, ["HideSelection"] = -1, ["MultiLine"] = -1, ["ReadOnly"] = 0,
            ["ScrollBars"] = 0, ["MousePointer"] = 0, ["DisableNoScroll"] = 0, ["MaxLength"] = 0,
            ["BulletIndent"] = 0, ["Appearance"] = 1, ["RightMargin"] = 0, ["AutoVerbMenu"] = 0,
            ["OLEDragMode"] = 1, ["OLEDropMode"] = 2
        };
        return defaults.TryGetValue(raw.Name, out var expected)
            && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == expected;
    }

    internal static void Write(VbFrmFormatSerializer vb, ComponentInstance instance, CompanionCitations citations)
    {
        vb.WriteProperty("_ExtentX", typeof(int), InBoxComponentClass.Extent((int)Math.Round(instance.GetPropertyOrDefault(VBProperties.WidthProperty))));
        vb.WriteProperty("_ExtentY", typeof(int), InBoxComponentClass.Extent((int)Math.Round(instance.GetPropertyOrDefault(VBProperties.HeightProperty))));
        vb.WriteProperty("_Version", typeof(int), 393217);
        foreach (var name in OwnOrder)
        {
            if (name == "TextRTF")
            {
                var source = RtfToWrite(instance);
                if (Raw(instance, name) is { } saved && FrxDeserializer.Citation(saved.Lines[0]) is { } citation
                    && saved.Records is not null && saved.Records.TryGetValue(citation.Offset, out var original)
                    && source == Source(instance))
                {
                    vb.WriteRawProperty(name, "$" + citations.Cite(original));
                    continue;
                }
                var payload = Ansi.GetBytes(source);
                var record = new byte[payload.Length + 4];
                BinaryPrimitives.WriteInt32LittleEndian(record, payload.Length);
                payload.CopyTo(record, 4);
                vb.WriteRawProperty(name, "$" + citations.Cite(record));
            }
            else if (Raw(instance, name) is { } raw && !IsDefault(raw))
            {
                if (VBLoader.RawDouble(instance, name) is { } number && double.IsFinite(number)
                    && (!BooleanProperties.Contains(name) || number is 0 or -1))
                {
                    if (BooleanProperties.Contains(name)) vb.WriteProperty(name, typeof(bool), number != 0);
                    else vb.WriteProperty(name, typeof(double), number);
                }
                else vb.WriteVerbatimProperty(name, raw.Lines.Select(line => citations.Cite(line, raw.Records)).ToList());
            }
            else if (name == "Enabled")
                vb.WriteProperty(name, typeof(bool), true);
        }
    }
}
