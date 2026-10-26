using Avalonia.Headless.XUnit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Highlighting;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public class Vb6HighlightingTests
{
    [AvaloniaFact]
    public void NamedColoursMatchThemePalette()
    {
        var palette = ((string Name, string Hex)[])typeof(SyntaxHighlightingTheme)
            .GetField("DarkPalette", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        SyntaxHighlightingTheme.Definition.NamedHighlightingColors.Select(c => c.Name)
            .Should().BeEquivalentTo(palette.Select(c => c.Name));
    }

    [AvaloniaFact]
    public void DigitsInsideIdentifiersRemainPlain()
    {
        using var highlighter = new DocumentHighlighter(new TextDocument("Form1 amount2"), SyntaxHighlightingTheme.Definition);
        highlighter.HighlightLine(1).Sections.Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData("' comment", "Comment")]
    [InlineData("rEm comment", "Comment")]
    [InlineData("\"a\"\"b\"", "String")]
    [InlineData("123", "Number")]
    [InlineData("&HFF&", "Number")]
    [InlineData("&O77%", "Number")]
    [InlineData(".5E-2!", "Number")]
    [InlineData("#1/2/2000#", "Constant")]
    [InlineData("amount%", "Type", 1)]
    [InlineData("amount&", "Type", 1)]
    [InlineData("amount!", "Type", 1)]
    [InlineData("amount#", "Type", 1)]
    [InlineData("amount@", "Type", 1)]
    [InlineData("amount$", "Type", 1)]
    [InlineData(" _", "Keyword")]
    public void TokensReceiveNamedColour(string text, string colour, int colouredLength = 0)
    {
        var definition = SyntaxHighlightingTheme.Definition;
        using var highlighter = new DocumentHighlighter(new TextDocument(text + "\n"), definition);
        var sections = highlighter.HighlightLine(1).Sections;
        sections.Should().NotBeEmpty();
        sections.Should().OnlyContain(s => s.Color == definition.GetNamedColor(colour));
        sections.Sum(s => s.Length).Should().Be(colouredLength == 0 ? text.TrimStart().Length : colouredLength);
    }

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
