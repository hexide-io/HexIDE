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
    [AvaloniaTheory]
    [InlineData("variable", "Identifier")]
    [InlineData("operator", "Operator")]
    public void SemanticTokensUseAndResetTheChosenBackground(string tokenType, string colorName)
    {
        var choice = SyntaxHighlightingTheme.Definition.GetNamedColor(colorName);
        var original = choice.Background;
        var background = Color.Parse("#654321");
        var editor = new TextEditor { Text = "value", SyntaxHighlighting = SyntaxHighlightingTheme.Definition };
        editor.TextArea.TextView.LineTransformers.Add(new SemanticTokensColorizer
        {
            Tokens = SemanticTokensColorizer.Decode(editor.Document, new([0, 0, 5, 0, 0], [tokenType]))
        });
        var window = new Window { Width = 400, Height = 200, Content = editor };
        try
        {
            window.Show();
            choice.Background = new AvaloniaEdit.Highlighting.SimpleHighlightingBrush(background);
            editor.TextArea.TextView.Redraw();
            var visual = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
            visual.Elements[0].TextRunProperties.BackgroundBrush.Should().BeAssignableTo<ISolidColorBrush>()
                .Which.Color.Should().Be(background);
            choice.Background = original;
            editor.TextArea.TextView.Redraw();
            visual = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(1));
            var resetBackground = (visual.Elements[0].TextRunProperties.BackgroundBrush as ISolidColorBrush)?.Color;
            resetBackground.Should().NotBe(background);
        }
        finally { window.Close(); choice.Background = original; }
    }

}
