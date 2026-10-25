using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public class Vb6HighlightingTests
{
    [AvaloniaTheory]
    [InlineData("Begin", "MetadataKeyword")]
    [InlineData("begin", "MetadataKeyword")]
    [InlineData("End", "MetadataKeyword")]
    [InlineData("end", "MetadataKeyword")]
    [InlineData("private", "StorageModifier")]
    [InlineData("SUB", "Keyword")]
    public void KeywordsIgnoreCase(string text, string colour)
    {
        var definition = SyntaxHighlightingTheme.Definition;
        using var highlighter = new DocumentHighlighter(new TextDocument(text), definition);

        var section = highlighter.HighlightLine(1).Sections.Should().ContainSingle().Subject;
        section.Offset.Should().Be(0);
        section.Length.Should().Be(text.Length);
        section.Color.Should().BeSameAs(definition.GetNamedColor(colour));
    }
}
