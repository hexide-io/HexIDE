using HexIDE.Office;

namespace HexIDE.Tests.Office;

public class CompoundFileLifetimeTests
{
    [Fact]
    public void OpeningLargeFileReadsMetadataAndStreamsRemainLazy()
    {
        using var source = new CountingStream(CompoundFileTests.Fixture("v3-difat.cfb.gz"));
        using var file = CompoundFile.Open(source);
        source.BytesRead.Should().BeLessThan(100_000);
        file.Root.FindChild("Big")!.ReadBytes().Length.Should().Be(7_600_000);
        source.BytesRead.Should().BeGreaterThan(7_600_000);
    }

    [Fact]
    public void OwnershipAndClosedReaderHaveExplicitLifetimes()
    {
        using var source = new MemoryStream(CompoundFileTests.Fixture("xls-shaped.cfb"));
        var file = CompoundFile.Open(source);
        var entry = file.Root.FindChild("Workbook")!;
        file.Dispose();
        source.CanRead.Should().BeTrue();
        Action read = () => entry.ReadBytes();
        read.Should().Throw<OfficeFileException>().Which.Reason.Should().Contain("closed");
        var owned = CompoundFile.Open(source, leaveOpen: false);
        owned.Dispose();
        source.CanRead.Should().BeFalse();
    }

    [Fact]
    public void DisposedBackingStreamIsReportedAsLibraryException()
    {
        var source = new MemoryStream(CompoundFileTests.Fixture("xls-shaped.cfb"));
        using var file = CompoundFile.Open(source);
        source.Dispose();
        Action read = () => file.Root.FindChild("Workbook")!.ReadBytes();
        read.Should().Throw<OfficeFileException>();
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public long BytesRead { get; private set; }
        public override int Read(Span<byte> buffer)
        {
            var count = base.Read(buffer);
            BytesRead += count;
            return count;
        }
    }
}
