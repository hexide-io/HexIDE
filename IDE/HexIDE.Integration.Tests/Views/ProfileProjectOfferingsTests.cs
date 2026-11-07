using System.Reflection;
using System.Runtime.CompilerServices;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Localization;
using HexIDE.Profiles;
using HexIDE.Projects;

namespace HexIDE.Integration.Tests.Views;

public class ProfileProjectOfferingsTests
{
    [AvaloniaFact]
    public void Empty_profile_dialog_explains_how_to_continue()
    {
        var profile = new ProfileService(new ProfileResolver(Path.GetTempPath(), Path.GetTempPath()).Resolve("vba"));
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            var dialog = new NewProjectViewModel(Substitute.For<IWindowManager>(), Substitute.For<IRecentProjectsService>(),
                Substitute.For<ISettingsService>(), profile, Substitute.For<ILocalizationService>());
            var view = new NewProjectView { DataContext = dialog };
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            var message = view.FindControl<TextBlock>("NoProjectTemplates");
            message.Should().NotBeNull();
            message!.IsVisible.Should().BeTrue();
            message.Text.Should().Be("This profile offers no new project templates. Open an existing project instead.");
            dialog.OkNew.CanExecute(null).Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("vb6")]
    [InlineData("vbaode")]
    [InlineData("vba")]
    [InlineData(null)]
    public void Dialog_and_flyout_offer_the_same_profile_templates(string? selector)
    {
        var profile = new ProfileService(new ProfileResolver(Path.GetTempPath(), Path.GetTempPath()).Resolve(selector));
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            var vm = (MainViewViewModel)RuntimeHelpers.GetUninitializedObject(typeof(MainViewViewModel));
            typeof(MainViewViewModel).GetField("<Profile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(vm, profile);
            var button = window.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "Standard.AddProject");
            ((MainView)window.Content!).DataContext = vm;
            var flyout = (MenuFlyout)button.Flyout!;
            flyout.ShowAt(button);
            Dispatcher.UIThread.RunJobs();
            var presenter = (MenuFlyoutPresenter)flyout.Popup!.Child!;
            var items = presenter.Items.Cast<MenuItem>().ToArray();
            items.Length.Should().Be(selector == "vba" ? 1 : selector == "vbaode" ? 1 : 8);
            var dialog = new NewProjectViewModel(Substitute.For<IWindowManager>(), Substitute.For<IRecentProjectsService>(),
                Substitute.For<ISettingsService>(), profile, Substitute.For<ILocalizationService>());
            items.Where(i => i.CommandParameter is IProjectTemplate).Select(i => i.CommandParameter)
                .Should().Equal(dialog.Templates.Select(t => t.Template));
            foreach (var item in items.Where(i => i.CommandParameter is IProjectTemplate))
            {
                item.IsEnabled.Should().Be(((IProjectTemplate)item.CommandParameter!).Supported);
                item.Command.Should().Be(ApplicationCommands.AddProjectCommand);
            }
            if (dialog.Templates.Count == 0)
            {
                items.Should().ContainSingle();
                items[0].IsEnabled.Should().BeFalse();
                items[0].Header.Should().Be("This profile offers no new project templates. Open an existing project instead.");
                dialog.OkNew.CanExecute(null).Should().BeFalse();
            }
            flyout.Hide();
        }
        finally { window.Close(); }
    }
}
