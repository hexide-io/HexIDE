using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using AvaloniaEdit;
using HexIDE.Controls;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Integration.Tests.Controls;

public class HoverContentViewTests
{
    [AvaloniaFact]
    public void Paragraphs_emphasis_inline_code_and_lists_are_controls()
    {
        var editor = new TextEditor();
        var panel = (StackPanel)HoverContentView.Render(new("markdown",
            "First **bold** and *italic* and `code`.\n\n- one\n- two\n\n1. three"), editor, _ => { });
        panel.Children.Should().HaveCount(4);
        var paragraph = (TextBlock)panel.Children[0];
        paragraph.Inlines!.OfType<Bold>().Single().Inlines.OfType<Run>().Single().Text.Should().Be("bold");
        paragraph.Inlines!.OfType<Italic>().Single().Inlines.OfType<Run>().Single().Text.Should().Be("italic");
        paragraph.Inlines!.OfType<Run>().Single(r => r.Text == "code").FontFamily.Should().Be(editor.FontFamily);
        ((TextBlock)panel.Children[1]).Inlines!.OfType<Run>().First().Text.Should().Be("• ");
        ((TextBlock)panel.Children[3]).Inlines!.OfType<Run>().First().Text.Should().Be("1. ");
    }

    [AvaloniaTheory]
    [InlineData("https://example.org/help", true)]
    [InlineData("http://example.org/help", true)]
    [InlineData("file:///tmp/help.bas", true)]
    [InlineData("command:run", false)]
    [InlineData("javascript:run", false)]
    public void Links_navigate_only_on_click_and_only_for_allowed_schemes(string target, bool clickable)
    {
        var opened = new List<Uri>();
        var panel = (StackPanel)HoverContentView.Render(new("markdown", $"[Help]({target})"), new TextEditor(), opened.Add);
        var text = (TextBlock)panel.Children.Single();
        opened.Should().BeEmpty();
        var links = text.Inlines!.OfType<InlineUIContainer>().Select(c => c.Child).OfType<HyperlinkButton>().ToArray();
        links.Should().HaveCount(clickable ? 1 : 0);
        if (clickable)
        {
            links[0].NavigateUri.Should().BeNull("only our scheme-filtered click handler may navigate");
            links[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            opened.Should().Equal(new Uri(target));
        }
        else text.Inlines!.OfType<Run>().Should().Contain(r => r.Text == "Help");
    }

    [AvaloniaFact]
    public void Every_advertised_format_has_a_renderer_and_plaintext_is_literal()
    {
        foreach (var format in HoverContentRenderer.Formats)
        {
            var panel = (StackPanel)HoverContentView.Render(new(format, "**word**"), new TextEditor(), _ => { });
            var text = (TextBlock)panel.Children.Single();
            if (format == "markdown") text.Inlines!.OfType<Bold>().Should().ContainSingle();
            else text.Inlines!.OfType<Run>().Should().Contain(r => r.Text == "**word**");
        }
    }
}
