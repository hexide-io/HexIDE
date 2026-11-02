using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class AboutDialogCreditsTests
{
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
