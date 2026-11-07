using System.Globalization;

namespace HexIDE.Runtime.Interpreter;

/// <summary>What a <c>Put</c> or <c>Get</c> operand is, as VB6 types it while compiling (#1646).</summary>
internal enum RecordOperandKind
{
    /// <summary>A scalar of a declared or statically known type: Integer to String.</summary>
    Typed,

    /// <summary>A Variant: its type goes first.</summary>
    Variant,

    /// <summary>A user-defined Type.</summary>
    Udt,

    /// <summary>An array, its own element type deciding each element.</summary>
    Array,
}

/// <summary>A <c>Put</c> or <c>Get</c> operand's kind, and for <see cref="RecordOperandKind.Typed"/> its type.</summary>
internal readonly record struct RecordOperand(RecordOperandKind Kind, Vb6Value.ValueType? Type = null);

/// <summary>
/// A <c>Get</c> that failed after reading had begun: VB6 leaves the file where it was and, for some failures, clears the
/// target first (measured, #1646). <see cref="ClearTo"/> is what the target is left holding, or null where it keeps its value.
/// </summary>
internal sealed class RecordGetFailure(VBStandardError error, Vb6Value? clearTo) : Exception(error.Description)
{
    public VBStandardError Error { get; } = error;

    public Vb6Value? ClearTo { get; } = clearTo;
}

/// <summary>A Variant holding an object was <c>Put</c>: VB6 writes its type, 9, then raises 13, the file staying where it was.</summary>
internal sealed class RecordObjectInVariant(byte[] written) : Exception("Type mismatch")
{
    public byte[] Written { get; } = written;
}

/// <summary>A Random file's record ran out before the value did: 59 (measured).</summary>
internal sealed class RecordOverflow() : Exception("Bad record length");

/// <summary>The bytes one <c>Get</c> reads from, in order. Past a Binary file's end they are zeros; past a Random record's end, <see cref="RecordOverflow"/>.</summary>
internal interface IRecordSource
{
    byte[] Take(int count);
}

/// <summary>
/// The bytes <c>Put</c> writes and <c>Get</c> reads, for each kind of operand, as vb6.exe wrote them (#1646; the oracle's
/// "Random and Binary files", native and p-code agreeing on every row):
/// <list type="bullet">
/// <item>Integer 2 bytes, Long 4, Single 4, Double 8, Currency 8 (scaled by 10,000), Date 8 (its serial as a Double),
/// Boolean 2 (True is FF FF), Byte 1, all little-endian. A UDT's fields are packed, with no alignment.</item>
/// <item>A String in the ANSI code page. Alone in a Binary file it is just its bytes; in a Random record, in a UDT, in an
/// array or in a Variant it has a length first: 2 bytes, or FF FF and 4 bytes from 65,535 on.</item>
/// <item>A Variant is its type, 2 bytes, then its value: nothing for Empty (0) and Null (1); Integer 2, Long 3, Single 4,
/// Double 5, Currency 6, Date 7, String 8, Boolean 11, Decimal 14 as the 16 bytes of a DECIMAL whose first two are the
/// type again, Byte 17, and an array 8192 plus its element type, with a descriptor.</item>
/// <item>An array's elements go first index fastest. A descriptor goes before them for an array in a Variant, and for a
/// dynamic array in a Random file: the number of dimensions, 2 bytes, then for each, from the last to the first, its count
/// and its lower bound, 4 bytes each.</item>
/// </list>
/// </summary>
internal static class VbRecordFormat
{
    internal const string ObjectRefused =
        "Can't Get or Put an object reference variable or a variable of user-defined type containing an object reference";

    private const int ArrayFlag = 0x2000;

    /// <summary>The bytes a <c>Put</c> is building.</summary>
    internal sealed class Writer
    {
        private readonly MemoryStream bytes = new();

        public byte[] ToArray() => bytes.ToArray();

