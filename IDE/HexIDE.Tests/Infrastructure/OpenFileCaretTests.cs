namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// open_file's line and column (#700), read headlessly from its declaration, as no test project references
/// HexIDE.Desktop. What it does with them is <c>CaretPlacementTests</c>'s.
/// </summary>
public class OpenFileCaretTests
{
    private static ToolSource.Tool OpenFile => ToolSource.Tools.Single(t => t.Name == "open_file");

    [Fact]
    public void Its_line_and_column_say_they_count_from_one_as_the_code_window_does()
    {
        OpenFile.Parameters.Single(p => p.Name == "line").Description.Should().Contain("1-based line")
            .And.Contain("header included");
        OpenFile.Parameters.Single(p => p.Name == "column").Description.Should().Contain("1-based column");
    }

    [Fact]
    public void A_place_off_the_text_is_refused_before_anything_opens()
    {
        // The review found a line past the end opened the document, then refused, and so gave it no focus.
        var body = OpenFile.Body;
        var refusal = body.IndexOf("CaretPlacement.Refusal(", StringComparison.Ordinal);

        refusal.Should().BeGreaterThan(-1);
        refusal.Should().BeLessThan(body.IndexOf("EditCode(", StringComparison.Ordinal));
    }

    [Fact]
    public void The_note_reads_the_caret_back_from_the_editor_get_file_content_reads()
    {
        OpenFile.Body.Should().Contain("CaretPlacement.Where(").And.Contain("CodeEditorOf(placed)");
    }
}
