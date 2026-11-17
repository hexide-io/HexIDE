using System.Buffers.Binary;
using System.Text;
using HexIDE.Office;

namespace HexIDE.Tests.Office;

public class OfficeMalformedTests
{
    private static uint Read(byte[] bytes, int offset) => BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset, 4));
    internal static void Write(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset, 4), value);
    internal static int DirectoryEntry(byte[] bytes, string name)
    {
        var size = 1 << BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(30, 2));
        var sector = Read(bytes, 48);
        var fat = Read(bytes, 76);
        while (sector != 0xFFFFFFFE)
        {
            var start = ((int)sector + 1) * size;
            for (var i = start; i < start + size; i += 128)
            {
                var length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(i + 64, 2));
                if (length >= 2 && Encoding.Unicode.GetString(bytes, i, length - 2) == name)
                    return i;
            }
            sector = Read(bytes, ((int)fat + 1) * size + (int)sector * 4);
        }
        throw new InvalidOperationException("Fixture entry not found.");
    }

    [Theory(Timeout = 5000)]
    [InlineData("fat-loop", "loops")]
    [InlineData("difat-loop", "loops")]
    [InlineData("difat-overlong", "count")]
    [InlineData("fat-overlong", "chain length")]
    [InlineData("sector-past-end", "past the end")]
    [InlineData("stream-short-chain", "chain length")]
    [InlineData("directory-loop", "Directory tree loops")]
    [InlineData("directory-out-of-range", "Directory tree entry is out of range")]
    [InlineData("truncated", "Truncated")]
    [InlineData("truncated-half", "past the end")]
    [InlineData("mini-loop", "loops")]
    [InlineData("mini-past-end", "past the end")]
    [InlineData("mini-short-chain", "chain length")]
    [InlineData("fat-count", "count exceeds")]
    [InlineData("difat-count", "count exceeds")]
    [InlineData("mini-fat-count", "count exceeds")]
    [InlineData("signature", "signature")]
    [InlineData("version", "header")]
    [InlineData("byte-order", "header")]
    [InlineData("sector-shift", "header")]
    [InlineData("mini-shift", "header")]
    [InlineData("cutoff", "header")]
    [InlineData("name-length", "name length")]
    public async Task PatchedFixtureIsRefusedWithReason(string corruption, string reason)
    {
        await Task.Run(() =>
        {
            var bytes = CompoundFileTests.Fixture(corruption.StartsWith("difat-", StringComparison.Ordinal) ? "v3-difat.cfb.gz" : "v3-mixed.cfb");
            var fatStart = ((int)Read(bytes, 76) + 1) * 512;
            var large = DirectoryEntry(bytes, corruption.StartsWith("difat-", StringComparison.Ordinal) ? "Big" : "Large");
            var first = Read(bytes, large + 116);
            switch (corruption)
            {
                case "fat-loop": Write(bytes, fatStart + (int)first * 4, first); break;
                case "difat-loop":
                    var difat = ((int)Read(bytes, 68) + 1) * 512;
                    Write(bytes, difat + 508, Read(bytes, 68)); break;
                case "difat-overlong": Write(bytes, 72, 0); break;
                case "fat-overlong": Write(bytes, large + 120, 4096); break;
                case "sector-past-end": Write(bytes, fatStart + (int)first * 4, (uint)(bytes.Length / 512)); break;
                case "stream-short-chain": Write(bytes, large + 120, 100000); break;
                case "directory-loop": Write(bytes, ((int)Read(bytes, 48) + 1) * 512 + 76, 0); break;
                case "directory-out-of-range": Write(bytes, large + 72, 100000); break;
                case "truncated": Array.Resize(ref bytes, 300); break;
                case "truncated-half": Array.Resize(ref bytes, bytes.Length / 2); break;
                case "mini-loop":
                case "mini-past-end":
                case "mini-short-chain":
                    var small = DirectoryEntry(bytes, "Small");
                    var mini = ((int)Read(bytes, 60) + 1) * 512 + (int)Read(bytes, small + 116) * 4;
                    Write(bytes, mini, corruption == "mini-loop" ? Read(bytes, small + 116) : corruption == "mini-past-end" ? 100000 : 0xFFFFFFFE); break;
                case "fat-count": Write(bytes, 44, uint.MaxValue); break;
                case "difat-count": Write(bytes, 72, uint.MaxValue); break;
                case "mini-fat-count": Write(bytes, 64, uint.MaxValue); break;
                case "signature": bytes[0] = 0; break;
                case "version": bytes[26] = 2; break;
                case "byte-order": bytes[28] = 0; break;
                case "sector-shift": bytes[30] = 12; break;
                case "mini-shift": bytes[32] = 9; break;
                case "cutoff": Write(bytes, 56, 100); break;
                case "name-length": bytes[large + 64] = 100; break;
            }
            Action read = () =>
            {
                using var source = new MemoryStream(bytes);
                using var file = CompoundFile.Open(source);
                file.Root.FindChild(corruption.StartsWith("mini-", StringComparison.Ordinal) ? "Small" : corruption.StartsWith("difat-", StringComparison.Ordinal) ? "Big" : "Large")!.ReadBytes();
            };
            read.Should().Throw<OfficeFileException>().Which.Reason.Should().Contain(reason);
        }, TestContext.Current.CancellationToken);
    }

    [Fact(Timeout = 5000)]
    public void HugeClaimIsRefusedBeforeLargeAllocation()
    {
        var bytes = CompoundFileTests.Fixture("v4-mixed.cfb");
        var entry = DirectoryEntry(bytes, "Large");
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(entry + 120, 8), 1UL << 48);
        using var source = new MemoryStream(bytes);
        using var file = CompoundFile.Open(source);
        var before = GC.GetAllocatedBytesForCurrentThread();
        Action read = () => file.Root.FindChild("Large")!.ReadBytes();
        read.Should().Throw<OfficeFileException>().Which.Reason.Should().Contain("chain length");
        (GC.GetAllocatedBytesForCurrentThread() - before).Should().BeLessThan(1024 * 1024);
    }

    [Fact]
    public void MisSortedTreeStillFindsChildrenByCfbNameComparison()
    {
        var bytes = CompoundFileTests.Fixture("xls-shaped.cfb");
        var entry = DirectoryEntry(bytes, "_VBA_PROJECT_CUR");
        var replacement = Encoding.Unicode.GetBytes("_vba_project_cur");
        replacement.CopyTo(bytes, entry);
        var left = Read(bytes, entry + 68);
        Write(bytes, entry + 68, Read(bytes, entry + 72));
        Write(bytes, entry + 72, left);
        using var source = new MemoryStream(bytes);
        using var file = CompoundFile.Open(source);
        file.Root.FindChild("_VBA_PROJECT_CUR")!.IsStorage.Should().BeTrue();
        file.Root.FindChild("_vba_project_cur")!.FindChild("vba")!.FindChild("DIR")!.ReadBytes().Length.Should().Be(600);
    }

    [Theory]
    [InlineData("ıAAAA", "IAAAA", true)]
    [InlineData("ſAAAA", "SAAAA", true)]
    [InlineData("ςAAAA", "ΣAAAA", true)]
    [InlineData("éAAAA", "ÉAAAA", true)]
    [InlineData("𐐨ABC", "𐐀ABC", false)]
    public void NameLookupUsesUnicodeUppercasePerUtf16Unit(string stored, string lookup, bool found)
    {
        var bytes = CompoundFileTests.Fixture("v3-mixed.cfb");
        var entry = DirectoryEntry(bytes, "Small");
        Encoding.Unicode.GetBytes(stored).CopyTo(bytes, entry);
        using var source = new MemoryStream(bytes);
        using var file = CompoundFile.Open(source);
        (file.Root.FindChild(lookup) != null).Should().Be(found);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(7u)]
    public void AnEmptyStream_ReadsNothing_WhateverItsStartSector(uint start)
    {
        var bytes = CompoundFileTests.Fixture("v3-mixed.cfb");
        Write(bytes, DirectoryEntry(bytes, "Empty") + 116, start);
        using var source = new MemoryStream(bytes);
        using var file = CompoundFile.Open(source);

        file.Root.FindChild("Empty")!.ReadBytes().Should().BeEmpty();
    }

    [Fact]
    public void Version3IgnoresUninitialisedHighSizeDword()
    {
        var bytes = CompoundFileTests.Fixture("v3-mixed.cfb");
        Write(bytes, DirectoryEntry(bytes, "Large") + 124, uint.MaxValue);
        using var source = new MemoryStream(bytes);
        using var file = CompoundFile.Open(source);
        file.Root.FindChild("Large")!.ReadBytes().Length.Should().Be(20000);
    }
}
