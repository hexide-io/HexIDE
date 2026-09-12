using HexIDE.Controls;

namespace HexIDE.Tests.Controls;

/// <summary>
/// Foldable regions in a message body.
/// </summary>
/// <remarks>
/// <b>A scan, not a parse, and the tests exist to hold that line.</b> The single most interesting body in
/// this window is one that does not parse — a server emitting malformed JSON is a real defect and this is
/// where it surfaces. A strategy that gave up there would fold everything except the message somebody
/// opened the window for.
/// </remarks>
public class JsonFoldingTests
{
    [Fact]
    public void AnObjectSpanningLinesIsFoldable()
    {
        var text = "{\n  \"a\": 1\n}";

        var folds = JsonFolding.Foldings(text);

        folds.Should().ContainSingle();
        folds[0].StartOffset.Should().Be(0);
        folds[0].EndOffset.Should().Be(text.Length);
    }

    [Fact]
    public void ASingleLineRegionIsNotFoldable()
    {
        // A fold arrow that saves no vertical space is noise on every line, and the export's JSON-lines
        // shape is nothing but single-line regions.
        JsonFolding.Foldings("""{"a":1,"b":[1,2]}""").Should().BeEmpty();
    }

    [Fact]
    public void NestedRegionsAreEachFoldable()
    {
        var folds = JsonFolding.Foldings("{\n  \"a\": {\n    \"b\": 1\n  }\n}");

        folds.Should().HaveCount(2);
        folds[0].StartOffset.Should().BeLessThan(folds[1].StartOffset,
            "AvaloniaEdit requires foldings ordered by start offset");
    }

    [Fact]
    public void ABracketInsideAStringIsNotABracket()
    {
        // Real traffic is full of these: a URI with a brace in it, a diagnostic message quoting code.
        // Counting them would pair the wrong things and fold across the message.
        JsonFolding.Foldings("{\n  \"a\": \"} not the end {\"\n}").Should().ContainSingle();
    }

    [Fact]
    public void AnEscapedQuoteDoesNotEndTheString()
    {
        JsonFolding.Foldings("{\n  \"a\": \"say \\\" then }\"\n}").Should().ContainSingle();
    }

    [Fact]
    public void AnUnclosedRegionIsSimplyNotFoldable()
    {
        // A truncated body is head, a marker and tail. The halves fold as far as they close, and what does
        // not close is left alone rather than folded to the end of the document.
        JsonFolding.Foldings("{\n  \"a\": 1\n").Should().BeEmpty();
    }

    [Fact]
    public void AMismatchedPairIsRefusedRatherThanFolded()
    {
        // Content that is not the JSON it looks like. Emitting a wrong fold would make the viewer lie
        // about the shape of exactly the message worth studying.
        JsonFolding.Foldings("{\n  \"a\": 1\n]").Should().BeEmpty();
    }

    [Fact]
    public void AMalformedBodyStillFoldsAsFarAsItCloses()
    {
        // The case this is written for: reformatting must never become a reason a body cannot be read.
        var folds = JsonFolding.Foldings("{\n  \"a\": 1,\n  oops\n}");

        folds.Should().ContainSingle();
    }

    [Fact]
    public void AnArrayIsMarkedDifferentlyFromAnObject()
    {
        // The collapsed placeholder is the only thing left on screen, so it has to say which it was.
        JsonFolding.Foldings("[\n  1,\n  2\n]").Single().Name.Should().Be("[…]");
        JsonFolding.Foldings("{\n  \"a\": 1\n}").Single().Name.Should().Be("{…}");
    }
}
