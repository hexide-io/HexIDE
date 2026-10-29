using System.Text.Json;
using HexIDE.Desktop.Server;

namespace HexIDE.Tests.Infrastructure;

/// <summary>A snapshot names its window and explains the missing native frame (#2078).</summary>
public class SnapshotWindowMetadataTests
{
    [Fact]
    public void The_description_explains_the_client_area_and_where_to_read_the_title()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Description.Should().Contain("client area, without its title bar or frame")
            .And.Contain("'title'").And.Contain("'activeDialog'").And.Contain("compatibility");
    }

    [Fact]
    public void The_reply_explains_what_the_image_contains()
    {
        var reply = JsonSerializer.SerializeToElement(new SnapshotResult("capture.png", null, "Hello"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        reply.TryGetProperty("captureArea", out var area).Should().BeTrue();
        area.GetString().Should().Be("Window client area, without its title bar or frame; open popups are included unless the note says otherwise.");
        reply.TryGetProperty("title", out _).Should().BeTrue();
        reply.GetProperty("activeDialog").GetString().Should().Be("Hello");
    }

    // The description gave the formulas, and a caller had to put the reply's fields into them (#2565). The reply now gives
    // the mapping with this capture's own numbers.
    [Fact]
    public void The_units_map_a_boundingRect_into_the_image_and_the_inline_copy_with_this_capture_s_numbers()
    {
        HexIdeTools.SnapshotUnits(1.5, 10, 20, null).Should().Be(
            "inspect_element's boundingRect is relative to the window, in device-independent units. In this image, "
            + "x = x * 1.5 + 10 and y = y * 1.5 + 20.");
        // The inline copy's x is (x + windowOffsetX / scale) * inlineScale, as the description used to put it.
        HexIdeTools.SnapshotUnits(1.5, 10, 20, 0.75).Should().EndWith(
            "In the inline copy, x = x * 0.75 + 5 and y = y * 0.75 + 10.");
    }

    [Fact]
    public void The_reply_carries_the_units_and_how_long_the_file_is_kept_and_the_description_points_at_them()
    {
        var reply = JsonSerializer.SerializeToElement(new SnapshotResult("capture.png", null, null,
                Units: HexIdeTools.SnapshotUnits(1, 0, 0, null), PathNote: HexIdeTools.SnapshotPathNote),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        reply.GetProperty("units").GetString().Should().StartWith("inspect_element's boundingRect is relative to the window");
        reply.GetProperty("pathNote").GetString().Should().Be(
            "Each call writes a new file. Earlier captures stay at their paths, and nothing deletes them.");
        var tool = ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Description.Should().Contain("'units'").And.Contain("'pathNote'");
        tool.Body.Should().Contain("Units: SnapshotUnits(").And.Contain("PathNote: SnapshotPathNote")
            .And.Contain("Units = SnapshotUnits(", "the inline copy's mapping is given too");
    }

    [Fact]
    public void The_tool_reads_the_actual_title_even_for_the_IDE_or_an_empty_caption()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Body.Should().Contain("Title: active.Title");
        ToolSource.FieldsOf("SnapshotResult")!.Select(f => f.Name).Should().Contain("title");
    }
}
