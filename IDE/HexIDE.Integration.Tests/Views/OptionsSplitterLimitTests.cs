using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Views;

// The Options splitter could give the category tree the whole width, and the page, an add-in's description with it,
// went to 0 (#4392). A third-party add-in asked for its description to keep at least 160 for a one-line slogan.
public class OptionsSplitterLimitTests
{
    private static readonly AddinInfo Addin =
        new("/addins/Slogan.dll", "Slogan", "V I V A T ♥ C U C U M I S ™", "1.0", "Tester");

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Dragging_the_splitter_to_the_right_edge_leaves_an_addins_description_160_wide(bool addinPage)
    {
        var (window, view, vm) = Open();
        try
        {
            if (addinPage)
            {
                vm.SelectedNode = vm.RootNodes.SelectMany(n => n.Children).Single(n => n.Title == Addin.Title);
                Dispatcher.UIThread.RunJobs();
            }

            Drag(window, view, toX: window.Width - 1);

            Description(view).Bounds.Width.Should().BeGreaterThanOrEqualTo(160);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Dragging_the_splitter_to_the_left_edge_leaves_the_tree_120_wide()
    {
        var (window, view, _) = Open();
        try
        {
            Drag(window, view, toX: 1);

            view.GetVisualDescendants().OfType<TreeView>().Single().Bounds.Width.Should().BeGreaterThanOrEqualTo(120);
            Description(view).Bounds.Width.Should().BeGreaterThanOrEqualTo(160);
        }
        finally { window.Close(); }
    }

    private static (Window Window, OptionsView View, OptionsViewModel Vm) Open()
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns("Classic");
        settings.ActiveKeymap.Returns("Default");
        settings.ActiveLanguage.Returns("en");
        settings.TabWidth.Returns(4);
        settings.GridWidth.Returns(8);
        settings.GridHeight.Returns(8);
        var theme = Substitute.For<IThemeService>();
        theme.ActiveTheme.Returns("Classic");
        theme.AvailableThemes.Returns(["Classic"]);
        var keymap = Substitute.For<IKeymapService>();
        keymap.ActiveKeymap.Returns("Default");
        keymap.AvailableKeymaps.Returns(["Default"]);
        var languageSwitch = Substitute.For<ILanguageSwitchService>();
        languageSwitch.ActiveLanguage.Returns("en");
        languageSwitch.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        languageSwitch.RegionsFor(Arg.Any<string>()).Returns([]);
        var registry = Substitute.For<IAddinRegistry>();
        registry.GetAll().Returns([Addin]);
        registry.GetStatus(Addin.AssemblyPath).Returns(AddinStatus.Loaded);

        var vm = new OptionsViewModel(settings, theme, keymap, languageSwitch, registry, new AddinOptionsService(),
            Substitute.For<IDeveloperModeService>(), new LocalizationService(), Substitute.For<IEventBus>(),
            "Str.Options.Node.AddIns");
        var view = new OptionsView { DataContext = vm };
        view.DataTemplates.Add(new ViewLocator());
        var window = new Window { Content = view, Width = 720, Height = 520 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm);
    }

    private static void Drag(Window window, OptionsView view, double toX)
    {
        window.UpdateLayout();
        var splitter = view.GetVisualDescendants().OfType<GridSplitter>().Single();
        var from = splitter.TranslatePoint(new Point(splitter.Bounds.Width / 2, splitter.Bounds.Height / 2), window)!.Value;
        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(from.WithX(toX));
        window.MouseUp(from.WithX(toX), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static TextBlock Description(OptionsView view) =>
        view.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text == Addin.Description && t.IsEffectivelyVisible);
}
