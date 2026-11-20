using System.Text;

namespace HexIDE.TwinBasic;

/// <summary>
/// Writes a version 1 container at the output's current position and leaves the stream open.
/// Format validation and cancellation checks finish before any output is written.
/// </summary>
public static class TwinBasicContainerWriter
{
    private const uint Magic = 0xEA0BA51C;
    private static readonly UTF8Encoding Utf8 = new(false, true);

    public static void Write(
        TwinBasicContainer container, Stream output, TwinBasicReadLimits? limits = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(container);
        ArgumentNullException.ThrowIfNull(output);
        limits ??= new();
        if (limits.MaxContainerBytes < 1 || limits.MaxEntries < 1 || limits.MaxDepth < 0 ||
            limits.MaxDepth > 256 || limits.MaxNameBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Positive limits and depth 0..256 are required.");
        cancellationToken.ThrowIfCancellationRequested();

        if (container.Version != 1)
            throw new TwinBasicFormatException(sizeof(uint), "/" + container.Root?.Name,
                $"Unsupported container version {container.Version}");

        new Validator(limits, cancellationToken).Entry(container.Root, 0);
        cancellationToken.ThrowIfCancellationRequested();

        using var writer = new BinaryWriter(output, Utf8, leaveOpen: true);
        writer.Write(Magic);
        WriteEntry(writer, container.Root, container.Version);
    }

    private static void WriteEntry(BinaryWriter writer, TwinBasicEntry entry, short kind)
    {
        var name = Utf8.GetBytes(entry.Name);
        writer.Write(kind);
        writer.Write((uint)name.Length);
        writer.Write(name);
        writer.Write(entry.Revision);
        writer.Write(entry.Flags);
        writer.Write(entry.Category);

        if (entry is TwinBasicFile file)
        {
            writer.Write((uint)file.Content.Length);
            writer.Write(file.Content.AsSpan());
            writer.Write((uint)file.Revisions.Length);
            foreach (var revision in file.Revisions) writer.Write(revision);
        }
        else
        {
            var directory = (TwinBasicDirectory)entry;
            writer.Write((uint)directory.Children.Length);
            foreach (var child in directory.Children)
                WriteEntry(writer, child, child is TwinBasicFile ? (short)1 : (short)2);
        }
    }

    private sealed class Validator(TwinBasicReadLimits limits, CancellationToken cancellationToken)
    {
        private readonly List<string> names = [];
        private long offset = sizeof(uint);
        private int entries;

        public void Entry(TwinBasicEntry? entry, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entry is null) throw Error("Missing entry");

            names.Add(entry.Name);
            if (depth > limits.MaxDepth) throw Error("Entry exceeds nesting limit");
            if (entries == limits.MaxEntries) throw Error("Container exceeds entry limit");
            entries++;

            if (entry.Name is null) throw Error("Missing entry name");
            int nameLength;
            try { nameLength = Utf8.GetByteCount(entry.Name); }
            catch (EncoderFallbackException) { throw Error("Invalid UTF-16 entry name"); }
            if (nameLength > limits.MaxNameBytes) throw Error("Name exceeds byte limit");

            // Kind/version, name length, revision, flags and category.
            long size = sizeof(short) + sizeof(uint) + (long)nameLength + sizeof(ulong) + sizeof(uint) + sizeof(byte);
            switch (entry)
            {
                case TwinBasicFile file:
                    if (file.Content.IsDefault) throw Error("Uninitialised file content");
                    if (file.Revisions.IsDefault) throw Error("Uninitialised file revisions");
                    size += sizeof(uint) + (long)file.Content.Length + sizeof(uint) + (long)file.Revisions.Length * sizeof(uint);
                    break;
                case TwinBasicDirectory directory:
                    if (directory.Children.IsDefault) throw Error("Uninitialised directory children");
                    size += sizeof(uint);
                    break;
                default:
                    throw Error("Unsupported entry type");
            }

            if (size > limits.MaxContainerBytes - offset) throw Error("Container exceeds byte limit");
            offset += size;
            if (entry is TwinBasicDirectory folder)
                foreach (var child in folder.Children) Entry(child, depth + 1);
            names.RemoveAt(names.Count - 1);
        }

        private TwinBasicFormatException Error(string reason) => new(offset, "/" + string.Join("/", names), reason);
    }
}
