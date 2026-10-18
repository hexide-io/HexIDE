using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace HexIDE.TwinBasic;

/// <summary>
/// Reads the documented version 1 container. No extraction, compiler or filesystem writes.
/// The input is read from its current position to EOF and left open, including on failure.
/// Offsets are relative to that starting position. The result never retains the input stream.
/// </summary>
public static class TwinBasicContainerReader
{
    public static TwinBasicContainer Read(
        Stream input, TwinBasicReadLimits? limits = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        limits ??= new();
        if (limits.MaxContainerBytes < 1 || limits.MaxEntries < 1 || limits.MaxDepth < 0 ||
            limits.MaxDepth > 256 || limits.MaxNameBytes < 1)
            throw new ArgumentOutOfRangeException(nameof(limits), "Positive limits and depth 0..256 are required.");
        cancellationToken.ThrowIfCancellationRequested();

        // Works for non-seekable and short-read streams too. Read at most one byte past the configured
        // bound, without trusting Stream.Length or any counts in the archive for an allocation.
        using var snapshot = new MemoryStream();
        var buffer = new byte[Math.Min(64 * 1024, limits.MaxContainerBytes)];
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var requested = (int)Math.Min(buffer.Length, (long)limits.MaxContainerBytes - snapshot.Length + 1);
            var count = input.Read(buffer, 0, requested);
            if (count == 0) break;
            if (snapshot.Length + count > limits.MaxContainerBytes)
                throw new TwinBasicFormatException(snapshot.Length + count, "/", "Container exceeds byte limit");
            snapshot.Write(buffer, 0, count);
        }
        return new Parser(snapshot.ToArray(), limits, cancellationToken).Parse();
    }

    private sealed class Parser(byte[] bytes, TwinBasicReadLimits limits, CancellationToken cancellationToken)
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private readonly List<string> names = [];
        private int offset;
        private int entries;

        public TwinBasicContainer Parse()
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (U32() != 0xEA0BA51C) Fail("Invalid container magic", 0);
            var root = (TwinBasicDirectory)Entry(0);
            cancellationToken.ThrowIfCancellationRequested();
            if (offset != bytes.Length) Fail("Unexpected trailing bytes");
            return new TwinBasicContainer(1, root);
        }

        private TwinBasicEntry Entry(int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > limits.MaxDepth) Fail("Entry exceeds nesting limit");
            if (++entries > limits.MaxEntries) Fail("Container exceeds entry limit");
            var headerOffset = offset;
            var kind = BinaryPrimitives.ReadInt16LittleEndian(Take(2));
            if (depth == 0)
            {
                if (kind != 1) Fail($"Unsupported container version {kind}", headerOffset);
                kind = 2; // The root field is a version, even though 1 means file elsewhere.
            }
            else if (kind is not (1 or 2)) Fail($"Unknown entry kind {kind}", headerOffset);

            var nameLength = Length(limits.MaxNameBytes, "Name");
            var nameOffset = offset;
            string name;
            try { name = Utf8.GetString(Take(nameLength)); }
            catch (DecoderFallbackException) { throw Error("Invalid UTF-8 entry name", nameOffset); }
            names.Add(name);
            try
            {
                var revision = BinaryPrimitives.ReadUInt64LittleEndian(Take(8));
                var flags = U32();
                var category = Take(1)[0];
                if (kind == 1)
                {
                    var length = Length(limits.MaxContainerBytes, "Content");
                    var content = ImmutableArray.CreateRange(Take(length).ToArray());
                    var count = U32();
                    if (count > (bytes.Length - offset) / 4) Fail("Truncated revision trailer");
                    var revisions = ImmutableArray.CreateBuilder<uint>((int)count);
                    for (var i = 0; i < count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        revisions.Add(U32());
                    }
                    return new TwinBasicFile(name, revision, flags, category, content, revisions.MoveToImmutable());
                }

                var childrenCount = U32();
                if (childrenCount > limits.MaxEntries - entries) Fail("Children exceed entry limit");
                // Even an empty-name, empty directory takes 23 bytes. Reject impossible counts before
                // creating a builder; nested entries are checked against the same global entry budget.
                if (childrenCount > (bytes.Length - offset) / 23) Fail("Truncated children");
                var children = ImmutableArray.CreateBuilder<TwinBasicEntry>((int)childrenCount);
                for (var i = 0; i < childrenCount; i++) children.Add(Entry(depth + 1));
                return new TwinBasicDirectory(name, revision, flags, category, children.MoveToImmutable());
            }
            finally { names.RemoveAt(names.Count - 1); }
        }

        private int Length(int maximum, string field)
        {
            var start = offset;
            var value = U32();
            if (value > maximum) Fail($"{field} exceeds byte limit", start);
            if (value > bytes.Length - offset) Fail($"Truncated {field.ToLowerInvariant()}", start);
            return (int)value;
        }

        private uint U32() => BinaryPrimitives.ReadUInt32LittleEndian(Take(4));

        private ReadOnlySpan<byte> Take(int count)
        {
            if (count > bytes.Length - offset) Fail("Truncated container");
            var result = bytes.AsSpan(offset, count);
            offset += count;
            return result;
        }

        private TwinBasicFormatException Error(string reason, int? at = null) =>
            new(at ?? offset, "/" + string.Join("/", names), reason);

        private void Fail(string reason, int? at = null) => throw Error(reason, at);
    }
}
