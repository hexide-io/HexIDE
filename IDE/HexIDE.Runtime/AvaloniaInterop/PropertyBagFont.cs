using System.IO;
using System.Text;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.AvaloniaInterop;

// Only the streams measured in #4741 and a-propertybag-at-run-time.md, not a general OLE font decoder.
internal sealed record PropertyBagFont(string Name, int Size, ushort Weight, ushort Charset, byte Flags)
{
    internal static readonly byte[] Clsid = Convert.FromHexString("0352E30B918FCE119DE300AA004BB851");
    internal static readonly PropertyBagFont Fresh = new("", 80000, 0, 1, 0);

    internal static PropertyBagFont FromDescriptor(VBFont font) => new(font.FontFamilyName,
        checked((int)(font.Size * 10000)), checked((ushort)font.Weight), checked((ushort)font.Charset),
        (byte)((font.Italic ? 2 : 0) | (font.Underline ? 4 : 0) | (font.Strikethrough ? 8 : 0)));

    internal PropertyBagFont? Assigned(string member, Vb6Value value) => member.ToLowerInvariant() switch
    {
        "name" when value.Type == Vb6Value.ValueType.String && value.Value is string name
            && name is "Arial" or "Courier New" or "Tahoma" => this with { Name = name },
        "size" when value.Type == Vb6Value.ValueType.Integer && value.Value is int size && size == 10
            => this with { Size = 100000 },
        "size" when value.Type == Vb6Value.ValueType.Double && value.Value is double size && size == 10.5
            => this with { Size = 105000 },
        "bold" when value.Type == Vb6Value.ValueType.Boolean && value.Value is true => this with { Weight = 700 },
        "weight" when value.Type == Vb6Value.ValueType.Integer && value.Value is int weight && weight is 600 or 700
            => this with { Weight = (ushort)weight },
        "charset" when value.Type == Vb6Value.ValueType.Integer && value.Value is int charset && charset == 2
            => this with { Charset = 2 },
        "italic" when value.Type == Vb6Value.ValueType.Boolean && value.Value is true => this with { Flags = (byte)(Flags | 2) },
        "underline" when value.Type == Vb6Value.ValueType.Boolean && value.Value is true => this with { Flags = (byte)(Flags | 4) },
        "strikethrough" when value.Type == Vb6Value.ValueType.Boolean && value.Value is true => this with { Flags = (byte)(Flags | 8) },
        _ => null,
    };

    internal VBFont Realized()
    {
        // #4741, 2026-11-20: untouched and single-assignment streams, before any property read.
        var raw = (Name, Size, Weight, Charset, Flags) switch
        {
            ("", 80000, 0, 1, 0 or 2) => new VBFont("MS Sans Serif", 8.25, italic: Flags == 2),
            ("Arial", 80000, 0, 1, 0) => new VBFont("Arial", 8.25),
            ("", 100000, 0, 1, 0) => new VBFont("MS Sans Serif", 9.75),
            ("", 80000, 700, 1, 0) => new VBFont("Arial", 8.25, weight: 700),
            ("", 80000, 0, 2, 0) => new VBFont("Wingdings", 8.25, charset: 2),
            _ => (VBFont?)null,
        };
        if (raw is { } font) return font;
        // The realized streams in the 11-18 oracle, plus Arial after reading Size in the 11-20 comment.
        if ((Name, Size, Weight, Charset, Flags) is
            ("MS Sans Serif", 82500, 400, 0, 0)
            or ("Arial", 82500, 400, 0, 0)
            or ("Arial", 97500, 400, 0, 0 or 2 or 4 or 8)
            or ("Arial", 97500, 700, 0, 0)
            or ("Wingdings", 97500, 400, 2, 0)
            or ("Courier New", 97500, 400, 0, 0)
            or ("Tahoma", 105000, 700, 0, 2)
            // A Size 10 stream read back, then read (#4741's review, 2026-11-20).
            or ("MS Sans Serif", 97500, 400, 0, 0))
            return new VBFont(Name, Size / 10000.0, italic: (Flags & 2) != 0, charset: Charset,
                underline: (Flags & 4) != 0, strikethrough: (Flags & 8) != 0, weight: Weight);
        throw Unmeasured();
    }

    internal void Write(BinaryWriter writer)
    {
        _ = Realized();
        writer.Write(Clsid);
        writer.Write((byte)1);
        writer.Write(Charset);
        writer.Write(Flags);
        writer.Write(Weight);
        writer.Write(Size);
        writer.Write((byte)Name.Length);
        writer.Write(Encoding.ASCII.GetBytes(Name));
    }

    internal static PropertyBagFont Read(BinaryReader reader)
    {
        if (reader.ReadByte() != 1) throw Unmeasured();
        var charset = reader.ReadUInt16();
        var flags = reader.ReadByte();
        var weight = reader.ReadUInt16();
        var size = reader.ReadInt32();
        var length = reader.ReadByte();
        if ((flags & ~14) != 0 || length > 13) throw Unmeasured();
        var bytes = reader.ReadBytes(length);
        if (bytes.Length != length) throw new EndOfStreamException();
        foreach (var b in bytes) if (b > 127) throw Unmeasured();
        var result = new PropertyBagFont(Encoding.ASCII.GetString(bytes), size, weight, charset, flags);
        _ = result.Realized();
        return result;
    }

    internal static NotImplementedException Unmeasured() =>
        new("PropertyBag StdFont stream or realization is unmeasured (#4741)");
}