        public void U16(int value)
        {
            bytes.WriteByte((byte)value);
            bytes.WriteByte((byte)(value >> 8));
        }

        public void I32(long value)
        {
            for (var i = 0; i < 4; i++)
                bytes.WriteByte((byte)(value >> (8 * i)));
        }

        public void I64(long value)
        {
            for (var i = 0; i < 8; i++)
                bytes.WriteByte((byte)(value >> (8 * i)));
        }

        public void Raw(byte[] raw) => bytes.Write(raw);

        public void Single(float value) => I32(BitConverter.SingleToInt32Bits(value));

        public void Double(double value) => I64(BitConverter.DoubleToInt64Bits(value));
    }

    // ---- Put ----

    /// <summary>A scalar of type <paramref name="type"/>. A String has its length first where <paramref name="prefixed"/>.</summary>
    public static void WriteTyped(Writer w, Vb6Value value, Vb6Value.ValueType type, bool prefixed)
    {
        if (type == Vb6Value.ValueType.String)
        {
            var bytes = VbFiles.Ansi().GetBytes(VbDisplay.ToText(value));
            if (prefixed)
                Length(w, bytes.Length);
            w.Raw(bytes);
            return;
        }
        var typed = VbNumeric.CoerceOnStore(value, type, null);
        if (type == Vb6Value.ValueType.Integer)
            w.U16(Convert.ToInt32(typed.Value, CultureInfo.InvariantCulture));
        else if (type == Vb6Value.ValueType.Long)
            w.I32(Convert.ToInt64(typed.Value, CultureInfo.InvariantCulture));
        else if (type == Vb6Value.ValueType.Single)
            w.Single(Convert.ToSingle(typed.Value, CultureInfo.InvariantCulture));
        else if (type == Vb6Value.ValueType.Double)
            w.Double(Convert.ToDouble(typed.Value, CultureInfo.InvariantCulture));
        else if (type == Vb6Value.ValueType.Currency)
            w.I64(decimal.ToInt64(Convert.ToDecimal(typed.Value, CultureInfo.InvariantCulture) * 10000m));
        else if (type == Vb6Value.ValueType.Date)
            w.Double(((VbDate)typed.Value!).Serial);
        else if (type == Vb6Value.ValueType.Boolean)
            w.U16((bool)typed.Value! ? 0xFFFF : 0);
        else if (type == Vb6Value.ValueType.Byte)
            w.Raw([Convert.ToByte(typed.Value, CultureInfo.InvariantCulture)]);
        else
            throw new NotImplementedException($"Put of a {type} is not implemented (#1646)");
    }

    // A String's length, as a Random record, a UDT, an array or a Variant has it: 2 bytes, or FF FF then 4 from 65,535 on
    // (measured: 40,000 is 40 9C, 65,535 FF FF FF FF 00 00, and 70,000 FF FF 70 11 01 00).
    private static void Length(Writer w, int length)
    {
        if (length < 0xFFFF)
            w.U16(length);
        else
        {
            w.U16(0xFFFF);
            w.I32(length);
        }
    }

