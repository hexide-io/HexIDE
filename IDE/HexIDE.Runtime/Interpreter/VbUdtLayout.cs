namespace HexIDE.Runtime.Interpreter;

/// <summary>The in-memory layout used by LSet, measured in #2518 (native and p-code).
/// Unlike the packed file format, fields align to their size capped at four; fixed Strings are UTF-16.</summary>
internal static class VbUdtLayout
{
    private sealed record Part(int Size, int Alignment, Action<byte[], int> Write, Action<byte[], int> Read);

    public static VbUdt Overlay(VbUdt target, VbUdt source)
    {
        var copy = target.DeepClone();
        var destination = Record(copy);
        var origin = Record(source);
        var bytes = new byte[destination.Size];
        var incoming = new byte[origin.Size];
        destination.Write(bytes, 0);
        origin.Write(incoming, 0);
        Array.Copy(incoming, bytes, Math.Min(bytes.Length, incoming.Length));
        destination.Read(bytes, 0);
        return copy;
    }

    private static int Align(int size, int alignment) => (size + alignment - 1) / alignment * alignment;

    private static Part Sequence(IEnumerable<Part> parts)
    {
        var fields = new List<(int Offset, Part Part)>();
        var size = 0;
        var alignment = 1;
        foreach (var part in parts)
        {
            size = Align(size, part.Alignment);
            fields.Add((size, part));
            size = checked(size + part.Size);
            alignment = Math.Max(alignment, part.Alignment);
        }
        return new Part(Align(size, alignment), alignment,
            (bytes, start) => { foreach (var (offset, part) in fields) part.Write(bytes, start + offset); },
            (bytes, start) => { foreach (var (offset, part) in fields) part.Read(bytes, start + offset); });
    }

    private static Part Record(VbUdt udt)
    {
        var fields = Sequence(Fields(udt));
        return new Part(fields.Size, fields.Alignment,
            (bytes, offset) =>
            {
                Array.Clear(bytes, offset, fields.Size);
                if (udt.LsetBytes is { } previous)
                    previous.CopyTo(bytes, offset);
                fields.Write(bytes, offset);
            },
            (bytes, offset) =>
            {
                fields.Read(bytes, offset);
                udt.LsetBytes = bytes.AsSpan(offset, fields.Size).ToArray();
            });
    }

    private static IEnumerable<Part> Fields(VbUdt udt)
    {
        foreach (var (name, value) in udt.EnumerateFields())
        {
            if (udt.IsVariantField(name) || udt.IsObjectField(name))
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            yield return Value(value, udt.DeclaredTypeOf(name), udt.FixedStringWidthOf(name),
                item => udt.TrySet(name, item));
        }
    }

    private static Part Value(Vb6Value value, Vb6Value.ValueType? type, int? width, Action<Vb6Value> store)
    {
        if (value.Value is VbUdt nested)
            return Record(nested);
        if (value.Value is VBArray array)
        {
            if (array.IsDynamic || array.DeclaredClass is not null || array.ElementType == Vb6Value.ValueType.EmptyVariant)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            return Sequence(VbRecordFormat.ColumnMajor(array).Select(index =>
                Value(array.GetValue(index), array.ElementType, array.FixedStringWidth, item => array.SetValue(index, item))));
        }
        if (type == Vb6Value.ValueType.String)
        {
            if (width is not { } count)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            return new Part(checked(count * 2), 2,
                (bytes, offset) =>
                {
                    var text = (string)value.Value!;
                    for (var i = 0; i < count; i++)
                    {
                        bytes[offset + i * 2] = (byte)text[i];
                        bytes[offset + i * 2 + 1] = (byte)(text[i] >> 8);
                    }
                },
                (bytes, offset) =>
                {
                    var text = new char[count];
                    for (var i = 0; i < count; i++)
                        text[i] = (char)(bytes[offset + i * 2] | bytes[offset + i * 2 + 1] << 8);
                    store(new Vb6Value(new string(text)));
                });
        }
        if (type is null || type == Vb6Value.ValueType.EmptyVariant || type == Vb6Value.ValueType.Object)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        var writer = new VbRecordFormat.Writer();
        VbRecordFormat.WriteTyped(writer, value, type, prefixed: false);
        var raw = writer.ToArray();
        return new Part(raw.Length, Math.Min(raw.Length, 4),
            (bytes, offset) => raw.CopyTo(bytes, offset),
            (bytes, offset) =>
            {
                if (type == Vb6Value.ValueType.Boolean && (bytes[offset] | bytes[offset + 1] << 8) is var bits
                    && bits != 0 && bits != 65535)
                    throw new NotImplementedException("LSet of a noncanonical Boolean is not implemented (#2518): VB6 keeps all 16 bits");
                store(VbRecordFormat.ReadTyped(new Source(bytes, offset), type, prefixed: false));
            });
    }

    private sealed class Source(byte[] bytes, int offset) : IRecordSource
    {
        public byte[] Take(int count)
        {
            var result = bytes.AsSpan(offset, count).ToArray();
            offset += count;
            return result;
        }
    }
}
