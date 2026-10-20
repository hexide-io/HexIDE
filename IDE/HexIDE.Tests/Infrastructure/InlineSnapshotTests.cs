using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using HexIDE.Desktop.Server;
using ModelContextProtocol.Protocol;

namespace HexIDE.Tests.Infrastructure;

/// <summary>take_snapshot's image returned in its reply for a client that cannot open the path (#957).</summary>
public class InlineSnapshotTests
{
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
    private const string Reply = """{"path":"/tmp/hexide_snapshot_1.png","width":1920,"height":1011,"scale":1}""";

    private static CallToolRequestParams Call(string name, string arguments) => new()
    {
        Name = name,
        Arguments = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments),
    };

    private static CallToolResult Result(string text, bool error = false) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = error };

    private static byte[] Read(string path) =>
        path == "/tmp/hexide_snapshot_1.png" ? Png : throw new FileNotFoundException("no such file", path);

    [Fact]
    public void Asked_for_inline_the_reply_gains_the_image_its_path_names_after_its_own_text()
    {
        var result = InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), Result(Reply), Read);

        result.Content.Should().HaveCount(2);
        ((TextContentBlock)result.Content[0]).Text.Should().Be(Reply, "the reply itself is unchanged");
        var image = result.Content[1].Should().BeOfType<ImageContentBlock>().Subject;
        image.MimeType.Should().Be("image/png");
        image.DecodedData.ToArray().Should().Equal(Png);
    }

    [Theory]
    [InlineData("take_snapshot", """{}""")]
    [InlineData("take_snapshot", """{"inline":false}""")]
    [InlineData("take_snapshot", """{"inline":"true"}""")]
    [InlineData("dump_visual_tree", """{"inline":true}""")]
    public void Not_asked_the_reply_is_left_as_the_tool_gave_it(string tool, string arguments)
    {
        var reply = Result(Reply);

        InlineSnapshot.With(Call(tool, arguments), reply, Read).Should().BeSameAs(reply);
    }

    [Theory]
    [InlineData("""{"error":"Window has no size","activeDialog":null}""", false)]
    [InlineData("No main window", false)]
    [InlineData(Reply, true)]
    public void A_reply_with_no_image_to_show_is_left_as_it_was(string text, bool error)
    {
        var reply = Result(text, error);

        InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), reply, Read).Should().BeSameAs(reply);
    }

    [Fact]
    public void A_file_that_cannot_be_read_keeps_the_reply_and_says_why()
    {
        var result = InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""),
            Result("""{"path":"/tmp/gone.png","width":1,"height":1,"scale":1}"""), Read);

        result.IsError.Should().NotBe(true);
        result.Content.Select(c => c.GetType()).Should().Equal(typeof(TextContentBlock), typeof(TextContentBlock));
        ((TextContentBlock)result.Content[1]).Text.Should().Be(
            "The image was not inlined: /tmp/gone.png could not be read (no such file)");
    }

    [Fact]
    public void The_tool_takes_the_argument_the_filter_reads_and_says_what_it_is_for()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == InlineSnapshot.Tool);

        tool.Parameters.Select(p => p.Name).Should().Contain(InlineSnapshot.Argument);
        tool.Description.Should().Contain("'inline' true");
    }
}
