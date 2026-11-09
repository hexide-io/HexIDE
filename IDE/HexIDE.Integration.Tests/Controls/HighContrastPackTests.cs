using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views.Options;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Controls;

public sealed class HighContrastPackTests
{
    [AvaloniaTheory]
    [InlineData("HighContrastLight", false)]
    [InlineData("HighContrastDark", true)]
    public void SelectionAppliesThePackAndShowsALocalizedName(string id, bool dark)
    {
        var service = new ThemeService();
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns(id);
        var vm = new ThemePageViewModel(settings, service);
        var view = new ThemePageView { DataContext = vm };
        view.Resources["Str.Theme." + id] = "Translated name";
        var window = new Window { Content = view, Width = 700, Height = 400 };
        try
        {
            service.Apply(id);
            service.ActiveTheme.Should().Be(id);
            Application.Current!.RequestedThemeVariant.Should().Be(dark ? ThemeVariant.Dark : ThemeVariant.Light);
            ((ISolidColorBrush)Application.Current.FindResource(Classic.CommonControls.SystemColors.WindowBrushKey)!)
                .Color.Should().Be(dark ? Colors.Black : Colors.White);
            window.Show();
            var box = view.GetVisualDescendants().OfType<ComboBox>().Single();
            box.SelectedItem.Should().Be(id);
            box.GetVisualDescendants().OfType<TextBlock>().Should()
                .Contain(text => text.IsEffectivelyVisible && text.Text == "Translated name");
            view.Resources["Str.Theme." + id] = "New translation";
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            box.GetVisualDescendants().OfType<TextBlock>().Should()
                .Contain(text => text.IsEffectivelyVisible && text.Text == "New translation");
            vm.SaveToSettings();
            settings.Received().ActiveTheme = id;
        }
        finally
        {
            window.Close();
            service.Apply("Classic");
        }
    }
}
