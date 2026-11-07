using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using HexIDE.Forms.Views;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.ViewModels;

public class CompletionPresentationTests
{
    private static CompletionItem Read(string fields) => JsonSerializer.Deserialize<CompletionItem>(
        "{\"label\":\"label\",\"kind\":1," + fields + "}", LspJsonContext.Default.Options)!;

    [AvaloniaTheory]
    [InlineData("\"documentation\":\"documentation\"")]
    [InlineData("\"documentation\":{\"kind\":\"markdown\",\"value\":\"documentation\"}")]
    public void DescriptionIncludesDetailAndDocumentation(string fields)
    {
        var data = new VbCompletionData(Read("\"detail\":\"signature\"," + fields), (_, _) => false);
        data.Description.Should().Be("signature\n\ndocumentation");
    }

    [AvaloniaTheory]
    [InlineData(9, "≡")]
    [InlineData(20, "⊡")]
    public void ProtocolKindsShowObjectBrowserGlyphs(int kind, string glyph)
    {
        var item = JsonSerializer.Deserialize<CompletionItem>($"{{\"label\":\"member\",\"kind\":{kind}}}",
            LspJsonContext.Default.Options)!;
        Enum.IsDefined(item.Kind).Should().BeTrue();
        var content = new VbCompletionData(item, (_, _) => false).Content;
        content.Should().BeOfType<StackPanel>().Which.Children.OfType<TextBlock>()
            .Select(block => block.Text).Should().Equal(glyph, "member");
    }

    [AvaloniaFact]
    public void FilterTextIsUsedWithoutChangingTheLabelOrInsertedText()
    {
        var data = new VbCompletionData(Read("\"filterText\":\"needle\""), (_, _) => false);
        data.Text.Should().Be("needle");
        data.ToString().Should().Be("label");
        var area = new AvaloniaEdit.Editing.TextArea { Document = new AvaloniaEdit.Document.TextDocument("nee") };
        data.Complete(area, new AvaloniaEdit.Document.TextSegment { StartOffset = 0, Length = 3 }, EventArgs.Empty);
        area.Document.Text.Should().Be("label");
    }

    [AvaloniaTheory]
    [InlineData("", "B,A", null)]
    [InlineData("B", "B,A", "B")]
    [InlineData("nee", "B,A", "A")]
    [InlineData("unmatched", "B,A", null)]
    public void ListUsesOrdinalServerRankingAndMatchingPreselection(string prefix, string visible, string? selected)
    {
        var theme = new Avalonia.Themes.Simple.SimpleTheme();
        var editorTheme = new Avalonia.Markup.Xaml.Styling.StyleInclude(new Uri("avares://AvaloniaEdit/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml")
        };
        Application.Current!.Styles.Add(theme);
        Application.Current.Styles.Add(editorTheme);
        var editor = new TextEditor { Text = prefix };
        editor.CaretOffset = prefix.Length;
        var host = new Window { Content = editor, Width = 500, Height = 300 };
        host.Show();
        var popup = new CompletionWindow(editor.TextArea);
        try
        {
            host.UpdateLayout();
            var items = JsonSerializer.Deserialize<CompletionItem[]>("""
                [{"label":"A","kind":1,"sortText":"b","preselect":true,"filterText":"needle"},
                 {"label":"B","kind":1,"sortText":"a"}]
                """, LspJsonContext.Default.Options)!;
            CodeEditorView.FillCompletionList(popup, items, (_, _) => false);
            popup.Show();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            popup.CompletionList.CompletionData.Select(data => data.ToString()).Should().Equal("B", "A");
            popup.CompletionList.CurrentList.Select(data => data.ToString()).Should().Equal(visible.Split(','));
            // Parenthesised: `SelectedItem?.ToString().Should()` would skip the assertion whenever nothing is selected.
            (popup.CompletionList.SelectedItem?.ToString()).Should().Be(selected);
        }
        finally
        {
            popup.Close();
            host.Close();
            Application.Current.Styles.Remove(editorTheme);
            Application.Current.Styles.Remove(theme);
        }
    }
}
