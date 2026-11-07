using System.Globalization;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.RichText;

namespace HexIDE.Runtime.AvaloniaInterop;

internal static class RichTextBoxMembers
{
    private static readonly HashSet<string> Shared = new(StringComparer.OrdinalIgnoreCase)
    {
        "Name", "Index", "Left", "Top", "Width", "Height", "Visible", "Tag", "Container", "Parent", "Object",
        "Move", "ZOrder"
    };

    internal static bool TryGet(Control control, string name, out Vb6Value value)
    {
        value = default;
        if (control is not VBRichTextBox box || Shared.Contains(name)) return false;
        value = name.ToLowerInvariant() switch
        {
            "enabled" => new(box.IsEnabled),
            "locked" => new(box.InputLocked),
            "maxlength" => new(box.InputMaxLength),
            "multiline" => new(box.MultiLine),
            "scrollbars" => new(box.ScrollBars),
            "selprotected" => Mixed(box, r => new Vb6Value(r.Format.Protected)),
            "appearance" => new(box.Appearance),
            "borderstyle" => new(box.BorderStyle),
            "text" => new(box.Document.Text),
            "textrtf" => new(box.WriteRtf()),
            "selstart" => new((long)box.Start),
            "sellength" => new((long)box.Length),
            "seltext" => new(box.Document.Text.Substring(box.Start, Math.Min(box.Length, box.Document.Text.Length - box.Start))),
            "selrtf" => new(box.Document.WriteSelection(box.Start, Math.Min(box.Length, box.Document.Text.Length - box.Start))),
            "selbold" => Mixed(box, r => new Vb6Value(r.Format.Bold)),
            "selitalic" => Mixed(box, r => new Vb6Value(r.Format.Italic)),
            "selunderline" => Mixed(box, r => new Vb6Value(r.Format.Underline)),
            "selstrikethru" => Mixed(box, r => new Vb6Value(r.Format.StrikeThrough)),
            "selfontname" => Mixed(box, r => new Vb6Value(r.Format.FontName)),
            "selfontsize" => Mixed(box, r => new Vb6Value(r.Format.PointSize)),
            "selcolor" => Color(box),
            "selalignment" => Mixed(box, r => new Vb6Value(r.Paragraph.Alignment switch { "qr" => 1, "qc" => 2, _ => 0 })),
            _ => throw Unsupported(name)
        };
        if (name.StartsWith("Sel", StringComparison.OrdinalIgnoreCase)
            && name.ToLowerInvariant() is not ("selstart" or "sellength" or "seltext" or "selrtf"))
            value = value.AsVariant();
        return true;
    }

