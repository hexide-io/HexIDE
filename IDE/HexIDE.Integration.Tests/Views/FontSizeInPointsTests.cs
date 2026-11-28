using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using HexIDE.Controls;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A VB6 font's size is in points, and is drawn at 96 pixels to the inch (#910). It was handed to Avalonia as pixels, so
/// every VB6 font drew a quarter too small, and the default was held as 11, the pixels its 8.25 points draw at.
/// </summary>
public class FontSizeInPointsTests
{
    [AvaloniaFact]
    public void The_Font_dialog_keeps_a_fractional_size_and_what_it_has_no_say_over()
    {
        var font = new VBFont("Tahoma", 9.6, charset: 128, underline: true, strikethrough: true, weight: 600);
        static FontDialogResult Answer(string family, FontWeight weight, double size) =>
            new(new FontFamily(family), FontStyle.Normal, weight, size);

        // OK on what it opened with: nothing changes, where the size used to come back 9 and the rest reset.
        PropertyFontBox.Chosen(font, Answer("Tahoma", FontWeight.Normal, 9.6)).Should().Be(font);
        // Bold, chosen: the weight is the bold one, and the rest is kept.
        PropertyFontBox.Chosen(font, Answer("Tahoma", FontWeight.Bold, 9.6)).Should()
            .Be(new VBFont("Tahoma", 9.6, charset: 128, underline: true, strikethrough: true, weight: 700));
        // A new family takes charset 0, as it did.
        PropertyFontBox.Chosen(font, Answer("Arial", FontWeight.Normal, 8.25)).Should()
            .Be(new VBFont("Arial", 8.25, underline: true, strikethrough: true, weight: 600));
    }

    [AvaloniaFact]
    public void The_Font_dialog_offers_the_font_s_own_size_and_draws_its_sample_in_points()
    {
        var odd = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 8.25));
        odd.FontSizes.Should().ContainInOrder(8, 8.25, 9).And.BeInAscendingOrder();
        odd.SelectedSize.Should().Be(8.25);
        odd.PreviewFontSize.Should().Be(11);

        var usual = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 12));
        usual.FontSizes.Should().Equal(8, 9, 10, 11, 12, 14, 16, 18, 20, 22, 24, 26, 28, 36, 48, 72);
        usual.PreviewFontSize.Should().Be(16);

        // And the sample redraws when another size or style is chosen. A preview that read its property's field was
        // never told of the change, so the sample kept the size and style the dialog opened with.
        var raised = new System.Collections.Generic.List<string?>();
        usual.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        usual.SelectedSize = 24;
        usual.SelectedStyle = "Bold Italic";
        raised.Should().Contain(nameof(FontDialogViewModel.PreviewFontSize))
            .And.Contain(nameof(FontDialogViewModel.PreviewFontStyle))
            .And.Contain(nameof(FontDialogViewModel.PreviewFontWeight));
        usual.PreviewFontSize.Should().Be(32);
        usual.PreviewFontStyle.Should().Be(FontStyle.Italic);
        usual.PreviewFontWeight.Should().Be(FontWeight.Bold);
    }

    [AvaloniaFact]
    public void The_Font_dialog_s_sample_draws_in_the_face_chosen()
    {
        var vm = new FontDialogViewModel(new FontDialogResult(new FontFamily("Arial"), FontStyle.Normal, FontWeight.Normal, 12));
        var window = new Window { Content = new HexIDE.Forms.Views.FontDialog { DataContext = vm } };
        window.Show();
        try
        {
            var sample = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == "AaBbCcDdEe 0123");
            sample.FontFamily.ToString().Should().Be("Arial", "it opened on Arial");

            var chosen = vm.FontFamilies.First(f => f != "Arial");
            vm.SelectedFamily = chosen;
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            sample.FontFamily.ToString().Should().Be(chosen, "another offered family was chosen");
        }
        finally { window.Close(); }
    }
}
