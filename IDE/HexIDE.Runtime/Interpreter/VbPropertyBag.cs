using System.IO;
using System.Numerics;
using System.Text;
using HexIDE.Runtime.AvaloniaInterop;

namespace HexIDE.Runtime.Interpreter;

// Standalone bags only. The UserControl bag has separate, unmeasured persistence rules (#4741).
internal sealed class VbPropertyBag : ICSharpAsyncProxy, ICSharpPropertyBag
{
    private sealed record Entry(string Name, Vb6Value Value);
    private List<Entry> entries = [];
    private static readonly UnicodeEncoding WireEncoding = new(bigEndian: false, byteOrderMark: false, throwOnInvalidBytes: true);
    private static readonly Vb6Value.ValueType BytesType = new(Vb6Value.ValueType.Byte, true);

    void ICSharpProxy.Call(string method, List<Vb6Value> args) => throw new InvalidOperationException("PropertyBag calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "contents":
                Count(args, 0, 0);
                return Task.FromResult<Vb6Value?>(Bytes(Encode()));
            case "writeproperty":
                Count(args, 2, 3);
                var name = Name(args[0]);
                var value = args[1];
                // Validate before storing; Contents must never silently substitute another wire type.
                using (var stream = new MemoryStream())
                using (var writer = new BinaryWriter(stream)) WriteValue(writer, value);
                if (args.Count == 3)
                {
                    var comparison = value.TryCompareTo(args[2]);
                    if (comparison is null)
                        throw new NotImplementedException("PropertyBag default comparison is unmeasured (#4741)");
                    if (comparison == 0) return Task.FromResult<Vb6Value?>(null);
                }
                entries.Add(new Entry(name.ToLowerInvariant(), Copy(value)));
                return Task.FromResult<Vb6Value?>(null);
            case "readproperty":
                Count(args, 1, 2);
                var key = Name(args[0]);
                for (var i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                        return Task.FromResult<Vb6Value?>(Copy(entries[i].Value));
                if (args.Count == 2) return Task.FromResult<Vb6Value?>(args[1]);
                throw new VBRunTimeException(new VBStandardError(327, $"Data value named '{key}' not found"));
            default:
                throw new NotImplementedException($"PropertyBag member {method} is not implemented (#4741)");
        }
    }

    private static void Count(List<Vb6Value> args, int min, int max)
    {
        if (args.Count < min || args.Count > max)
            throw new NotImplementedException("PropertyBag argument-count errors are unmeasured (#4741)");
    }

