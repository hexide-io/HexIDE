using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using AvaloniaEdit;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public class SemanticTokensColourTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TokensOverrideLexicalColoursAndUncoveredTextKeepsThem(bool hasTokens)
    {
        var editor = new TextEditor { Text = "Private Long", SyntaxHighlighting = SyntaxHighlightingTheme.Definition };
        var colorizer = new SemanticTokensColorizer
        {
            Tokens = SemanticTokensColorizer.Decode(editor.Document, hasTokens ? new SemanticTokenResult([0, 0, 7, 0, 0], ["comment"]) : null)
        };
        editor.TextArea.TextView.LineTransformers.Add(colorizer);
        var window = new Window { Width = 400, Height = 200, Content = editor };
        window.Show();
        try
        {
            var visual = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
            var elements = visual.Elements.Where(e => e.DocumentLength > 0).ToArray();
            ((ISolidColorBrush)elements[0].TextRunProperties.ForegroundBrush!).Color.Should()
                .Be(SyntaxHighlightingTheme.Definition.GetNamedColor(hasTokens ? "Comment" : "Keyword").Foreground!.GetColor(null!));
            ((ISolidColorBrush)elements[^1].TextRunProperties.ForegroundBrush!).Color.Should()
                .Be(SyntaxHighlightingTheme.Definition.GetNamedColor("Type").Foreground!.GetColor(null!));
        }
        finally { window.Close(); }
    }
}
