using System.Linq;
using AwesomeAssertions.Execution;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.TextFormatting;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class AboutDialogCreditsTests
{
    [AvaloniaTheory]
    [InlineData("Title")]
    [InlineData("SubTitle")]
    [InlineData("Copyright")]
    [InlineData("RootFingerprint")]
    [InlineData("FirstPartyFingerprint")]
    public void LatinDataUnderHebrew_IsLeftToRightAndFits(string member)
    {
        var localization = new LocalizationService();
        var previousLanguage = localization.ActiveLanguage;
        var vm = new AboutDialogViewModel(new AboutDialogOptions
        {
            Title = "HexIDE", SubTitle = "A cross-platform, VB6-compatible IDE",
            Copyright = "© 2026 The HexIDE Authors. Not affiliated with, endorsed by, or sponsored by Microsoft."
        }, localization);
        var window = new WindowManager(localization).MakeDialogWindow("About",
            new HexIDE.Forms.Views.AboutDialog { DataContext = vm });
        try
        {
            localization.Apply("he");
            window.FlowDirection = localization.FlowDirection;
            window.Show();
            foreach (var expander in window.GetLogicalDescendants().OfType<Expander>())
                expander.IsExpanded = true;
            window.UpdateLayout();
            var value = (string)typeof(AboutDialogViewModel).GetProperty(member)!.GetValue(vm)!;
            var text = window.GetLogicalDescendants().OfType<TextBlock>().Single(t => t.Text == value);
            if (member.EndsWith("Fingerprint"))
            {
                using var digit = new TextLayout("0", new Typeface(text.FontFamily), text.FontSize, Brushes.Black);
                // A wrap after a group plus a space: Consolas at 11pt hits this at 300 DIPs.
                ((StackPanel)text.Parent!).MaxWidth = 49.5 * digit.Width;
                window.UpdateLayout();
            }
            using var assertions = new AssertionScope(member);
            text.FlowDirection.Should().Be(FlowDirection.LeftToRight);
            // The copyright line stays centred, as it is in every language; the rest align to the mirrored side.
            text.HorizontalAlignment.Should().Be(member == "Copyright" ? HorizontalAlignment.Center : HorizontalAlignment.Left,
                "the RTL parent mirrors logical left to its right edge");
            text.Bounds.Width.Should().BeGreaterThan(0);
            text.TextLayout.Width.Should().BeLessThanOrEqualTo(text.Bounds.Width,
                "every glyph must fit inside the text block");
            text.DesiredSize.Width.Should().BeLessThanOrEqualTo(text.Bounds.Width);
            for (var index = 0; index < value.Length; index++)
            {
                if (char.IsWhiteSpace(value[index])) continue;
                var glyph = text.TextLayout.HitTestTextPosition(index);
                glyph.Left.Should().BeGreaterThanOrEqualTo(0);
                glyph.Right.Should().BeLessThanOrEqualTo(text.Bounds.Width,
                    $"visible character {index} must not be clipped (trailing spaces may overflow)");
            }
        }
        finally
        {
            window.Close();
            localization.Apply(previousLanguage);
        }
    }

    [AvaloniaFact]
    public void CreditsExpander_ShowsOriginalThanksThenBrandAddition()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var vm = new AboutDialogViewModel(new AboutDialogOptions
        {
            Title = "Example IDE", AdditionalCredits = ["Example brand contributors"]
        }, localization);
        var window = new Window { Content = new HexIDE.Forms.Views.AboutDialog { DataContext = vm } };
        window.Show();
        try
        {
            var credits = window.GetLogicalDescendants().OfType<Expander>()
                .Single(e => Equals(e.Header, "Credits"));
            credits.IsVisible.Should().BeTrue();
            credits.IsExpanded.Should().BeTrue();
            window.UpdateLayout();
            var text = credits.GetLogicalDescendants().OfType<TextBlock>().Select(t => t.Text).ToArray();
            text.Should().Contain(vm.Credits[0]).And.Contain("Example brand contributors");
            credits.Bounds.Height.Should().BeGreaterThan(0);
        }
        finally { window.Close(); }
    }
}
