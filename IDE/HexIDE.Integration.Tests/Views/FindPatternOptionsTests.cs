using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class FindPatternOptionsTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void PatternMatchingDisablesMatchCaseAndPreservesItsValue(bool matchCase)
    {
        var model = new FindReplaceViewModel(Substitute.For<IWindowManager>(),
            Substitute.For<IDocumentDockService>(), Substitute.For<ILocalizationService>()) { MatchCase = matchCase };
        var view = new FindReplaceView { DataContext = model };
        view.Resources["Str.Dialog.FindReplace.Option.MatchCase"] = "Match Case";
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var checkbox = view.GetVisualDescendants().OfType<CheckBox>().Single(c => c.Content as string == "Match Case");
            checkbox.IsEnabled.Should().BeTrue();

            model.UsePatternMatching = true;
            Dispatcher.UIThread.RunJobs();
            checkbox.IsEnabled.Should().BeFalse();
            checkbox.IsChecked.Should().Be(matchCase);
            model.MatchCase.Should().Be(matchCase);

            model.UsePatternMatching = false;
            Dispatcher.UIThread.RunJobs();
            checkbox.IsEnabled.Should().BeTrue();
            checkbox.IsChecked.Should().Be(matchCase);
        }
        finally { window.Close(); }
    }
}
