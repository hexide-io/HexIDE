using System;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using AvaloniaEdit;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Themes;

namespace HexIDE.Controls;

internal static class HoverContentView
{
    internal static Control Render(MarkupContent content, TextEditor editor, Action<Uri> navigate)
    {
        var panel = new StackPanel { Spacing = 8, MaxWidth = 700 };
        foreach (var block in HoverContentRenderer.Render(content))
        {
            if (block.Language is { } language)
            {
                var code = new TextEditor
                {
                    Text = block.Text, IsReadOnly = true, FontFamily = editor.FontFamily,
                    FontSize = editor.FontSize, Background = editor.Background, Foreground = editor.Foreground,
                    SyntaxHighlighting = language.Equals("vb", StringComparison.OrdinalIgnoreCase)
                        || language.Equals("vba", StringComparison.OrdinalIgnoreCase) ? SyntaxHighlightingTheme.Definition : null,
                    HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                    VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
                };
                code.Options.EnableHyperlinks = false;
                code.Options.EnableEmailHyperlinks = false;
                panel.Children.Add(code);
            }
            else
            {
                var text = new TextBlock { TextWrapping = TextWrapping.Wrap };
                if (block.ListMarker is { } marker)
                {
                    text.Margin = new Thickness(12, 0, 0, 0);
                    text.Inlines!.Add(new Run(char.IsDigit(marker[0]) ? marker + " " : "• "));
                }
                if (content.Kind == "markdown") AddInlines(text.Inlines!, block.Text, editor.FontFamily, navigate);
                else text.Inlines!.Add(new Run(block.Text));
                panel.Children.Add(text);
            }
        }
        return panel;
    }

    private static void AddInlines(InlineCollection target, string text, FontFamily font, Action<Uri> navigate, int depth = 0)
    {
        if (depth > 16) { target.Add(new Run(text)); return; }
        // This subset deliberately has no HTML path: tags are ordinary Run text.
        var pattern = @"(`+)(.*?)\1|\[([^\]\n]+)\]\(([^\s)]+)\)|(\*\*|__|\*|_)(.+?)\5";
        var offset = 0;
        foreach (Match match in Regex.Matches(text, pattern))
        {
            target.Add(new Run(text[offset..match.Index]));
            if (match.Groups[1].Success)
                target.Add(new Run(match.Groups[2].Value) { FontFamily = font });
            else if (match.Groups[3].Success)
            {
                var label = match.Groups[3].Value;
                if (Uri.TryCreate(match.Groups[4].Value, UriKind.Absolute, out var uri)
                    && uri.Scheme is "http" or "https" or "file")
                {
                    var caption = new TextBlock();
                    AddInlines(caption.Inlines!, label, font, navigate, depth + 1);
                    var link = new HyperlinkButton { Content = caption, Padding = new Thickness(0) };
                    link.Click += (_, _) => navigate(uri);
                    target.Add(new InlineUIContainer(link));
                }
                else target.Add(new Run(label));
            }
            else
            {
                Span span = match.Groups[5].Length == 2 ? new Bold() : new Italic();
                AddInlines(span.Inlines, match.Groups[6].Value, font, navigate, depth + 1);
                target.Add(span);
            }
            offset = match.Index + match.Length;
        }
        target.Add(new Run(text[offset..]));
    }
}
