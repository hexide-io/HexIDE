using HexIDE.Automation;

namespace HexIDE.Tests.Server;

/// <summary>
/// take_snapshot writes each capture to a file of its own, so a path one call returned still holds that image when
/// the next call returns (#756). Every capture used to go to one <c>hexide_snapshot.png</c>, and a before/after
/// pair read the "after" twice.
/// </summary>
public class SnapshotCapturePathTests
{
    [Fact]
    public void Two_captures_in_the_same_millisecond_get_two_files()
    {
        var folder = Path.Combine(Path.GetTempPath(), "hexide-snapshot-path-test");
        var now = new DateTime(2026, 10, 12, 9, 30, 15, 123);

        var first = SnapshotComposer.NewCapturePath(folder, now);
        var second = SnapshotComposer.NewCapturePath(folder, now);

        first.Should().NotBe(second);
        Path.GetDirectoryName(first).Should().Be(folder);
        Path.GetDirectoryName(second).Should().Be(folder);
    }

    [Fact]
    public void A_capture_is_named_for_its_time_and_count()
    {
        var path = SnapshotComposer.NewCapturePath(Path.GetTempPath(), new DateTime(2026, 10, 12, 9, 30, 15, 123));

        Path.GetFileName(path).Should().MatchRegex(@"^hexide_snapshot_20261012-093015-123_\d+\.png$");
    }
}
