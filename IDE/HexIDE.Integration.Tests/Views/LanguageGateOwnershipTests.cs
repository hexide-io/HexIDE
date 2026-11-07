using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using HexIDE.Localization;

namespace HexIDE.Integration.Tests.Views;

public class LanguageGateOwnershipTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task GateHoldsOptionsEvenWhenMainWindowIsActive(int comboIndex)
    {
        var ide = new Window();
        var options = new Window();
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(new[] { ide, options });
        var loc = new LocalizationService();
        loc.Apply("en");
        var manager = new WindowManager(loc);
        var windows = Substitute.For<IWindowManager>();
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call => manager.ShowDialog(desktop, call.Arg<IDialog>()));
        var languages = new LanguageSwitchService(loc, windows);
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveLanguage.Returns("en");
        var vm = new LanguagePageViewModel(settings, languages, loc, () => { });
        var page = new LanguagePageView { DataContext = vm };
        options.Content = page;
        try
        {
            ide.Show();
            var optionsClosed = options.ShowDialog(ide);
            Dispatcher.UIThread.RunJobs();
            var combo = page.GetVisualDescendants().OfType<ComboBox>().ElementAt(comboIndex);
            combo.Focus();
            ide.Activate();
            var pending = languages.SwitchWithGateAsync("fr");
            Dispatcher.UIThread.RunJobs();
            var gate = ide.OwnedWindows.Concat(options.OwnedWindows).Single(w => w != options);

            gate.Owner.Should().BeSameAs(options, "an automation-opened gate must hold Options regardless of activation");
            gate.IsDialog.Should().BeTrue();
            var click = UiAutomationDriver.Interact(combo, "click", null);
            click.Success.Should().BeFalse("the combo is behind a modal gate");
            click.Error.Should().Contain(gate.Title);
            loc.ActiveLanguage.Should().Be("fr", "neither combo may apply a change under the gate");
            pending.IsCompleted.Should().BeFalse();

            if (comboIndex == 0)
            {
                // A programmatic request still reaches #622's guard and restores the actually applied selection.
                vm.SelectedLanguage = vm.AvailableLanguages.Single(l => l.Id == "de");
                vm.SelectedLanguage!.Id.Should().Be("fr");
                vm.SelectedRegion!.Id.Should().Be("fr");
                options.OwnedWindows.Should().ContainSingle();
            }

            ((LanguageRevertGateViewModel)gate.DataContext!).RevertCommand.Execute(null);
            (await pending).Should().BeFalse();
            loc.ActiveLanguage.Should().Be("en");
            combo.Focus().Should().BeTrue("closing the gate releases Options");
            options.Close();
            await optionsClosed;
        }
        finally
        {
            foreach (var gate in options.OwnedWindows.ToArray()) gate.Close();
            options.Close();
            foreach (var gate in ide.OwnedWindows.ToArray()) gate.Close();
            ide.Close();
        }
    }
}
