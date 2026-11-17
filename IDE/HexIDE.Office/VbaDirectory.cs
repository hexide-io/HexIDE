using System.Text;

namespace HexIDE.Office;

public sealed record VbaModuleRecord(byte[] NameBytes, string? NameUnicode, byte[] StreamNameBytes,
    string StreamNameUnicode, uint SourceOffset, ushort ModuleType, bool IsReadOnly, bool IsPrivate,
    string DocString, uint HelpContext);

/// <summary>The decompressed dir stream (MS-OVBA 2.3.4.2).</summary>
public sealed record VbaDirectory(VbaProjectInformation Information, IReadOnlyList<VbaReference> References,
    IReadOnlyList<VbaModuleRecord> Modules)
{
    /// <summary>Reads the records, decoding strings that have no Unicode form with <paramref name="codePage"/>, or with the
    /// code page the stream declares when it is null.</summary>
    public static VbaDirectory Read(byte[] decompressed, int? codePage = null)
    {
        var reader = new VbaRecordReader(decompressed);
        var systemKind = reader.Number(1);
        uint? compatibility = reader.PeekId() == 0x4A ? reader.Number(0x4A) : null;
        var lcid = reader.Number(2);
        var invoke = reader.Number(0x14);
        var declaredCodePage = reader.ShortNumber(3);
        var encoding = EncodingFor(codePage ?? declaredCodePage);
        string Text(byte[] value) => encoding.GetString(value);
        string Pair(ushort id, ushort unicodeId)
        {
            reader.Record(id);
            return reader.Unicode(reader.Record(unicodeId));
        }
        var name = Text(reader.Record(4));
        var docString = Pair(5, 0x40);
        var help = Text(reader.Record(6));
        reader.Record(0x3D);
        var context = reader.Number(7);
        var flags = reader.Number(8);
        reader.Id(9);
        reader.U32(); // PROJECTVERSION's reserved value is not its payload length.
        var major = reader.U32();
        var minor = reader.U16();
        var constants = reader.PeekId() == 0xC ? Pair(0xC, 0x3C) : "";
        var information = new VbaProjectInformation(systemKind, compatibility, lcid, invoke, declaredCodePage,
            name, docString, help, context, flags, major, minor, constants);
        var references = new List<VbaReference>();
        while (reader.PeekId() != 0xF)
        {
            string? referenceName = reader.PeekId() == 0x16 ? Pair(0x16, 0x3E) : null;
            string? original = null;
            if (reader.PeekId() == 0x33)
                original = Text(reader.Record(0x33));
            var id = reader.U16();
            reader.U32(); // Reference aggregate sizes are explicitly ignored on read.
            var libid = Text(reader.SizedBytes());
            switch (id)
            {
                case 0xD when original == null:
                    reader.U32();
                    reader.U16();
                    references.Add(new VbaReference(referenceName, VbaReferenceKind.Registered, libid));
                    break;
                case 0xE when original == null:
                    var relative = Text(reader.SizedBytes());
                    references.Add(new VbaReference(referenceName, VbaReferenceKind.Project, libid,
                        relative, reader.U32(), reader.U16()));
                    break;
                case 0x2F:
                    reader.U32();
                    reader.U16();
                    var extendedName = reader.PeekId() == 0x16 ? Pair(0x16, 0x3E) : null;
                    reader.U16(); // Reserved3 (0x0030).
                    reader.U32();
                    var extended = Text(reader.SizedBytes());
                    reader.U32();
                    reader.U16();
                    var typeLibrary = new Guid(reader.Bytes(16));
                    references.Add(new VbaReference(referenceName, VbaReferenceKind.Control, libid,
                        OriginalLibid: original, ExtendedLibid: extended, ExtendedName: extendedName,
                        OriginalTypeLibrary: typeLibrary, Cookie: reader.U32()));
                    break;
                default:
                    throw new OfficeFileException($"Unsupported VBA reference record 0x{id:X4}.");
            }
        }
        var count = reader.ShortNumber(0xF);
        reader.ShortNumber(0x13); // Project cookie.
        var modules = new List<VbaModuleRecord>();
        for (var i = 0; i < count; ++i)
        {
            var nameBytes = reader.Record(0x19);
            var nameUnicode = reader.PeekId() == 0x47 ? reader.Unicode(reader.Record(0x47)) : null;
            var streamBytes = reader.Record(0x1A);
            var streamUnicode = reader.Unicode(reader.Record(0x32));
            var moduleDoc = Pair(0x1C, 0x48);
            var offset = reader.Number(0x31);
            var helpContext = reader.Number(0x1E);
            reader.ShortNumber(0x2C);
            var type = reader.U16();
            if (type is not (0x21 or 0x22))
                throw new OfficeFileException("Invalid VBA module type record.");
            reader.U32();
            var readOnly = reader.PeekId() == 0x25;
            if (readOnly)
                reader.ReservedRecord(0x25);
            var isPrivate = reader.PeekId() == 0x28;
            if (isPrivate)
                reader.ReservedRecord(0x28);
            reader.ReservedRecord(0x2B);
            modules.Add(new VbaModuleRecord(nameBytes, nameUnicode, streamBytes, streamUnicode, offset,
                type, readOnly, isPrivate, moduleDoc, helpContext));
        }
        reader.ReservedRecord(0x10);
        if (!reader.AtEnd)
            throw new OfficeFileException("Unexpected bytes after the VBA dir terminator.");
        return new VbaDirectory(information, references.AsReadOnly(), modules.AsReadOnly());
    }

    /// <summary>Reads the records with the code page the module names show they were written in
    /// (<see cref="SelectSourceCodePage"/>). The project's name, its help file and reference paths have no Unicode form, and
    /// were written in that code page too, which a project saved on a machine of another ANSI code page doesn't declare.</summary>
    public static (VbaDirectory Directory, int CodePage) ReadWithMeasuredCodePage(byte[] decompressed)
    {
        var directory = Read(decompressed);
        var codePage = directory.SelectSourceCodePage();
        return (codePage == directory.Information.DeclaredCodePage ? directory : Read(decompressed, codePage), codePage);
    }

    internal static Encoding EncodingFor(int codePage)
    {
        try
        {
            return CodePagesEncodingProvider.Instance.GetEncoding(codePage) ??
                throw new OfficeFileException($"Unsupported VBA code page {codePage}.");
        }
        catch (ArgumentException exception)
        {
            throw new OfficeFileException($"Unsupported VBA code page {codePage}.", exception);
        }
    }

    public int SelectSourceCodePage()
    {
        bool Matches(int codePage)
        {
            var encoding = CodePagesEncodingProvider.Instance.GetEncoding(codePage,
                EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            if (encoding == null)
                return false;
            try
            {
                return Modules.All(m => m.NameUnicode == null || encoding.GetString(m.NameBytes) == m.NameUnicode);
            }
            catch (DecoderFallbackException) { return false; }
        }
        if (Matches(Information.DeclaredCodePage))
            return Information.DeclaredCodePage;
        foreach (var codePage in new[] { 874, 932, 936, 949, 950, 1250, 1251, 1252, 1253, 1254, 1255, 1256, 1257, 1258 })
            if (Matches(codePage))
                return codePage;
        throw new OfficeFileException("No supported Windows ANSI code page matches the VBA module names.");
    }
}