    private static string Name(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.String)
            throw new NotImplementedException("PropertyBag name conversion is unmeasured (#4741)");
        var name = (string)value.Value!;
        if (name.Length == 0) throw Invalid();
        _ = EncodeText(name);
        return name;
    }

    private static Vb6Value Copy(Vb6Value value) => value.Value switch
    {
        VBArray array => value.WithArray(array.CopyForLet(static v => v)),
        ControlFont font => new Vb6Value(font.CopyForPropertyBag()),
        _ => value,
    };

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = default;
        if (!name.Equals("Contents", StringComparison.OrdinalIgnoreCase)) return false;
        value = Bytes(Encode());
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (!name.Equals("Contents", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Type == Vb6Value.ValueType.String || value.Type == Vb6Value.ValueType.Integer
            || value.Type == Vb6Value.ValueType.Long || value.Type == Vb6Value.ValueType.Single
            || value.Type == Vb6Value.ValueType.Double || value.Type == Vb6Value.ValueType.Currency
            || value.Type == Vb6Value.ValueType.Decimal || value.Type == Vb6Value.ValueType.Byte)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (value.Value is not VBArray array || array.ElementType != Vb6Value.ValueType.Byte || array.Rank != 1)
            throw new NotImplementedException("PropertyBag Contents conversion is unmeasured (#4741)");
        var bytes = new byte[array.Length()];
        for (var i = 0; i < bytes.Length; i++) bytes[i] = (byte)array.GetValue([array.LowerBound() + i]).Value!;
        var parsed = Decode(bytes);
        entries = parsed;
        return true;
    }

    internal static Vb6Value Bytes(byte[] bytes, int lower = 0)
    {
        var result = new Vb6Value(BytesType, [(lower, checked(lower + bytes.Length - 1))]);
        var array = (VBArray)result.Value!;
        for (var i = 0; i < bytes.Length; i++) array.SetValue([lower + i], new Vb6Value(bytes[i]));
        return result;
    }

    private static uint Hash(string name)
    {
        uint hash = 0;
        foreach (var c in name) hash = BitOperations.RotateLeft(hash, 13) ^ c;
        return hash;
    }

    private byte[] Encode()
    {
        if (entries.Count == 0) return [];
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write(0xB293);
        writer.Write(0);
        for (var i = 0; i < entries.Count; i++)
        {
            var start = stream.Position;
            var entry = entries[i];
            writer.Write(WireType(entry.Value));
            writer.Write(checked((ushort)entry.Name.Length));
            writer.Write(Hash(entry.Name));
            writer.Write(0);
            writer.Write(EncodeText(entry.Name));
            WriteValue(writer, entry.Value);
            while (stream.Position % 4 != 0) writer.Write((byte)0);
            var end = stream.Position;
            stream.Position = start + 8;
            writer.Write(checked((int)(i == entries.Count - 1 ? 8 - start : end - start)));
            stream.Position = end;
        }
        stream.Position = 4;
        writer.Write(checked((int)stream.Length));
        return stream.ToArray();
    }

    private static ushort WireType(Vb6Value value)
    {
        var type = value.Type;
        if (value.Value is VBArray array)
        {
            if (array.ElementType != Vb6Value.ValueType.Byte || array.Rank != 1)
                throw new VBRunTimeException(new VBStandardError(328, "Illegal parameter.  Can't write arrays"));
            return 0x2011;
        }
        if (type == Vb6Value.ValueType.EmptyVariant) return 0;
        if (type == Vb6Value.ValueType.Null) return 1;
        if (type == Vb6Value.ValueType.Integer) return 2;
        if (type == Vb6Value.ValueType.Long) return 3;
        if (type == Vb6Value.ValueType.Single) return 4;
        if (type == Vb6Value.ValueType.Double) return 5;
        if (type == Vb6Value.ValueType.Currency) return 6;
        if (type == Vb6Value.ValueType.Date) return 7;
        if (type == Vb6Value.ValueType.String) return 8;
        if (value.Value is VbCollection)
            throw new VBRunTimeException(new VBStandardError(330, "Illegal parameter.  Can't write object because it does not support persistence."));
        if (type == Vb6Value.ValueType.Object || type == Vb6Value.ValueType.Nothing || type == Vb6Value.ValueType.CSharpProxyObject || type == Vb6Value.ValueType.Control)
        {
            if (value.Value is null or ControlFont) return 9;
            throw Objects();
        }
        if (type == Vb6Value.ValueType.Boolean) return 11;
        if (type == Vb6Value.ValueType.Decimal) return 14;
        if (type == Vb6Value.ValueType.Byte) return 17;
        throw new NotImplementedException("PropertyBag value type is not implemented (#4741)");
    }

    private static void WriteValue(BinaryWriter writer, Vb6Value value)
    {
        switch (WireType(value))
        {
            case 0: case 1: return;
            case 2: writer.Write(checked((short)(int)value.Value!)); return;
            case 3: writer.Write(checked((int)(long)value.Value!)); return;
            case 4: writer.Write((float)value.Value!); return;
            case 5: writer.Write((double)value.Value!); return;
            case 6: writer.Write(checked((long)((decimal)value.Value! * 10000))); return;
            case 7: writer.Write(((VbDate)value.Value!).Serial); return;
            case 8:
                var text = (string)value.Value!;
                writer.Write(text.Length);
                writer.Write(EncodeText(text));
                return;
            case 9:
                if (value.Value is ControlFont font) font.Persistence.Write(writer);
                else writer.Write(new byte[16]);
                return;
            case 11: writer.Write((short)((bool)value.Value! ? -1 : 0)); return;
            case 14:
                var bits = decimal.GetBits((decimal)value.Value!);
                writer.Write((ushort)14);
                writer.Write((byte)(bits[3] >> 16));
                writer.Write((byte)(bits[3] >> 24));
                writer.Write(bits[2]); writer.Write(bits[0]); writer.Write(bits[1]);
                return;
            case 17: writer.Write((byte)value.Value!); return;
            case 0x2011:
                var array = (VBArray)value.Value!;
                writer.Write((ushort)1); writer.Write(array.Length()); writer.Write(array.LowerBound());
                foreach (var item in array.EnumerateElements()) writer.Write((byte)item.Value!);
                return;
        }
    }

    private static List<Entry> Decode(byte[] bytes)
    {
        if (bytes.Length == 0) return [];
        try
        {
            using var stream = new MemoryStream(bytes);
            using var reader = new BinaryReader(stream, Encoding.Unicode);
            if (reader.ReadInt32() != 0xB293 || reader.ReadInt32() != bytes.Length || bytes.Length == 8) throw Invalid();
            var result = new List<Entry>();
            while (stream.Position < stream.Length)
            {
                var start = stream.Position;
                var type = reader.ReadUInt16();
                var length = reader.ReadUInt16();
                var hash = reader.ReadUInt32();
                var distance = reader.ReadInt32();
                var name = ReadText(reader, length);
                if (length == 0 || Hash(name) != hash) throw Invalid();
                var value = ReadValue(reader, type);
                var end = (stream.Position + 3) & ~3L;
                if (end > stream.Length || (end == stream.Length ? distance != 8 - start : distance != end - start)) throw Invalid();
                stream.Position = end;
                result.Add(new Entry(name, value));
            }
            return result;
        }
        catch (Exception e) when (e is EndOfStreamException or OverflowException or ArgumentException)
        {
            throw Invalid();
        }
    }

    private static string ReadText(BinaryReader reader, int length)
    {
        if (length < 0 || length > (reader.BaseStream.Length - reader.BaseStream.Position) / 2) throw Invalid();
        try
        {
            return WireEncoding.GetString(reader.ReadBytes(checked(length * 2)));
        }
        catch (DecoderFallbackException)
        {
            throw UnpairedText();
        }
    }

    private static byte[] EncodeText(string text)
    {
        try
        {
            return WireEncoding.GetBytes(text);
        }
        catch (EncoderFallbackException)
        {
            throw UnpairedText();
        }
    }

    private static NotImplementedException UnpairedText() =>
        new("PropertyBag unpaired UTF-16 code units are unmeasured (#4741)");

    private static Vb6Value ReadValue(BinaryReader reader, ushort type)
    {
        switch (type)
        {
            case 0: return Vb6Value.Variant;
            case 1: return Vb6Value.Null;
            case 2: return new Vb6Value((int)reader.ReadInt16());
            case 3: return new Vb6Value((long)reader.ReadInt32());
            case 4: return new Vb6Value(reader.ReadSingle());
            case 5: return new Vb6Value(reader.ReadDouble());
            case 6: return Vb6Value.NewCurrency(reader.ReadInt64() / 10000m);
            case 7: return new Vb6Value(new VbDate(reader.ReadDouble()));
            case 8: return new Vb6Value(ReadText(reader, reader.ReadInt32()));
            case 9:
                var clsid = reader.ReadBytes(16);
                if (clsid.Length != 16) throw Invalid();
                if (clsid.AsSpan().SequenceEqual(PropertyBagFont.Clsid))
                    return new Vb6Value(ControlFont.FromPersistence(PropertyBagFont.Read(reader)));
                foreach (var b in clsid) if (b != 0) throw Objects();
                return Vb6Value.Nothing;
            case 11: return new Vb6Value(reader.ReadInt16() != 0);
            case 14:
                if (reader.ReadUInt16() != 14) throw Invalid();
                var scale = reader.ReadByte(); var sign = reader.ReadByte();
                var hi = reader.ReadInt32(); var lo = reader.ReadInt32(); var mid = reader.ReadInt32();
                if (scale > 28 || (sign != 0 && sign != 128)) throw Invalid();
                return Vb6Value.NewDecimal(new decimal(lo, mid, hi, sign != 0, scale));
            case 17: return new Vb6Value(reader.ReadByte());
            case 0x2011:
                if (reader.ReadUInt16() != 1) throw Invalid();
                var count = reader.ReadInt32(); var lower = reader.ReadInt32();
                if (count < 0 || count > reader.BaseStream.Length - reader.BaseStream.Position) throw Invalid();
                return Bytes(reader.ReadBytes(count), lower);
            default: throw Invalid();
        }
    }

    private static VBRunTimeException Invalid() => new(VBStandardError.InvalidProcedureCall);
    private static NotImplementedException Objects() => new("PropertyBag persistable and other objects are not implemented (#4741)");
}
