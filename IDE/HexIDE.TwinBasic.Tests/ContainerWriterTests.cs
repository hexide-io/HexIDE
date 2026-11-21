using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Text;

namespace HexIDE.TwinBasic.Tests;

public sealed class ContainerWriterTests
{
    public static IEnumerable<TheoryDataRow<string>> Fixtures()
    {
        var folder = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        var files = Directory.GetFiles(folder, "*.twinproj", SearchOption.AllDirectories)
            .Concat(Directory.GetFiles(folder, "*.twinpack", SearchOption.AllDirectories))
            .Order(StringComparer.Ordinal).ToArray();
        files.Should().NotBeEmpty();
        return files.Select(path => new TheoryDataRow<string>(Path.GetRelativePath(folder, path)));
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_fixture_round_trips_byte_for_byte(string fixture)
    {
        var bytes = FixtureBytes(fixture);
        Write(Read(bytes)).Should().Equal(bytes);
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Editing_each_file_changes_only_its_content_length_and_content(string fixture)
    {
        var container = Read(FixtureBytes(fixture));
        var original = Write(container);
        using var reader = new BinaryReader(new MemoryStream(original));
        reader.ReadUInt32(); // Magic.
        var files = FileOffsets(reader, container.Root, []).ToArray();
        reader.BaseStream.Position.Should().Be(original.Length);

        foreach (var (path, offset, length) in files)
        {
            var replacement = new byte[length + 1];
            Array.Fill(replacement, (byte)0xFF);
            var edited = container with { Root = ReplaceContent(container.Root, path, [.. replacement]) };
            var actual = Write(edited);
            actual.Length.Should().Be(original.Length + 1);
            actual[..offset].Should().Equal(original[..offset]);
            BinaryPrimitives.ReadUInt32LittleEndian(actual.AsSpan(offset)).Should().Be((uint)replacement.Length);
            var contentOffset = offset + sizeof(uint);
            actual[contentOffset..(contentOffset + replacement.Length)].Should().Equal(replacement);
            actual[(contentOffset + replacement.Length)..].Should().Equal(original[(contentOffset + length)..]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Synthetic_containers_round_trip_byte_for_byte(bool emptyRoot)
    {
        var bytes = Archive(emptyRoot);
        Write(Read(bytes)).Should().Equal(bytes);
    }

    [Theory]
    [InlineData((short)0)]
    [InlineData((short)2)]
    public void Refuses_unsupported_versions(short version)
    {
        Refuses(new(version, Root()), null, "/root", "version");
    }

    [Theory]
    [InlineData("kind", "Unsupported entry type")]
    [InlineData("content", "content")]
    [InlineData("revisions", "revisions")]
    [InlineData("children", "children")]
    [InlineData("name", "UTF-16")]
    public void Refuses_invalid_entries_before_emitting_the_earlier_entries(string invalid, string reason)
    {
        var file = SampleFile();
        TwinBasicEntry bad = invalid switch
        {
            "kind" => new UnknownEntry(),
            "content" => file with { Content = default },
            "revisions" => file with { Revisions = default },
            "children" => Root() with { Name = "file", Children = default },
            "name" => file with { Name = "file\uD800" },
            _ => throw new ArgumentOutOfRangeException(nameof(invalid))
        };
        Refuses(new(1, Root(file with { Name = "first" }, bad)), null, "/root/" + bad.Name, reason);
    }

    [Fact]
    public void Refuses_missing_entries_and_names()
    {
        Refuses(new(1, null!), null, "/", "Missing entry");
        Refuses(new(1, Root(SampleFile(), null!)), null, "/root", "Missing entry");
        Refuses(new(1, Root(SampleFile() with { Name = null! })), null, "/root/", "Missing entry name");
    }

    [Theory]
    [InlineData("bytes", "byte limit")]
    [InlineData("entries", "entry limit")]
    [InlineData("depth", "nesting limit")]
    [InlineData("name", "Name exceeds")]
    public void Refuses_output_beyond_each_reader_limit(string limit, string reason)
    {
        var container = new TwinBasicContainer(1, Root(SampleFile() with { Name = "ééé" }));
        var size = Write(container).Length;
        var boundary = new TwinBasicReadLimits(MaxContainerBytes: size, MaxEntries: 2, MaxDepth: 1, MaxNameBytes: 6);
        var bytes = Write(container, boundary);
        Read(bytes, boundary).Root.Children.Should().ContainSingle();
        var limits = limit switch
        {
            "bytes" => boundary with { MaxContainerBytes = size - 1 },
            "entries" => boundary with { MaxEntries = 1 },
            "depth" => boundary with { MaxDepth = 0 },
            "name" => boundary with { MaxNameBytes = 5 },
            _ => throw new ArgumentOutOfRangeException(nameof(limit))
        };
        Refuses(container, limits, "/root/ééé", reason);
    }

    [Fact]
    public void Enforces_the_default_limits()
    {
        var name = new string('a', new TwinBasicReadLimits().MaxNameBytes + 1);
        Refuses(new(1, Root(SampleFile() with { Name = name })), null, "/root/" + name, "Name exceeds");
    }

    [Theory]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 0, 0, 1)]
    [InlineData(1, 1, -1, 1)]
    [InlineData(1, 1, 257, 1)]
    [InlineData(1, 1, 0, 0)]
    public void Refuses_invalid_limit_settings(int bytes, int entries, int depth, int nameBytes)
    {
        using var output = new MemoryStream();
        var limits = new TwinBasicReadLimits(bytes, entries, depth, nameBytes);
        FluentActions.Invoking(() => TwinBasicContainerWriter.Write(new(1, Root()), output, limits,
            TestContext.Current.CancellationToken)).Should().ThrowExactly<ArgumentOutOfRangeException>();
        output.CanWrite.Should().BeTrue();
        output.Length.Should().Be(0);
    }

    [Fact]
    public void Writes_from_the_current_position_and_leaves_the_stream_open()
    {
        var bytes = Archive(emptyRoot: true);
        using var output = new MemoryStream();
        output.WriteByte(42);
        TwinBasicContainerWriter.Write(Read(bytes), output, cancellationToken: TestContext.Current.CancellationToken);
        output.CanWrite.Should().BeTrue();
        output.Position.Should().Be(bytes.Length + 1);
        output.ToArray().Should().Equal(new byte[] { 42 }.Concat(bytes));
    }

    [Fact]
    public void Writes_to_a_non_seekable_stream()
    {
        var bytes = Archive(emptyRoot: false);
        using var buffer = new MemoryStream();
        using var output = new NonSeekableStream(buffer);
        TwinBasicContainerWriter.Write(Read(bytes), output, cancellationToken: TestContext.Current.CancellationToken);
        output.CanWrite.Should().BeTrue();
        buffer.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public void Cancellation_before_writing_leaves_the_stream_empty_and_open()
    {
        using var output = new MemoryStream();
        FluentActions.Invoking(() => TwinBasicContainerWriter.Write(new(1, Root()), output,
            cancellationToken: new CancellationToken(true))).Should().ThrowExactly<OperationCanceledException>();
        output.CanWrite.Should().BeTrue();
        output.Length.Should().Be(0);
    }

    private static void Refuses(TwinBasicContainer container, TwinBasicReadLimits? limits, string path, string reason)
    {
        using var output = new MemoryStream();
        var error = FluentActions.Invoking(() => TwinBasicContainerWriter.Write(container, output, limits,
            TestContext.Current.CancellationToken)).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.EntryPath.Should().Be(path);
        error.Message.Should().Contain(reason);
        output.CanWrite.Should().BeTrue();
        output.Length.Should().Be(0);
    }

    private static byte[] FixtureBytes(string fixture) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture));

    private static TwinBasicContainer Read(byte[] bytes, TwinBasicReadLimits? limits = null)
    {
        using var input = new MemoryStream(bytes, writable: false);
        return TwinBasicContainerReader.Read(input, limits, TestContext.Current.CancellationToken);
    }

    private static byte[] Write(TwinBasicContainer container, TwinBasicReadLimits? limits = null)
    {
        using var output = new MemoryStream();
        TwinBasicContainerWriter.Write(container, output, limits, TestContext.Current.CancellationToken);
        return output.ToArray();
    }

    private static IEnumerable<(int[] Path, int Offset, int Length)> FileOffsets(
        BinaryReader reader, TwinBasicEntry entry, int[] path)
    {
        reader.ReadInt16(); // Version or kind.
        var nameLength = reader.ReadUInt32();
        reader.BaseStream.Position += nameLength + sizeof(ulong) + sizeof(uint) + sizeof(byte);
        var offset = (int)reader.BaseStream.Position;
        var count = (int)reader.ReadUInt32();
        if (entry is TwinBasicDirectory directory)
        {
            count.Should().Be(directory.Children.Length);
            for (var i = 0; i < count; i++)
                foreach (var file in FileOffsets(reader, directory.Children[i], [.. path, i]))
                    yield return file;
        }
        else
        {
            reader.BaseStream.Position += count;
            var revisions = reader.ReadUInt32();
            reader.BaseStream.Position += revisions * sizeof(uint);
            yield return (path, offset, count);
        }
    }

    private static TwinBasicDirectory ReplaceContent(
        TwinBasicDirectory directory, ReadOnlySpan<int> path, ImmutableArray<byte> content)
    {
        var child = directory.Children[path[0]];
        var replacement = child is TwinBasicDirectory nested
            ? (TwinBasicEntry)ReplaceContent(nested, path[1..], content)
            : ((TwinBasicFile)child) with { Content = content };
        return directory with { Children = directory.Children.SetItem(path[0], replacement) };
    }

    private static byte[] Archive(bool emptyRoot)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
        writer.Write(0xEA0BA51Cu);
        Header(writer, 1, "");
        writer.Write(emptyRoot ? 0u : 3u);
        if (!emptyRoot)
        {
            Header(writer, 2, "空");
            writer.Write(0u);
            Header(writer, 1, "");
            writer.Write(0u);
            writer.Write(0u);
            Header(writer, 1, "", ulong.MaxValue, 0x80000007, 255);
            writer.Write(2u);
            writer.Write(new byte[] { 0xC3, 0x28 });
            writer.Write(2u);
            writer.Write(uint.MaxValue);
            writer.Write(42u);
        }
        return output.ToArray();
    }

    private static void Header(BinaryWriter writer, short kind, string name, ulong revision = 0, uint flags = 0, byte category = 0)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        writer.Write(kind);
        writer.Write((uint)nameBytes.Length);
        writer.Write(nameBytes);
        writer.Write(revision);
        writer.Write(flags);
        writer.Write(category);
    }

    private static TwinBasicDirectory Root(params TwinBasicEntry[] children) => new("root", 0, 0, 0, [.. children]);

    private static TwinBasicFile SampleFile() => new("file", 0, 0, 0, [1, 2, 3], [42]);

    private sealed record UnknownEntry() : TwinBasicEntry("file", 0, 0, 0);

    private sealed class NonSeekableStream(MemoryStream output) : Stream
    {
        private bool disposed;

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => !disposed;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => output.Flush();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            disposed = true;
            base.Dispose(disposing);
        }
    }
}
