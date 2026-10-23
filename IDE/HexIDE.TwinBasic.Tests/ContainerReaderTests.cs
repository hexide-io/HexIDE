using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class ContainerReaderTests
{
    [Theory]
    [InlineData("impexp.twinproj", "f9b9c3241b2534f90b60e9b4f4596a4dab0acac83e59a0ec443829cde625fac5")]
    [InlineData("sample.twinpack", "4afa8819602355d1f95e076c26fbac7a4e1f2fe392d9e2029cbe9a3aabe76ce8")]
    public void Published_fixtures_match_the_independent_parser(string fixture, string hash)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", fixture);
        var bytes = File.ReadAllBytes(path);
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(hash);
        using var input = new MemoryStream(bytes, writable: false);
        var container = TwinBasicContainerReader.Read(input, cancellationToken: TestContext.Current.CancellationToken);
        container.Version.Should().Be(1);
        using var expected = JsonDocument.Parse(File.ReadAllText(path + ".json"));
        var entries = Walk(container.Root, "").ToArray();
        entries.Length.Should().Be(expected.RootElement.GetArrayLength());
        for (var i = 0; i < entries.Length; i++)
        {
            var (entry, entryPath) = entries[i];
            var row = expected.RootElement[i];
            entryPath.Should().Be(row.GetProperty("path").GetString());
            entry.Name.Should().Be(row.GetProperty("name").GetString());
            entry.Revision.Should().Be(row.GetProperty("revision").GetUInt64());
            entry.Flags.Should().Be(row.GetProperty("flags").GetUInt32());
            entry.Category.Should().Be(row.GetProperty("category").GetByte());
            (entry is TwinBasicFile ? "file" : "directory").Should().Be(row.GetProperty("kind").GetString());
            if (entry is not TwinBasicFile file) continue;
            file.Content.Length.Should().Be(row.GetProperty("length").GetInt32());
            Convert.ToHexStringLower(SHA256.HashData(file.Content.AsSpan())).Should().Be(row.GetProperty("sha256").GetString());
            file.Revisions.Should().Equal(row.GetProperty("revisions").EnumerateArray().Select(x => x.GetUInt32()));
        }
        input.CanRead.Should().BeTrue();
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    private static IEnumerable<(TwinBasicEntry Entry, string Path)> Walk(TwinBasicEntry entry, string path)
    {
        yield return (entry, path);
        if (entry is TwinBasicDirectory directory)
            foreach (var child in directory.Children)
                foreach (var item in Walk(child, path.Length == 0 ? child.Name : path + "/" + child.Name))
                    yield return item;
    }

    [Fact]
    public void Preserves_unicode_order_empty_folders_unknown_metadata_and_full_width_revisions()
    {
        var bytes = Archive(w =>
        {
            Header(w, 2, "空"); w.Write(0u);
            Header(w, 1, "é.twin", ulong.MaxValue, 0x80000007, 255);
            Blob(w, [0, 255, 0xC3, 0x28]); // Opaque content need not be UTF-8.
            w.Write(2u); w.Write(uint.MaxValue); w.Write(42u);
        }, children: 2);
        var root = Read(bytes).Root;
        root.Children[0].Should().BeOfType<TwinBasicDirectory>().Which.Children.Should().BeEmpty();
        var file = root.Children[1].Should().BeOfType<TwinBasicFile>().Which;
        file.Name.Should().Be("é.twin");
        file.Revision.Should().Be(ulong.MaxValue);
        file.Flags.Should().Be(0x80000007u);
        file.Category.Should().Be(255);
        file.Content.Should().Equal(new byte[] { 0, 255, 0xC3, 0x28 });
        file.Revisions.Should().Equal(new uint[] { uint.MaxValue, 42 });
    }

    [Fact]
    public void Every_truncation_of_a_valid_container_is_refused()
    {
        var bytes = Archive(FileEntry);
        for (var length = 0; length < bytes.Length; length++)
        {
            using var input = new MemoryStream(bytes[..length]);
            var error = FluentActions.Invoking(() => TwinBasicContainerReader.Read(input, cancellationToken: TestContext.Current.CancellationToken)).Should().ThrowExactly<TwinBasicFormatException>().Which;
            error.Offset.Should().BeInRange(0, length);
            input.CanRead.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(0, 0, "magic")]
    [InlineData(4, 2, "version")]
    [InlineData(27, 3, "kind")]
    public void Refuses_unknown_headers(int offset, byte value, string reason)
    {
        var bytes = Archive(FileEntry);
        bytes[offset] = value;
        var error = FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.Message.Should().Contain(reason);
        error.Offset.Should().Be(offset);
    }

    [Fact]
    public void Root_version_one_is_a_directory_even_when_empty()
    {
        Read(Archive(_ => { }, 0)).Root.Children.Should().BeEmpty();
    }

    [Fact]
    public void Rejects_trailing_bytes()
    {
        var bytes = Archive(FileEntry);
        var error = FluentActions.Invoking(() => Read([.. bytes, 0])).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.Offset.Should().Be(bytes.Length);
        error.Message.Should().Contain("trailing");
    }

    [Fact]
    public void Rejects_invalid_name_encoding_but_never_decodes_file_content()
    {
        var bytes = Archive(FileEntry);
        bytes[33] = 255; // First byte of the child's name.
        var error = FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.Message.Should().Contain("UTF-8");
        error.Offset.Should().Be(33);
    }

    [Theory]
    [InlineData(6, "Name")]
    [InlineData(23, "entry limit")]
    [InlineData(47, "Content")]
    [InlineData(54, "revision")]
    public void Untrusted_lengths_and_counts_are_checked_before_allocation(int offset, string reason)
    {
        var bytes = Archive(FileEntry);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), uint.MaxValue);
        var error = FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.Message.Should().Contain(reason);
    }

    [Fact]
    public void Error_identifies_the_entry_and_offset()
    {
        var bytes = Archive(FileEntry);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(47), uint.MaxValue);
        var error = FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<TwinBasicFormatException>().Which;
        error.EntryPath.Should().Be("//a"); // Empty root name is retained.
        error.Offset.Should().Be(47);
    }

    [Fact]
    public void Configurable_limits_allow_the_boundary_and_refuse_one_more()
    {
        var bytes = Archive(FileEntry);
        Read(bytes, new(MaxContainerBytes: bytes.Length, MaxEntries: 2, MaxDepth: 1, MaxNameBytes: 1)).Root.Children.Should().ContainSingle();
        FluentActions.Invoking(() => Read(bytes, new(MaxContainerBytes: bytes.Length - 1))).Should().ThrowExactly<TwinBasicFormatException>();
        FluentActions.Invoking(() => Read(bytes, new(MaxEntries: 1))).Should().ThrowExactly<TwinBasicFormatException>();
        FluentActions.Invoking(() => Read(bytes, new(MaxDepth: 0))).Should().ThrowExactly<TwinBasicFormatException>();
        FluentActions.Invoking(() => Read(Archive(w => { Header(w, 2, "long"); w.Write(0u); }), new(MaxNameBytes: 3))).Should().ThrowExactly<TwinBasicFormatException>();
    }

    [Fact]
    public void Entry_budget_applies_across_nested_siblings()
    {
        var bytes = Archive(w =>
        {
            Header(w, 2, "d"); w.Write(1u); FileEntry(w);
            FileEntry(w);
        }, 2);
        FluentActions.Invoking(() => Read(bytes, new(MaxEntries: 3))).Should().ThrowExactly<TwinBasicFormatException>();
        Walk(Read(bytes, new(MaxEntries: 4)).Root, "").Count().Should().Be(4);
    }

    [Fact]
    public void Deep_input_is_refused_without_unbounded_recursion()
    {
        var bytes = Archive(w =>
        {
            for (var i = 0; i < 130; i++) { Header(w, 2, "d"); w.Write(1u); }
            FileEntry(w);
        });
        FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<TwinBasicFormatException>().Which.Message.Should().Contain("nesting");
    }

    [Fact]
    public void Snapshot_is_detached_and_duplicate_names_are_not_dropped()
    {
        var bytes = Archive(w => { FileEntry(w); FileEntry(w); }, 2);
        var result = Read(bytes);
        Array.Clear(bytes);
        result.Root.Children.Length.Should().Be(2);
        result.Root.Children[0].Should().BeOfType<TwinBasicFile>().Which.Content.Should().Equal(new byte[] { 1, 2, 3 });
    }

    [Fact]
    public void Reads_short_nonseekable_streams_and_does_not_close_them()
    {
        using var input = new ShortStream(Archive(FileEntry));
        TwinBasicContainerReader.Read(input, cancellationToken: TestContext.Current.CancellationToken).Root.Children.Should().ContainSingle();
        input.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Starts_at_current_position()
    {
        using var input = new MemoryStream([99, 99, .. Archive(FileEntry)]);
        input.Position = 2;
        TwinBasicContainerReader.Read(input, cancellationToken: TestContext.Current.CancellationToken).Root.Children.Should().ContainSingle();
    }

    [Fact]
    public void Cancellation_before_and_during_read_leaves_stream_open()
    {
        using var source = new CancellationTokenSource();
        using var input = new ShortStream(Archive(FileEntry), source.Cancel);
        FluentActions.Invoking(() => TwinBasicContainerReader.Read(input, cancellationToken: source.Token)).Should().ThrowExactly<OperationCanceledException>();
        input.CanRead.Should().BeTrue();
        FluentActions.Invoking(() => TwinBasicContainerReader.Read(input, cancellationToken: source.Token)).Should().ThrowExactly<OperationCanceledException>();
    }

    [Fact]
    public void Cancellation_at_end_of_input_is_observed_before_parsing()
    {
        using var source = new CancellationTokenSource();
        using var input = new ShortStream([], source.Cancel);
        FluentActions.Invoking(() => TwinBasicContainerReader.Read(input, cancellationToken: source.Token)).Should().ThrowExactly<OperationCanceledException>();
        input.CanRead.Should().BeTrue();
    }

    [Fact]
    public void Invalid_policy_is_rejected_before_reading()
    {
        FluentActions.Invoking(() => Read([], new(MaxDepth: 257))).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Read([], new(MaxContainerBytes: 0))).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Read([], new(MaxEntries: 0))).Should().ThrowExactly<ArgumentOutOfRangeException>();
        FluentActions.Invoking(() => Read([], new(MaxNameBytes: 0))).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    private static TwinBasicContainer Read(byte[] bytes, TwinBasicReadLimits? limits = null)
    {
        using var input = new MemoryStream(bytes);
        return TwinBasicContainerReader.Read(input, limits, TestContext.Current.CancellationToken);
    }

    // Small structural cases supplement the published fixtures; they do not serve as their oracle.
    private static byte[] Archive(Action<BinaryWriter> childrenBody, uint children = 1)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0xEA0BA51Cu); Header(writer, 1, ""); writer.Write(children);
        childrenBody(writer);
        return stream.ToArray();
    }

    private static void Header(BinaryWriter writer, short kind, string name, ulong revision = 0, uint flags = 0, byte category = 0)
    {
        writer.Write(kind); Blob(writer, Encoding.UTF8.GetBytes(name));
        writer.Write(revision); writer.Write(flags); writer.Write(category);
    }

    private static void Blob(BinaryWriter writer, byte[] bytes) { writer.Write((uint)bytes.Length); writer.Write(bytes); }

    private static void FileEntry(BinaryWriter writer)
    {
        Header(writer, 1, "a"); Blob(writer, [1, 2, 3]); writer.Write(0u);
    }

    private sealed class ShortStream(byte[] bytes, Action? afterRead = null) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count)
        {
            var result = base.Read(buffer, offset, Math.Min(count, 3));
            afterRead?.Invoke();
            return result;
        }
    }
}