    internal static bool TrySet(Control control, string name, Vb6Value value)
    {
        if (control is not VBRichTextBox box || Shared.Contains(name)) return false;
        switch (name.ToLowerInvariant())
        {
            case "enabled": box.IsEnabled = Boolean(value); break;
            case "locked": box.InputLocked = Boolean(value); break;
            case "maxlength":
            {
                var limit = Integer(value);
                if (limit is not (0 or 5 or 20)) throw Unsupported("MaxLength outside the measured 0, 5 and 20 values");
                if (limit > 0 && box.Document.Text.Length > limit)
                    throw Unsupported("MaxLength reduction below existing text length");
                box.InputMaxLength = limit;
                break;
            }
            case "selprotected": box.FormatSelection(f => f with { Protected = Boolean(value) }); break;
            case "multiline":
            case "scrollbars":
            case "appearance":
            case "borderstyle":
                throw new VBRunTimeException(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty);
            case "text": box.SetText(Text(value)); break;
            case "textrtf": box.SetDocument(RtfDocument.Read(Text(value)), false); break;
            case "selstart": box.Select((int)Math.Min(Position(value), box.Document.Text.Length), 0); break;
            case "sellength": box.Select(box.Start, (int)Math.Min(Position(value), box.Document.Text.Length + 2L - box.Start)); break;
            case "seltext": box.ReplaceSelection(RtfDocument.FromText(Text(value)), true); break;
            case "selrtf":
            {
                var replacement = RtfDocument.Read(Text(value));
                if (replacement.Text.Contains('\r') || replacement.Text.Contains('\n')
                    || replacement.Runs.Any(r => r.Paragraph.Controls.Length != 0))
                    throw Unsupported("paragraph SelRTF insertion and bullet font remapping");
                box.ReplaceSelection(replacement, false);
                break;
            }
            case "selbold": box.FormatSelection(f => f with { Bold = Boolean(value, measuredBold: true) }); break;
            case "selitalic": box.FormatSelection(f => f with { Italic = Boolean(value) }); break;
            case "selunderline": box.FormatSelection(f => f with { Underline = Boolean(value) }); break;
            case "selstrikethru": box.FormatSelection(f => f with { StrikeThrough = Boolean(value) }); break;
            case "selfontname":
            {
                var face = Text(value);
                if (face.Length == 0) throw Unsupported("SelFontName empty-string restoration");
                box.FormatSelection(f => f with { FontName = face });
                break;
            }
            case "selfontsize":
            {
                var size = Number(value);
                if (size <= 0) break;
                // Fractional point conversion beyond the recorded half-point sizes is not measured.
                if (size * 2 != Math.Truncate(size * 2)) throw Unsupported("SelFontSize fractional conversion");
                if (size == 2000)
                {
                    box.FormatSelection(f => f with { PointSize = 1638.35f });
                    break;
                }
                if (size > 1638) throw Unsupported("SelFontSize maximum conversion to RTF");
                box.FormatSelection(f => f with { HalfPoints = (int)(size * 2), PointSize = (float)size });
                break;
            }
            case "selcolor":
            {
                var color = Integer(value);
                if (color < 0) color = 0;
                box.FormatSelection(f => f with { Color = color });
                break;
            }
            case "selalignment":
            {
                if (value.IsNull) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                var alignment = Integer(value);
                if (alignment is < 0 or > 2) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                box.Align(alignment switch { 1 => "qr", 2 => "qc", _ => "ql" });
                break;
            }
            default: throw Unsupported(name);
        }
        return true;
    }

    internal static void RefuseMethod(Control control, string name)
    {
        if (control is VBRichTextBox && !Shared.Contains(name)) throw Unsupported(name);
    }

    private static Vb6Value Color(VBRichTextBox box)
    {
        if (box.SelectedRuns.Select(r => r.Format.Color).Distinct().Take(2).Count() == 2)
            return Vb6Value.Null;
        throw Unsupported("SelColor scalar reads (Variant subtype 19)");
    }

    private static Vb6Value Mixed(VBRichTextBox box, Func<RtfRun, Vb6Value> read)
    {
        var values = box.SelectedRuns.Select(read).Distinct().ToArray();
        return values.Length == 1 ? values[0] : Vb6Value.Null;
    }

    private static bool Boolean(Vb6Value value, bool measuredBold = false)
    {
        if (value.Value is bool boolean) return boolean;
        if (measuredBold && (value.IsNull || Vb6Value.TryNumericToDouble(value, out var number) && number == 5)) return true;
        throw Unsupported("unmeasured Boolean conversion");
    }

    private static int Integer(Vb6Value value)
    {
        var number = Number(value);
        if (number != Math.Truncate(number)) throw Unsupported("fractional integer conversion");
        return Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Long, null).Value, CultureInfo.InvariantCulture);
    }

    private static double Number(Vb6Value value) =>
        Vb6Value.TryNumericToDouble(value, out var number) && double.IsFinite(number)
            ? number : throw Unsupported("unmeasured numeric conversion");
    private static long Position(Vb6Value value)
    {
        var position = Integer(value);
        return position < 0 ? throw new VBRunTimeException(VBStandardError.InvalidPropertyValue) : position;
    }
    private static string Text(Vb6Value value) => value.Value is string text
        ? text : throw Unsupported("unmeasured String conversion");
    private static NotImplementedException Unsupported(string name) => new($"RichTextBox {name} is not implemented (#1100)");
}
