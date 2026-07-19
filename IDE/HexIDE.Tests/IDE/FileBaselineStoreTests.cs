using System.IO;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Tests.IDE;

public class FileBaselineStoreTests
{
    private static readonly string Dir = Path.Combine(Path.GetTempPath(), "hexide-baseline-tests");
    private static string P(string name) => Path.Combine(Dir, name);

    // ── FileHasher ──────────────────────────────────────────────

    [Fact]
    public void Hash_IsStable_ForIdenticalContent()
    {
        FileHasher.Hash("Sub Foo()\nEnd Sub").Should().Be(FileHasher.Hash("Sub Foo()\nEnd Sub"));
    }

    [Fact]
    public void Hash_Differs_ForDifferentContent()
    {
        FileHasher.Hash("a").Should().NotBe(FileHasher.Hash("b"));
    }

    [Fact]
    public void Hash_StringAndBytes_AgreeForUtf8()
    {
        FileHasher.Hash("hello").Should().Be(FileHasher.Hash(Encoding.UTF8.GetBytes("hello")));
    }

    // ── Record / TryGet ─────────────────────────────────────────

    [Fact]
    public void TryGet_ReturnsNull_WhenNothingRecorded()
    {
        var sut = new FileBaselineStore();
        sut.TryGet(P("missing.bas")).Should().BeNull();
    }

    [Fact]
    public void Record_String_StoresHashAndLength()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "abc");

        var baseline = sut.TryGet(P("a.bas"));
        baseline.Should().NotBeNull();
        baseline!.Hash.Should().Be(FileHasher.Hash("abc"));
        baseline.Length.Should().Be(3);
    }

    // ── Matches ─────────────────────────────────────────────────

    [Fact]
    public void Matches_True_AfterRecordingSameContent()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "Module code");
        sut.Matches(P("a.bas"), Encoding.UTF8.GetBytes("Module code")).Should().BeTrue();
    }

    [Fact]
    public void Matches_False_WhenContentChanged()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "original");
        sut.Matches(P("a.bas"), Encoding.UTF8.GetBytes("modified externally")).Should().BeFalse();
    }

    [Fact]
    public void Matches_False_WhenNoBaseline()
    {
        var sut = new FileBaselineStore();
        sut.Matches(P("a.bas"), Encoding.UTF8.GetBytes("anything")).Should().BeFalse();
    }

    [Fact]
    public void Matches_False_WhenLengthDiffersButHashWouldCollideOnPrefix()
    {
        // Distinct lengths are rejected by the cheap pre-filter before hashing.
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "abc");
        sut.Matches(P("a.bas"), Encoding.UTF8.GetBytes("abcd")).Should().BeFalse();
    }

    // ── Epoch ───────────────────────────────────────────────────

    [Fact]
    public void Epoch_IsMonotonic_AcrossRecords()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "1");
        var first = sut.TryGet(P("a.bas"))!.Epoch;
        sut.Record(P("b.bas"), "2");
        var second = sut.TryGet(P("b.bas"))!.Epoch;

        second.Should().BeGreaterThan(first);
    }

    [Fact]
    public void Epoch_Bumps_WhenSamePathReRecorded()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "1");
        var first = sut.TryGet(P("a.bas"))!.Epoch;
        sut.Record(P("a.bas"), "2");
        var second = sut.TryGet(P("a.bas"))!.Epoch;

        second.Should().BeGreaterThan(first);
    }

    // ── Remove ──────────────────────────────────────────────────

    [Fact]
    public void Remove_DropsBaseline()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "x");
        sut.Remove(P("a.bas"));

        sut.TryGet(P("a.bas")).Should().BeNull();
        sut.Matches(P("a.bas"), Encoding.UTF8.GetBytes("x")).Should().BeFalse();
    }

    // ── Case-insensitive keying ─────────────────────────────────

    [Fact]
    public void Keys_AreCaseInsensitive()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("Module.bas"), "code");

        // Same path, different filename casing → resolves to the same baseline entry.
        sut.TryGet(P("module.bas")).Should().NotBeNull();
        sut.Matches(P("MODULE.BAS"), Encoding.UTF8.GetBytes("code")).Should().BeTrue();
    }

    // ── Snapshot ────────────────────────────────────────────────

    [Fact]
    public void Snapshot_ContainsRecordedEntries_AndIsDecoupled()
    {
        var sut = new FileBaselineStore();
        sut.Record(P("a.bas"), "x");

        var snapshot = sut.Snapshot();
        snapshot.Should().ContainKey(Path.GetFullPath(P("a.bas")));

        // Mutating the store after the snapshot must not change the snapshot.
        sut.Record(P("b.bas"), "y");
        snapshot.Should().NotContainKey(Path.GetFullPath(P("b.bas")));
    }
}
