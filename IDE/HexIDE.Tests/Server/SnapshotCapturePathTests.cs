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
    public void Frame_capture_is_opt_in_and_says_a_covering_window_is_not_in_it()
    {
        var tool = HexIDE.Tests.Infrastructure.ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Parameters.Should().Contain(p => p.Name == "includeFrame" && p.Optional && p.Type == "bool");
        tool.Description.Should().Contain("includeFrame").And.Contain("even where another window covers it").And.Contain("Windows");
    }

    [Fact]
    public void Frame_capture_failure_is_returned_instead_of_a_client_only_image()
    {
        var tool = HexIDE.Tests.Infrastructure.ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Body.Should().Contain("(snapshot, var frameError) = await HexIDE.Automation.SnapshotComposer.CaptureFrameAsync(active)")
            .And.Contain("new SnapshotResult(null, frameError, activeDialog, Title: active.Title)");
    }

    [Fact]
    public void The_tool_explains_and_returns_the_window_offset()
    {
        var tool = HexIDE.Tests.Infrastructure.ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Description.Should().Contain("windowOffsetX").And.Contain("windowOffsetY");
        tool.Body.Should().Contain("WindowOffsetX:").And.Contain("WindowOffsetY:");
    }

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
    public void A_capture_is_named_for_its_UTC_time_and_count()
    {
        var path = SnapshotComposer.NewCapturePath(Path.GetTempPath(), new DateTime(2026, 10, 12, 9, 30, 15, 123, DateTimeKind.Utc));

        Path.GetFileName(path).Should().MatchRegex(@"^hexide_snapshot_20261012-093015-123Z_\d+\.png$");
    }

    [Fact]
    public void The_tool_names_a_capture_by_the_UTC_clock()
    {
        // Local time with no offset could not be lined up against the tools' UTC stamps (#956 review).
        HexIDE.Tests.Infrastructure.ToolSource.Tools.Single(t => t.Name == "take_snapshot").Body
            .Should().Contain("NewCapturePath(System.IO.Path.GetTempPath(), DateTime.UtcNow)");
    }
}