    /// <summary>A Variant: its type, then its value.</summary>
    public static void WriteVariant(Writer w, Vb6Value value)
    {
        if (value.Value is VBArray array)
        {
            var elementVt = VariantType(array.ElementType is { } type && type != Vb6Value.ValueType.EmptyVariant ? type : null);
            w.U16(ArrayFlag | elementVt);
            Descriptor(w, array);
            WriteElements(w, array);
            return;
        }
        if (value.Type == Vb6Value.ValueType.Object || value.Type == Vb6Value.ValueType.Nothing
            || value.Type == Vb6Value.ValueType.Control || value.Type == Vb6Value.ValueType.CSharpProxyObject)
        {
            w.U16(9);
            throw new RecordObjectInVariant(w.ToArray());
        }
        if (value.Type == Vb6Value.ValueType.Missing || value.Type == Vb6Value.ValueType.Error)
            throw new NotImplementedException("Put of an Error Variant is not implemented (#2520)");
        if (value.Type == Vb6Value.ValueType.UserDefinedType)
            throw new NotImplementedException("Put of a Variant holding a user-defined Type is not implemented (#1646)");
        var vt = VariantType(value.Type);
        w.U16(vt);
        switch (vt)
        {
            case 0 or 1:
                return;
            case 8:
                WriteTyped(w, value, Vb6Value.ValueType.String, prefixed: true);
                return;
            case 14:
                var bits = decimal.GetBits(Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture));
                w.U16(14);
                w.Raw([(byte)((bits[3] >> 16) & 0xFF), (byte)(bits[3] < 0 ? 0x80 : 0)]);
                w.I32(bits[2]);
                w.I32(bits[0]);
                w.I32(bits[1]);
                return;
            default:
                WriteTyped(w, value, value.Type, prefixed: true);
                return;
        }
    }

    // VB6's VarType numbers, for the types a Variant can hold here; null for a Variant's own (12, an array's elements).
    private static int VariantType(Vb6Value.ValueType? type)
    {
        if (type is null)
            return 12;
        if (type == Vb6Value.ValueType.EmptyVariant) return 0;
        if (type == Vb6Value.ValueType.Null) return 1;
        if (type == Vb6Value.ValueType.Integer) return 2;
        if (type == Vb6Value.ValueType.Long) return 3;
        if (type == Vb6Value.ValueType.Single) return 4;
        if (type == Vb6Value.ValueType.Double) return 5;
        if (type == Vb6Value.ValueType.Currency) return 6;
        if (type == Vb6Value.ValueType.Date) return 7;
        if (type == Vb6Value.ValueType.String) return 8;
        if (type == Vb6Value.ValueType.Boolean) return 11;
        if (type == Vb6Value.ValueType.Decimal) return 14;
        if (type == Vb6Value.ValueType.Byte) return 17;
        throw new NotImplementedException($"Put of a Variant holding a {type} is not implemented (#1646)");
    }

    /// <summary>An array: a descriptor first where <paramref name="descriptor"/>, then the elements.</summary>
    public static void WriteArray(Writer w, VBArray array, bool descriptor)
    {
        if (descriptor)
            Descriptor(w, array);
        WriteElements(w, array);
    }

    // The number of dimensions, then each one's count and lower bound, from the last dimension to the first (measured:
    // Integer(1 To 2, 3 To 5) is 02 00, 03 00 00 00, 03 00 00 00, 02 00 00 00, 01 00 00 00). An array with no bounds yet is
    // 00 00 (measured).
    private static void Descriptor(Writer w, VBArray array)
    {
        w.U16(array.Rank);
        for (var d = array.Rank; d >= 1; d--)
        {
            w.I32(array.Length(d));
            w.I32(array.LowerBound(d));
        }
    }

    private static void WriteElements(Writer w, VBArray array)
    {
        var element = array.ElementType;
        if (element == Vb6Value.ValueType.UserDefinedType)
            throw new NotImplementedException("Get/Put of an array of a user-defined Type is not implemented (#178)");
        if (array.DeclaredClass is not null || element == Vb6Value.ValueType.Object)
            throw new VBCompileErrorException(ObjectRefused);
        foreach (var index in ColumnMajor(array))
        {
            var value = array.GetValue(index);
            if (value.Value is VbUdt udt)
                WriteUdt(w, udt);
            else if (element is null || element == Vb6Value.ValueType.EmptyVariant)
                WriteVariant(w, value);
            else
                WriteTyped(w, value, element, prefixed: true);
        }
    }

    /// <summary>A user-defined Type: its fields in order, packed.</summary>
    public static void WriteUdt(Writer w, VbUdt udt)
    {
        foreach (var (name, value) in udt.EnumerateFields())
        {
            if (value.Value is VBArray)
                throw new NotImplementedException("Get/Put of a Type containing an array field is not implemented (#178)");
            if (value.Value is VbUdt nested)
                WriteUdt(w, nested);
            else if (udt.IsVariantField(name))
                WriteVariant(w, value);
            else if (udt.DeclaredTypeOf(name) is { } type)
                WriteTyped(w, value, type, prefixed: true);
            else
                throw new VBCompileErrorException(ObjectRefused);
        }
    }

    /// <summary>An array's indexes, first index fastest, as VB6 writes its elements (measured for 2 and 3 dimensions).</summary>
    public static IEnumerable<int[]> ColumnMajor(VBArray array)
    {
        if (!array.IsDefined)
            yield break;
        var rank = array.Rank;
        var index = new int[rank];
        for (var d = 0; d < rank; d++)
        {
            if (array.Length(d + 1) <= 0)
                yield break;
            index[d] = array.LowerBound(d + 1);
        }
        while (true)
        {
            yield return (int[])index.Clone();
            var d = 0;
            while (d < rank && ++index[d] > array.UpperBound(d + 1))
            {
                index[d] = array.LowerBound(d + 1);
                d++;
            }
            if (d == rank)
                yield break;
        }
    }

    // ---- Get ----

    /// <summary>
    /// A scalar of type <paramref name="type"/>. A String has its length first where <paramref name="prefixed"/>, and is
    /// otherwise <paramref name="unprefixedLength"/> bytes, as many as the String held (measured: "xy" reads 2).
    /// </summary>
    public static Vb6Value ReadTyped(IRecordSource source, Vb6Value.ValueType type, bool prefixed, int unprefixedLength = 0)
    {
        try
        {
            if (type == Vb6Value.ValueType.String)
            {
                var length = prefixed ? ReadLength(source) : unprefixedLength;
                return new Vb6Value(VbFiles.Ansi().GetString(source.Take(length)));
            }
            if (type == Vb6Value.ValueType.Integer)
                return new Vb6Value((int)BitConverter.ToInt16(source.Take(2)));
            if (type == Vb6Value.ValueType.Long)
                return new Vb6Value((long)BitConverter.ToInt32(source.Take(4)));
            if (type == Vb6Value.ValueType.Single)
                return new Vb6Value(BitConverter.ToSingle(source.Take(4)));
            if (type == Vb6Value.ValueType.Double)
                return new Vb6Value(BitConverter.ToDouble(source.Take(8)));
            if (type == Vb6Value.ValueType.Currency)
                return Vb6Value.NewCurrency(BitConverter.ToInt64(source.Take(8)) / 10000m);
            if (type == Vb6Value.ValueType.Date)
                return ToDate(BitConverter.ToDouble(source.Take(8)));
            if (type == Vb6Value.ValueType.Boolean)
                return ToBoolean(BitConverter.ToUInt16(source.Take(2)));
            if (type == Vb6Value.ValueType.Byte)
                return new Vb6Value(source.Take(1)[0]);
        }
        catch (RecordOverflow)
        {
            // A String the record can't hold is left empty (measured: "" from a length of 10 in a record of 8); a number
            // keeps its value (measured for a Long in a record of 2).
            throw new RecordGetFailure(VBStandardError.BadRecordLength, type == Vb6Value.ValueType.String ? new Vb6Value("") : (Vb6Value?)null);
        }
        throw new NotImplementedException($"Get of a {type} is not implemented (#1646)");
    }

    private static int ReadLength(IRecordSource source)
    {
        var length = BitConverter.ToUInt16(source.Take(2));
        return length == 0xFFFF ? BitConverter.ToInt32(source.Take(4)) : length;
    }

    // VB6 keeps whatever bits a Boolean is given, and 01 00 reads True while CInt gives 1 and `= True` is False (measured).
    // HexIDE's Boolean is True or False, so such bits are refused rather than read as a True that answers differently.
    private static Vb6Value ToBoolean(int bits) => bits switch
    {
        0 => new Vb6Value(false),
        0xFFFF => new Vb6Value(true),
        _ => throw new NotImplementedException(
            $"Get of a Boolean stored as {bits} is not implemented: VB6 keeps the bits, so CInt of it is {(short)bits}, which a HexIDE Boolean can't hold"),
    };

    // VB6 keeps any Double as a Date, years past 9999 included (measured: 9000372404224). Outside 100 to 9999, which is what
    // a HexIDE Date can hold, it is refused rather than read as some other date.
    private static Vb6Value ToDate(double serial)
    {
        if (VbDate.InRange(serial))
            return new Vb6Value(new VbDate(serial));
        throw new NotImplementedException(
            $"Get of a Date whose serial is {serial.ToString(CultureInfo.InvariantCulture)} is not implemented: it is outside the years 100 to 9999");
    }

    /// <summary>
    /// A Variant: its type, then its value. A type 9, an object, is 13 and leaves the target as it was; 13 is 13 and 8209-style
    /// unknown types 458, each leaving it Empty (measured, the file staying where it was in every case).
    /// </summary>
    public static Vb6Value ReadVariant(IRecordSource source)
    {
        try
        {
            var vt = (int)BitConverter.ToUInt16(source.Take(2));
            if ((vt & ArrayFlag) != 0 && (vt & ~ArrayFlag) is var elementVt && ElementTypeOf(elementVt, out var elementType))
                return ReadVariantArray(source, elementType);
            switch (vt)
            {
                case 0:
                    return Vb6Value.Variant;
                case 1:
                    return Vb6Value.Null;
                case 9:
                    throw new RecordGetFailure(VBStandardError.TypeMismatch, null);
                case 10:
                    throw new NotImplementedException("Get of an Error Variant is not implemented (#2520)");
                case 13:
                    throw new RecordGetFailure(VBStandardError.TypeMismatch, Vb6Value.Variant);
                case 14:
                    return ReadDecimal(source.Take(16));
            }
            if (ElementTypeOf(vt, out var type) && type is not null)
                return ReadTyped(source, type, prefixed: true);
            throw new RecordGetFailure(VBStandardError.VariableUsesATypeNotSupportedInVisualBasic, Vb6Value.Variant);
        }
        catch (RecordOverflow)
        {
            throw new RecordGetFailure(VBStandardError.BadRecordLength, Vb6Value.Variant);
        }
        catch (RecordGetFailure failure) when (failure.Error.ErrNo == VBStandardError.BadRecordLength.ErrNo)
        {
            // A String inside the Variant ran past the record: the Variant is left Empty (measured).
            throw new RecordGetFailure(VBStandardError.BadRecordLength, Vb6Value.Variant);
        }
    }

    // The scalar types a Variant or a Variant's array may hold, by VarType; null for 12, a Variant array's elements.
    private static bool ElementTypeOf(int vt, out Vb6Value.ValueType? type)
    {
        type = vt switch
        {
            2 => Vb6Value.ValueType.Integer,
            3 => Vb6Value.ValueType.Long,
            4 => Vb6Value.ValueType.Single,
            5 => Vb6Value.ValueType.Double,
            6 => Vb6Value.ValueType.Currency,
            7 => Vb6Value.ValueType.Date,
            8 => Vb6Value.ValueType.String,
            11 => Vb6Value.ValueType.Boolean,
            17 => Vb6Value.ValueType.Byte,
            _ => null,
        };
        return type is not null || vt == 12;
    }

    // A DECIMAL's 16 bytes: two reserved, the scale, the sign, then the high 32 bits and the low 64 (measured: CDec(1.5) is
    // 0E 00 01 00 00 00 00 00 0F 00 ...). VB6 takes a scale past 28 (measured); a C# decimal can't hold it, so it is refused.
    private static Vb6Value ReadDecimal(byte[] bytes)
    {
        var scale = bytes[2];
        if (scale > 28)
            throw new NotImplementedException($"Get of a Decimal with scale {scale} is not implemented: past 28, which HexIDE's Decimal can hold");
        var hi = BitConverter.ToInt32(bytes, 4);
        var lo = BitConverter.ToInt32(bytes, 8);
        var mid = BitConverter.ToInt32(bytes, 12);
        return Vb6Value.NewDecimal(new decimal(lo, mid, hi, (bytes[3] & 0x80) != 0, scale));
    }

    private static Vb6Value ReadVariantArray(IRecordSource source, Vb6Value.ValueType? elementType)
    {
        var element = elementType ?? Vb6Value.ValueType.EmptyVariant;
        var bounds = ReadDescriptor(source);
        var arrayType = new Vb6Value.ValueType(element, true);
        if (bounds.Count == 0)
            return new Vb6Value(arrayType);
        var value = new Vb6Value(arrayType, bounds);
        ReadElementsInto(source, (VBArray)value.Value!);
        return value;
    }

    /// <summary>A descriptor's bounds, in dimension order: written from the last dimension to the first.</summary>
    public static List<(int lowerBound, int upperBound)> ReadDescriptor(IRecordSource source)
    {
        var rank = BitConverter.ToUInt16(source.Take(2));
        var bounds = new (int lowerBound, int upperBound)[rank];
        for (var d = rank - 1; d >= 0; d--)
        {
            var count = BitConverter.ToInt32(source.Take(4));
            var lower = BitConverter.ToInt32(source.Take(4));
            bounds[d] = (lower, lower + count - 1);
        }
        return [.. bounds];
    }

    /// <summary>An array's elements, read into it in place, first index fastest. A UDT element is read into in place too.</summary>
    public static void ReadElementsInto(IRecordSource source, VBArray array)
    {
        var element = array.ElementType;
        if (element == Vb6Value.ValueType.UserDefinedType)
            throw new NotImplementedException("Get/Put of an array of a user-defined Type is not implemented (#178)");
        if (array.DeclaredClass is not null || element == Vb6Value.ValueType.Object)
            throw new VBCompileErrorException(ObjectRefused);
        foreach (var index in ColumnMajor(array))
        {
            var current = array.GetValue(index);
            try
            {
                if (current.Value is VbUdt udt)
                    ReadUdtInto(source, udt);
                else if (element is null || element == Vb6Value.ValueType.EmptyVariant)
                    array.SetValue(index, ReadVariant(source));
                else
                    array.SetValue(index, ReadTyped(source, element, prefixed: true));
            }
            catch (RecordGetFailure failure)
            {
                // As a UDT's field does: the element that failed takes what the failure leaves, and the array stays.
                if (failure.ClearTo is { } cleared)
                    array.SetValue(index, cleared);
                throw new RecordGetFailure(failure.Error, null);
            }
        }
    }

    /// <summary>
    /// A user-defined Type's fields, read into it in place and in order. A field that fails stops it there, the fields before
    /// it keeping what was read (measured: a Type of a Byte and a Long, in a record of 2, takes the Byte and keeps the Long).
    /// </summary>
    public static void ReadUdtInto(IRecordSource source, VbUdt udt)
    {
        foreach (var (name, current) in udt.EnumerateFields().ToList())
        {
            if (current.Value is VBArray)
                throw new NotImplementedException("Get/Put of a Type containing an array field is not implemented (#178)");
            try
            {
                if (current.Value is VbUdt nested)
                    ReadUdtInto(source, nested);
                else if (udt.IsVariantField(name))
                    udt.TrySet(name, ReadVariant(source));
                else if (udt.DeclaredTypeOf(name) is { } type)
                    udt.TrySet(name, ReadTyped(source, type, prefixed: true));
                else
                    throw new VBCompileErrorException(ObjectRefused);
            }
            catch (RecordGetFailure failure)
            {
                if (failure.ClearTo is { } cleared)
                    udt.TrySet(name, cleared);
                throw new RecordGetFailure(failure.Error, null);
            }
        }
    }
}
