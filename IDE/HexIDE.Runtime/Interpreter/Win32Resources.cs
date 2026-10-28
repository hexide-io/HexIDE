using System.Text;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Runtime.Interpreter;

// Format: https://learn.microsoft.com/en-us/windows/win32/menurc/resourceheader
// Lookup measurements: https://github.com/hexide-io/HexIDE/issues/2625 (and #2624, #2628).
internal sealed class Win32Resources
{
    private sealed record Entry(object Type, object Name, ushort Language, byte[] Data);
    private readonly List<Entry> entries = [];

    internal static Win32Resources Load(string? path)
    {
        if (path is null) return new();
        try { return Read(File.ReadAllBytes(path)); }
        catch (IOException) { return new(); }
        catch (UnauthorizedAccessException) { return new(); }
        catch (ArgumentException) { return new(); }
        catch (NotSupportedException) { return new(); }
    }

    internal static Win32Resources Read(byte[] bytes)
    {
        var result = new Win32Resources();
        using var stream = new MemoryStream(bytes, false);
        using var reader = new BinaryReader(stream, Encoding.Unicode);
        try
        {
            while (stream.Position < stream.Length)
            {
                var start = stream.Position;
                var size = reader.ReadUInt32();
                var headerSize = reader.ReadUInt32();
                var headerEnd = start + headerSize;
                if (headerSize < 28 || headerSize % 4 != 0 || headerEnd > stream.Length) throw new InvalidDataException();
                var type = Identifier(reader, headerEnd);
                var name = Identifier(reader, headerEnd);
                stream.Position = Align(stream.Position);
                if (stream.Position + 16 > headerEnd) throw new InvalidDataException();
                reader.ReadUInt32(); // DataVersion
                reader.ReadUInt16(); // MemoryFlags
                var language = reader.ReadUInt16();
                reader.ReadUInt32(); // Version
                reader.ReadUInt32(); // Characteristics
                if (size > int.MaxValue || headerEnd + size > stream.Length) throw new InvalidDataException();
                stream.Position = headerEnd;
                var data = reader.ReadBytes((int)size);
                if (!(type is ushort t && t == 0 && name is ushort n && n == 0 && size == 0))
                    result.entries.Add(new(type, name, language, data));
                stream.Position = Align(stream.Position);
            }
            return result;
        }
        catch (EndOfStreamException) { return new(); }
        catch (InvalidDataException) { return new(); }
    }

    private static long Align(long position) => (position + 3) & ~3L;

    private static object Identifier(BinaryReader reader, long end)
    {
        ushort Word()
        {
            if (reader.BaseStream.Position + 2 > end) throw new InvalidDataException();
            return reader.ReadUInt16();
        }
        var first = Word();
        if (first == 0xffff) return Word();
        var text = new StringBuilder();
        for (var word = first; word != 0; word = Word()) text.Append((char)word);
        return text.ToString();
    }

    // Ordinals remain distinct from strings containing digits. Callers perform VB argument coercion.
    internal byte[]? Find(object type, object name)
    {
        Entry? found = null;
        foreach (var entry in entries)
        {
            if (!Same(entry.Type, type) || !Same(entry.Name, name)) continue;
            if (found is not null && found.Language != entry.Language)
                throw new NotImplementedException("Resource language selection is unmeasured (#2625)");
            found ??= entry;
        }
        return found is null ? null : (byte[])found.Data.Clone();
    }

    private static bool Same(object left, object right) => left is string a && right is string b
        ? string.Equals(a, b, StringComparison.OrdinalIgnoreCase) : left.Equals(right);

    internal string? FindString(ushort id)
    {
        var data = Find((ushort)6, (ushort)(id / 16 + 1));
        if (data is null) return null;
        using var stream = new MemoryStream(data, false);
        using var reader = new BinaryReader(stream, Encoding.Unicode);
        string? result = null;
        try
        {
            for (var i = 0; i < 16; i++)
            {
                var length = reader.ReadUInt16();
                if (stream.Position + length * 2 > stream.Length) return null;
                var text = Encoding.Unicode.GetString(reader.ReadBytes(length * 2));
                if (i == id % 16 && length != 0) result = text;
            }
            return result;
        }
        catch (EndOfStreamException) { return null; }
    }

    internal static string? PathFor(ProjectElements.ProjectDefinition project)
    {
        var value = AppInfo.PreSectionValue(project, "ResFile32");
        return project.AbsolutePath is { } path && value is not null
            ? Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, SerializedProject.ToHostPath(value)))
            : null;
    }
}
