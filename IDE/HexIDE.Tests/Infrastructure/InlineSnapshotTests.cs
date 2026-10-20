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

    // A path of each test's own, so a copy one test holds cannot answer another's.
    private readonly string _path = $"/tmp/hexide_snapshot_{Guid.NewGuid():N}.png";

    private string Reply => $$"""{"path":"{{_path}}","width":3840,"height":2054,"scale":1.5}""";

    private static CallToolRequestParams Call(string name, string? arguments) => new()
    {
        Name = name,
        Arguments = arguments is null ? null : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments),
    };

    private static CallToolResult Result(string text, bool error = false) =>
        new() { Content = [new TextContentBlock { Text = text }], IsError = error };

    [Fact]
    public void Asked_for_inline_the_reply_gains_the_copy_the_tool_held_after_its_own_text()
    {
        InlineSnapshot.Hold(_path, Png);

        var result = InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), Result(Reply));

        result.Content.Should().HaveCount(2);
        ((TextContentBlock)result.Content[0]).Text.Should().Be(Reply, "the reply itself is unchanged");
        var image = result.Content[1].Should().BeOfType<ImageContentBlock>().Subject;
        image.MimeType.Should().Be("image/png");
        image.DecodedData.ToArray().Should().Equal(Png);
    }

    [Fact]
    public void A_copy_is_attached_once_and_not_held_after()
    {
        InlineSnapshot.Hold(_path, Png);
        InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), Result(Reply));

        var again = InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), Result(Reply));

        ((TextContentBlock)again.Content[1]).Text.Should().Be($"The image was not inlined: no copy of {_path} was made.");
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"inline":false}""")]
    [InlineData(null)]
    public void Not_asked_the_reply_is_left_as_the_tool_gave_it_and_nothing_stays_held(string? arguments)
    {
        InlineSnapshot.Hold(_path, Png);
        var reply = Result(Reply);

        InlineSnapshot.With(Call("take_snapshot", arguments), reply).Should().BeSameAs(reply);
        InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), Result(Reply))
            .Content[1].Should().BeOfType<TextContentBlock>("the copy was let go with the call it was made for");
    }

    [Fact]
    public void Another_tool_s_reply_is_left_as_it_was()
    {
        var reply = Result(Reply);

        InlineSnapshot.With(Call("dump_visual_tree", """{"inline":true}"""), reply).Should().BeSameAs(reply);
    }

    [Theory]
    [InlineData("""{"error":"Window has no size","activeDialog":null}""", false)]
    [InlineData("No main window", false)]
    [InlineData(null, true)]
    public void A_reply_with_no_image_to_show_is_left_as_it_was(string? text, bool error)
    {
        var reply = Result(text ?? Reply, error);

        InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), reply).Should().BeSameAs(reply);
    }

    [Fact]
    public void The_image_follows_a_modal_dialog_s_note_and_its_path_is_still_found()
    {
        InlineSnapshot.Hold(_path, Png);
        var noted = OpenDialogNote.With(Result(Reply), "'About', a modal dialog over 'HexIDE', is open.");

        var result = InlineSnapshot.With(Call("take_snapshot", """{"inline":true}"""), noted);

        result.Content.Select(c => c.GetType()).Should().Equal(typeof(TextContentBlock), typeof(ImageContentBlock));
    }

    [Theory]
    [InlineData(3840, 2054, 1568, 839)]
    [InlineData(2054, 3840, 839, 1568)]
    [InlineData(918, 284, 918, 284)]
    [InlineData(1568, 10, 1568, 10)]
    [InlineData(20000, 1, 1568, 1)]
    public void The_copy_is_fitted_to_the_longest_edge_and_a_smaller_image_is_kept_as_it_is(
        int width, int height, int fittedWidth, int fittedHeight)
    {
        InlineSnapshot.Fit(width, height).Should().Be((fittedWidth, fittedHeight));
    }

    [Fact]
    public void The_tool_takes_the_argument_the_filter_reads_and_the_server_runs_the_filter()
    {
        var tool = ToolSource.Tools.Single(t => t.Name == InlineSnapshot.Tool);
        tool.Parameters.Select(p => p.Name).Should().Contain(InlineSnapshot.Argument);
        tool.Description.Should().Contain("'inline' true");
        tool.Body.Should().Contain("InlineSnapshot.Hold(");

        File.ReadAllText(Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Desktop", "Server", "IdeServer.cs"))
            .Should().Contain(".AddCallToolFilter(InlineSnapshot.Filter)");
    }
}
