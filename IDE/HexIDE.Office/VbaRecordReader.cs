using System.Buffers.Binary;
using System.Text;

namespace HexIDE.Office;

internal sealed class VbaRecordReader(byte[] bytes)
{
    private int position;
    public bool AtEnd => position == bytes.Length;
    private ReadOnlySpan<byte> Take(int count)
    {
        if (count < 0 || count > bytes.Length - position)
            throw new OfficeFileException("VBA record runs past the stream end.");
        var result = bytes.AsSpan(position, count);
        position += count;
        return result;
    }
    public ushort PeekId()
    {
        var result = U16();
        position -= 2;
        return result;
    }
    public ushort U16() => BinaryPrimitives.ReadUInt16LittleEndian(Take(2));
    public uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));
    public byte[] Bytes(int count) => Take(count).ToArray();
    public void Id(ushort expected)
    {
        var actual = U16();
        if (actual != expected)
            throw new OfficeFileException($"Unexpected VBA record 0x{actual:X4}; expected 0x{expected:X4}.");
    }
    public void Size(uint expected)
    {
        if (U32() != expected)
            throw new OfficeFileException("Invalid fixed VBA record size.");
    }
    public byte[] SizedBytes()
    {
        var size = U32();
        if (size > int.MaxValue)
            throw new OfficeFileException("VBA record size exceeds the supported limit.");
        return Bytes((int)size);
    }
    public byte[] Record(ushort id)
    {
        Id(id);
        return SizedBytes();
    }
    public uint Number(ushort id)
    {
        Id(id);
        Size(4);
        return U32();
    }
    public ushort ShortNumber(ushort id)
    {
        Id(id);
        Size(2);
        return U16();
    }
    public void ReservedRecord(ushort id)
    {
        Id(id);
        U32();
    }
    public string Unicode(byte[] value)
    {
        if (value.Length % 2 != 0)
            throw new OfficeFileException("Odd byte length in a VBA Unicode record.");
        try { return new UnicodeEncoding(false, false, true).GetString(value); }
        catch (DecoderFallbackException exception)
        { throw new OfficeFileException("Invalid UTF-16 in a VBA record.", exception); }
    }
}
