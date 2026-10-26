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

    [Fact]
    public void The_tool_reads_the_actual_title_even_for_the_IDE_or_an_empty_caption()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == "take_snapshot");
        tool.Body.Should().Contain("Title: active.Title");
        ToolSource.FieldsOf("SnapshotResult")!.Select(f => f.Name).Should().Contain("title");
    }
}
