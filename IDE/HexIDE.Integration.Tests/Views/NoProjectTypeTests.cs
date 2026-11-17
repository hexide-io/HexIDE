using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Projects;
using HexIDE.Utils;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.VisualTree;
using Avalonia.Automation;

namespace HexIDE.Integration.Tests.Views;

public class NoProjectTypeTests
{
    [AvaloniaTheory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task New_tab_explains_why_it_has_no_templates(bool withoutVb6, bool hiddenByProfile)
    {
        var previous = Static.WithoutVb6;
        try
        {
            Static.WithoutVb6 = withoutVb6;
            await using var owned = new RealComposition();
            var setup = owned.Composition;
            IProfileService profile = setup.ProfileService;
            if (hiddenByProfile)
            {
                profile = Substitute.For<IProfileService>();
                profile.AvailableProjectTypes.Returns(Array.Empty<IProjectTemplate>());
            }
            var vm = new NewProjectViewModel(Substitute.For<IWindowManager>(), Substitute.For<IRecentProjectsService>(),
                Substitute.For<ISettingsService>(), profile, setup.LocalizationService, setup.AddinProjectTemplateService);
            var view = new NewProjectView { DataContext = vm };
            var window = HexIDE.Integration.Tests.Automation.ToolbarAutomationNameTests.ShowMainView();
            window.Content = view;
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                var message = view.FindControl<TextBlock>("NoProjectTemplates")!;
                message.IsVisible.Should().Be(withoutVb6 || hiddenByProfile);
                if (withoutVb6)
                    message.Text.Should().Be("No project type that's installed makes new projects. Open an existing project instead.");
                else if (hiddenByProfile)
                    message.Text.Should().Be("This profile offers no new project templates. Open an existing project instead.");
                else
                    vm.Templates.Should().NotBeEmpty();
            }
            finally { window.Close(); }
        }
        finally { Static.WithoutVb6 = previous; }
    }

    [AvaloniaTheory]
    [InlineData("SaveProject")]
    [InlineData("SaveProjectAs")]
    [InlineData("Make")]
    [InlineData("Start")]
    [InlineData("AddForm")]
    [InlineData("AddModule")]
    [InlineData("AddClassModule")]
    [InlineData("Properties")]
    public async Task No_project_disables_project_commands_and_invoking_them_is_safe(string name)
    {
        var previous = Static.WithoutVb6;
        try
        {
            Static.WithoutVb6 = true;
            await using var owned = new RealComposition();
            var setup = owned.Composition;
            var vm = setup.Root;
            var main = new MainView { DataContext = vm, Content = new TextBox() };
            var window = new Window { Content = main };
            try
            {
                window.Show();
                Dispatcher.UIThread.RunJobs();
                using var log = new CapturedErrors();
                var makes = MakeOutcomes.Started;
                if (name == "SaveProject")
                {
                    ApplicationCommands.SaveProjectCommand.CanExecute(null, main).Should().BeFalse();
                    ApplicationCommands.SaveProjectCommand.Execute(null, main);
                }
                else
                {
                    DelegateCommand command = name switch
                    {
                        "SaveProjectAs" => vm.SaveProjectAsCommand,
                        "Make" => vm.MakeProjectCommand,
                        "Start" => vm.StartDefaultProjectCommand,
                        "AddForm" => vm.AddFormCommand,
                        "AddModule" => vm.AddModuleCommand,
                        "AddClassModule" => vm.AddClassModuleCommand,
                        _ => vm.ProjectPropertiesCommand,
                    };
                    command.CanExecute(null).Should().BeFalse();
                    var invoke = () => command.Execute(null);
                    invoke.Should().NotThrow();
                }
                Dispatcher.UIThread.RunJobs();
                setup.ProjectManager.LoadedProjects.Should().BeEmpty();
                setup.ProjectManager.StartupProject.Should().BeNull();
                window.OwnedWindows.Should().BeEmpty();
                MakeOutcomes.Started.Should().Be(makes);
                log.Errors.Should().BeEmpty();
            }
            finally { window.Close(); }
        }
        finally { Static.WithoutVb6 = previous; }
    }
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Empty_project_flyout_distinguishes_missing_types_from_profile_selection(bool withoutVb6)
    {
        var previous = Static.WithoutVb6;
        try
        {
            Static.WithoutVb6 = withoutVb6;
            await using var owned = new RealComposition();
            var vm = owned.Composition.Root;
            var profile = Substitute.For<IProfileService>();
            profile.AvailableProjectTypes.Returns(Array.Empty<IProjectTemplate>());
            typeof(MainViewViewModel).GetField("<Profile>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(vm, profile);
            var window = HexIDE.Integration.Tests.Automation.ToolbarAutomationNameTests.ShowMainView();
            try
            {
                var main = (MainView)window.Content!;
                main.DataContext = vm;
                var button = main.GetVisualDescendants().OfType<Button>()
                    .Single(b => AutomationProperties.GetAutomationId(b) == "Standard.AddProject");
                var flyout = (MenuFlyout)button.Flyout!;
                flyout.ShowAt(button);
                Dispatcher.UIThread.RunJobs();
                var presenter = (MenuFlyoutPresenter)flyout.Popup!.Child!;
                var item = presenter.Items.Cast<MenuItem>().Should().ContainSingle().Subject;
                item.IsEnabled.Should().BeFalse();
                item.Header.Should().Be(withoutVb6
                    ? "No project type that's installed makes new projects. Open an existing project instead."
                    : "This profile offers no new project templates. Open an existing project instead.");
                flyout.Hide();
            }
            finally { window.Close(); }
        }
        finally { Static.WithoutVb6 = previous; }
    }

    private sealed class CapturedErrors : ILogEventSink, IDisposable
    {
        private readonly ILogger previous = Log.Logger;
        public List<LogEvent> Errors { get; } = [];
        public CapturedErrors() => Log.Logger = new LoggerConfiguration().WriteTo.Sink(this).CreateLogger();
        public void Emit(LogEvent entry)
        {
            if (entry.Level >= LogEventLevel.Error) Errors.Add(entry);
        }
        public void Dispose() => Log.Logger = previous;
    }
}
