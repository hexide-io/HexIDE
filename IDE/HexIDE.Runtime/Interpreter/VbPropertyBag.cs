using System.IO;
using System.Numerics;
using System.Text;
using HexIDE.Runtime.AvaloniaInterop;

namespace HexIDE.Runtime.Interpreter;

// Standalone bags only. UserControlPropertyBag reads a placed instance's saved .frm text (#4741).
internal sealed class VbPropertyBag : ICSharpAsyncProxy, ICSharpPropertyBag
{
    private sealed record Entry(string Name, Vb6Value Value);
    private List<Entry> entries = [];
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
                    if (EqualsDefault(value, args[2])) return Task.FromResult<Vb6Value?>(null);
                }
                entries.Add(new Entry(name.ToLowerInvariant(), Copy(value)));
                return Task.FromResult<Vb6Value?>(null);
            case "readproperty":
                Count(args, 1, 2);
                var key = Name(args[0], reading: true);
                for (var i = entries.Count - 1; i >= 0; i--)
                    if (entries[i].Name.Equals(key, StringComparison.OrdinalIgnoreCase))
                        return Task.FromResult<Vb6Value?>(Copy(entries[i].Value));
                if (args.Count == 2) return Task.FromResult<Vb6Value?>(args[1]);
                throw new VBRunTimeException(new VBStandardError(327, $"Data value named '{key}' not found"));
            default:
                throw new VBCompileErrorException("Method or data member not found");
        }
    }

    private static void Count(List<Vb6Value> args, int min, int max)
    {
        if (args.Count < min) throw new VBCompileErrorException("Argument not optional");
        if (args.Count > max) throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
    }

    // SP6 native/p-code: #4741, remaining standalone-bag measurements (2026-11-20).
    internal static Exception? LateBound(Vb6Value reference, VBCompileErrorException error, string member)
    {
        if (reference.DeclaredAs?.Equals("PropertyBag", StringComparison.OrdinalIgnoreCase) == true)
            // Early-bound Contents(0) may index the returned array, as a Variant-returning property can; unmeasured.
            return member.Equals("Contents", StringComparison.OrdinalIgnoreCase)
                ? new NotImplementedException("Early-bound PropertyBag.Contents with arguments is unmeasured (#4741)")
                : null;
        if (error.Message.Contains("Method or data member not found"))
            return new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        if (error.Message.Contains("Argument not optional") || error.Message.Contains("Wrong number of arguments"))
            return new VBRunTimeException(member.Equals("Contents", StringComparison.OrdinalIgnoreCase)
                ? VBStandardError.ObjectNotACollection : VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        return null;
    }

    private static string Name(Vb6Value value, bool reading = false)
    {
        if (value.IsNull)
            throw new VBRunTimeException(reading ? VBStandardError.TypeMismatch : VBStandardError.InvalidUseOfNull);
        if (IsObject(value) || value.Value is VBArray || value.Type == Vb6Value.ValueType.Error)
            throw new NotImplementedException("PropertyBag name conversion is unmeasured (#4741)");
        var name = VbDisplay.ToText(value);
        if (name.Length == 0) throw Invalid();
        return name;
    }

    private static bool IsObject(Vb6Value value) => value.Type == Vb6Value.ValueType.Object
        || value.Type == Vb6Value.ValueType.CSharpProxyObject || value.Type == Vb6Value.ValueType.Control
        || value.Type == Vb6Value.ValueType.Nothing;

    private static bool EqualsDefault(Vb6Value value, Vb6Value fallback)
    {
        if (IsObject(value) || IsObject(fallback))
        {
            if (IsObject(value) && IsObject(fallback) && fallback.Value is null or ControlFont
                && (value.Value is null || fallback.Value is null))
                return value.Value is null && fallback.Value is null;
            throw UnmeasuredComparison();
        }
        if (value.Value is VBArray || value.Type == Vb6Value.ValueType.Error) throw UnmeasuredComparison();
        // An Error default was never measured, and assignment's rule for it (#2520) need not be OLE's conversion.
        if (fallback.Type == Vb6Value.ValueType.Error) throw UnmeasuredComparison();
        // OLE converts a Byte array to a String, so a String against an array default may compare equal. Unmeasured.
        if (fallback.Value is VBArray && value.Type == Vb6Value.ValueType.String) throw UnmeasuredComparison();
        if (value.IsNull)
        {
            if (fallback.Value is VBArray) throw UnmeasuredComparison();
            return true;
        }
        if (fallback.IsNull) return false;
        if (value.Type == Vb6Value.ValueType.EmptyVariant)
        {
            if (fallback.Type == Vb6Value.ValueType.EmptyVariant
                || fallback.Type == Vb6Value.ValueType.String && (string)fallback.Value! == ""
                || Vb6Value.TryNumericToDouble(fallback, out var number) && number == 0) return true;
            throw UnmeasuredComparison();
        }
        try
        {
            // Automation's Boolean-to-String is not VB's "True"/"False". Only this comparison
            // is measured; do not expose an inferred conversion value (#4741, section 2).
            if (value.Type == Vb6Value.ValueType.String && fallback.Type == Vb6Value.ValueType.Boolean)
            {
                if ((string)value.Value! == "True" && (bool)fallback.Value!) return false;
                throw new NotImplementedException("PropertyBag Boolean-to-String default comparison is unmeasured (#4741)");
            }
            if (fallback.Value is VBArray) return false;
            var converted = VbNumeric.CoerceOnStore(fallback, value.Type, null);
            var comparison = value.TryCompareTo(converted) ?? throw UnmeasuredComparison();
            return comparison == 0;
        }
        catch (VBRunTimeException)
        {
            return false;
        }
        catch (NotImplementedException)
        {
            throw UnmeasuredComparison();
        }
    }

    private static NotImplementedException UnmeasuredComparison() =>
        new("PropertyBag default comparison is unmeasured (#4741)");

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
        if (value.Value is not VBArray array || array.ElementType != Vb6Value.ValueType.Byte || array.Rank != 1)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
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
        if (type == Vb6Value.ValueType.Error) return 10;
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
            case 10: writer.Write(0x800A0000u | (ushort)value.Value!); return;
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
        var chars = new char[length];
        for (var i = 0; i < length; i++) chars[i] = (char)reader.ReadUInt16();
        return new string(chars);
    }

    private static byte[] EncodeText(string text)
    {
        var bytes = new byte[checked(text.Length * 2)];
        for (var i = 0; i < text.Length; i++)
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(i * 2), text[i]);
        return bytes;
    }

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
            case 10:
                var scode = reader.ReadUInt32();
                // Only VB's own facility (an Error from CVErr) was measured; another SCODE, such as a Missing argument's, is not.
                if ((scode & 0xFFFF0000) != 0x800A0000)
                    throw new NotImplementedException("PropertyBag Error entries outside VB's facility are unmeasured (#4741)");
                return Vb6Value.NewError((ushort)scode);
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
